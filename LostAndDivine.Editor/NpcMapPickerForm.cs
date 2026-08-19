using System.Drawing.Drawing2D;
using System.Text.Json;
using LostAndDivine.Shared.Tiled;

namespace LostAndDivine.Editor;

/// <summary>
/// Выбор клетки на Tiled-карте для размещения NPC. Показывает тайловую подложку карты
/// (zone_*.tmj и секторы открытого мира Sectors\*.tmj), маркеры уже размещённых NPC
/// и текущее положение размещаемого NPC. Клик по клетке + «Разместить здесь» —
/// пишет объект в слой «NPC» карты через TiledNpcWriter.
/// </summary>
public sealed class NpcMapPickerForm : Form
{
    private readonly string _npcId;
    private readonly string _npcType;
    private readonly string _contentDir;

    private readonly ComboBox _mapCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _mapLabel = new() { Text = "Карта:", Dock = DockStyle.Left, Width = 60, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _info = new() { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _placeBtn;
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly PictureBox _view = new() { BackColor = Color.Black };

    private MapEntry? _current;
    private Bitmap? _tiles;
    private readonly Dictionary<string, Bitmap> _tilesetImages = new();
    private double _scale = 1;
    private (int X, int Y)? _hoverCell;
    private (int X, int Y)? _selectedCell;
    private double _minScale = 0.05;
    private double _maxScale = 1;

    public string PlacedZoneId { get; private set; } = "";
    public int PlacedTileX { get; private set; }
    public int PlacedTileY { get; private set; }

    public NpcMapPickerForm(string npcId, string npcName, string npcType, string contentDir)
    {
        _npcId = npcId;
        _npcType = npcType;
        _contentDir = contentDir;
        Text = $"Размещение NPC {npcId} ({npcName}) — выбор клетки";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 700);
        MinimumSize = new Size(640, 480);
        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; Place(); }
            else if (e.KeyCode == Keys.Escape) { e.Handled = true; DialogResult = DialogResult.Cancel; }
        };

        // ── верх: выбор карты ──
        var top = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 4) };
        var hint = new Label
        {
            Text = "Клик по клетке — выбор, колесо — масштаб, двойной клик — разместить",
            Dock = DockStyle.Right,
            Width = 320,
            TextAlign = ContentAlignment.MiddleRight
        };
        top.Controls.Add(_mapCombo);
        top.Controls.Add(_mapLabel);
        top.Controls.Add(hint);
        top.Controls.SetChildIndex(hint, 0);
        top.Controls.SetChildIndex(_mapLabel, 2);

        // ── центр: прокручиваемая карта ──
        _scroll.Controls.Add(_view);
        _mapCombo.SelectedIndexChanged += (s, e) => SwitchMap();
        _view.MouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _selectedCell = CellFrom(e.Location);
            _placeBtn.Enabled = _selectedCell.HasValue;
            UpdateInfo();
            _view.Invalidate();
        };
        _view.MouseDoubleClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _selectedCell = CellFrom(e.Location);
            Place();
        };
        _view.MouseMove += (s, e) =>
        {
            var cell = CellFrom(e.Location);
            if (cell != _hoverCell)
            {
                _hoverCell = cell;
                UpdateInfo();
                _view.Invalidate();
            }
        };
        _view.MouseEnter += (s, e) => _view.Focus();
        _view.MouseWheel += (s, e) => Zoom(e.Delta > 0 ? 1.25 : 0.8);
        _view.Paint += View_Paint;

        // ── низ: инфо + кнопки ──
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6, 4, 6, 4) };
        _placeBtn = new Button
        {
            Text = "Разместить здесь",
            Dock = DockStyle.Right,
            Width = 160,
            Enabled = false,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _placeBtn.Click += (s, e) => Place();
        var cancel = new Button { Text = "Отмена", Dock = DockStyle.Right, Width = 90, Cursor = Cursors.Hand };
        cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
        bottom.Controls.Add(_placeBtn);
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(_info);
        bottom.Controls.SetChildIndex(_info, 2);

        Controls.Add(_scroll);
        Controls.Add(top);
        Controls.Add(bottom);

        LoadMaps();
        if (_mapCombo.Items.Count == 0)
        {
            _info.Text = "Карты не найдены в папке контента клиента.";
            return;
        }
        _mapCombo.SelectedIndex = 0;
    }

    // ── список карт ──────────────────────────────────────────────────────────

    private void LoadMaps()
    {
        var maps = new List<MapEntry>();
        if (Directory.Exists(_contentDir))
        {
            foreach (var f in Directory.GetFiles(_contentDir, "zone_*.tmj", SearchOption.TopDirectoryOnly))
            {
                string id = Path.GetFileNameWithoutExtension(f)["zone_".Length..];
                if (string.Equals(id, "main", StringComparison.OrdinalIgnoreCase)) continue; // zone_main сервером не грузится
                maps.Add(new MapEntry { Display = $"Зона {id}", FilePath = f, ZoneId = id });
            }
            var sectors = Path.Combine(_contentDir, "Sectors");
            if (Directory.Exists(sectors))
            {
                foreach (var f in Directory.GetFiles(sectors, "*.tmj", SearchOption.TopDirectoryOnly))
                {
                    string fname = Path.GetFileNameWithoutExtension(f);
                    int sep = fname.IndexOf('_');
                    if (sep <= 0 || sep == fname.Length - 1) continue;
                    if (!int.TryParse(fname.AsSpan(0, sep), out int col)) continue;
                    if (!int.TryParse(fname.AsSpan(sep + 1), out int row)) continue;
                    maps.Add(new MapEntry
                    {
                        Display = $"Открытый мир: сектор {col}_{row}",
                        FilePath = f,
                        ZoneId = "main",
                        SectorCol = col,
                        SectorRow = row
                    });
                }
            }
        }

        // Пустые секторы (слой земли без тайлов) — шаблоны, сервер их пропускает.
        foreach (var m in maps)
        {
            var data = ParseMap(m);
            m.Data = data;
            if (m.IsSector && (data == null || !data.HasGroundTiles)) continue;
            _mapCombo.Items.Add(m);
        }
        _mapCombo.DisplayMember = nameof(MapEntry.Display);
    }

    private MapData? ParseMap(MapEntry entry)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(entry.FilePath));
            var root = doc.RootElement;
            var data = new MapData
            {
                Width = root.GetProperty("width").GetInt32(),
                Height = root.GetProperty("height").GetInt32(),
                TileW = root.TryGetProperty("tilewidth", out var tw) && tw.ValueKind == JsonValueKind.Number ? tw.GetInt32() : 64,
                TileH = root.TryGetProperty("tileheight", out var th) && th.ValueKind == JsonValueKind.Number ? th.GetInt32() : 64
            };

            if (root.TryGetProperty("tilesets", out var tilesets))
            {
                foreach (var ts in tilesets.EnumerateArray())
                {
                    data.Tilesets.Add(new TsInfo
                    {
                        FirstGid = ts.GetProperty("firstgid").GetInt32(),
                        Columns = ts.TryGetProperty("columns", out var c) ? c.GetInt32() : 0,
                        Tw = ts.TryGetProperty("tilewidth", out var tw2) ? tw2.GetInt32() : data.TileW,
                        Th = ts.TryGetProperty("tileheight", out var th2) ? th2.GetInt32() : data.TileH,
                        Image = ts.TryGetProperty("image", out var img) ? img.GetString() ?? "" : ""
                    });
                }
            }

            if (root.TryGetProperty("layers", out var layers))
            {
                var npcTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "npc", "merchant", "board", "instance_portal", "dummy", "storage"
                };
                foreach (var layer in layers.EnumerateArray())
                {
                    string ltype = layer.TryGetProperty("type", out var lt) ? lt.GetString() ?? "" : "";
                    if (string.Equals(ltype, "tilelayer", StringComparison.OrdinalIgnoreCase) &&
                        layer.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array &&
                        arr.GetArrayLength() == data.Width * data.Height)
                    {
                        var tileData = new long[data.Width * data.Height];
                        int i = 0;
                        foreach (var v in arr.EnumerateArray()) tileData[i++] = v.GetInt64();
                        data.TileLayers.Add(tileData);
                        if (data.HasGroundTiles == false)
                            data.HasGroundTiles = tileData.Any(t => (t & 0x1FFFFFFF) != 0);
                    }
                    else if (string.Equals(ltype, "objectgroup", StringComparison.OrdinalIgnoreCase) &&
                             layer.TryGetProperty("objects", out var objs))
                    {
                        foreach (var o in objs.EnumerateArray())
                        {
                            string otype = o.TryGetProperty("type", out var ot) ? ot.GetString() ?? "" : "";
                            if (string.Equals(otype, "portal", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!npcTypes.Contains(otype)) continue;
                            string name = o.TryGetProperty("name", out var on) ? on.GetString() ?? "" : "";
                            double ox = o.TryGetProperty("x", out var oxp) && oxp.ValueKind == JsonValueKind.Number ? oxp.GetDouble() : 0;
                            double oy = o.TryGetProperty("y", out var oyp) && oyp.ValueKind == JsonValueKind.Number ? oyp.GetDouble() : 0;
                            bool point = o.TryGetProperty("point", out var pp) && pp.ValueKind == JsonValueKind.True;
                            double w = o.TryGetProperty("width", out var wv) && wv.ValueKind == JsonValueKind.Number ? wv.GetDouble() : 0;
                            double h = o.TryGetProperty("height", out var hv) && hv.ValueKind == JsonValueKind.Number ? hv.GetDouble() : 0;
                            int tx = point ? (int)(ox / data.TileW) : (int)((ox + w / 2) / data.TileW);
                            int ty = point ? (int)(oy / data.TileH) : (int)((oy + h / 2) / data.TileH);
                            if (tx < 0 || ty < 0 || tx >= data.Width || ty >= data.Height) continue;
                            data.Markers.Add(new NpcMarker { X = tx, Y = ty, Name = name, Type = otype });
                        }
                    }
                }
            }
            return data;
        }
        catch
        {
            return null;
        }
    }

    // ── переключение карты ───────────────────────────────────────────────────

    private void SwitchMap()
    {
        _current = _mapCombo.SelectedItem as MapEntry;
        _selectedCell = null;
        _hoverCell = null;
        _placeBtn.Enabled = false;
        if (_current == null || _current.Data == null)
        {
            _view.Image = null;
            _info.Text = "Не удалось прочитать карту.";
            return;
        }

        var map = _current.Data;
        double pxW = map.Width * map.TileW;
        double pxH = map.Height * map.TileH;
        int vw = Math.Max(300, _scroll.ClientSize.Width - 24);
        int vh = Math.Max(200, _scroll.ClientSize.Height - 24);
        double fit = Math.Min(vw / pxW, vh / pxH);
        _maxScale = Math.Min(1.0, 2048.0 / pxW);
        _maxScale = Math.Min(_maxScale, 2048.0 / pxH);
        _minScale = Math.Max(fit * 0.2, 0.02);
        _scale = Math.Clamp(fit, _minScale, _maxScale);
        RenderTiles();
        UpdateInfo();
    }

    private void Zoom(double factor)
    {
        if (_current == null) return;
        double ns = Math.Clamp(_scale * factor, _minScale, _maxScale);
        if (Math.Abs(ns - _scale) < 0.0001) return;
        _scale = ns;
        RenderTiles();
        UpdateInfo();
    }

    private void RenderTiles()
    {
        var map = _current?.Data;
        if (map == null) return;
        _tiles?.Dispose();
        _tiles = null;

        int bw = Math.Max(1, (int)Math.Ceiling(map.Width * map.TileW * _scale));
        int bh = Math.Max(1, (int)Math.Ceiling(map.Height * map.TileH * _scale));
        var bmp = new Bitmap(bw, bh);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Black);
            double tilePx = map.TileW * _scale;
            foreach (var layer in map.TileLayers)
            {
                for (int ty = 0; ty < map.Height; ty++)
                {
                    for (int tx = 0; tx < map.Width; tx++)
                    {
                        long gid = layer[ty * map.Width + tx] & 0x1FFFFFFF;
                        if (gid == 0) continue;
                        var ts = FindTileset(map, gid);
                        if (ts == null || ts.Columns <= 0) continue;
                        int local = (int)(gid - ts.FirstGid);
                        int sx = (local % ts.Columns) * ts.Tw;
                        int sy = (local / ts.Columns) * ts.Th;
                        var img = GetTilesetImage(ts, map);
                        if (img == null) continue;
                        int dx = (int)(tx * tilePx);
                        int dy = (int)(ty * tilePx);
                        int dw = Math.Max(1, (int)(map.TileW * _scale));
                        int dh = Math.Max(1, (int)(map.TileH * _scale));
                        g.DrawImage(img, new Rectangle(dx, dy, dw, dh),
                            new Rectangle(sx, sy, ts.Tw, ts.Th), GraphicsUnit.Pixel);
                    }
                }
            }
        }
        _tiles = bmp;
        _view.Image = bmp;
        _view.Size = new Size(bw, bh);
        _scroll.AutoScrollPosition = Point.Empty;
    }

    private TsInfo? FindTileset(MapData map, long gid)
    {
        TsInfo? best = null;
        foreach (var ts in map.Tilesets)
        {
            if (ts.FirstGid <= gid && (best == null || ts.FirstGid > best.FirstGid))
                best = ts;
        }
        return best;
    }

    private Bitmap? GetTilesetImage(TsInfo ts, MapData map)
    {
        if (string.IsNullOrWhiteSpace(ts.Image)) return null;
        if (_tilesetImages.TryGetValue(ts.Image, out var cached)) return cached;
        string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_current!.FilePath) ?? ".", ts.Image.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new Bitmap(path);
            _tilesetImages[ts.Image] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private (int X, int Y)? CellFrom(Point p)
    {
        var map = _current?.Data;
        if (map == null) return null;
        int x = (int)(p.X / (map.TileW * _scale));
        int y = (int)(p.Y / (map.TileH * _scale));
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return null;
        return (x, y);
    }

    private void UpdateInfo()
    {
        var map = _current?.Data;
        if (map == null)
        {
            _info.Text = "";
            return;
        }
        var cell = _selectedCell ?? _hoverCell;
        string pos = "наведите курсор на клетку";
        if (cell.HasValue)
        {
            string global = _current.IsSector
                ? $"глобально: {_current.SectorCol * 100 + cell.Value.X},{_current.SectorRow * 100 + cell.Value.Y}"
                : "зональные координаты";
            pos = $"клетка {cell.Value.X},{cell.Value.Y} ({global})";
        }
        string portalNote = string.Equals(_npcType, "instance_portal", StringComparison.OrdinalIgnoreCase)
            ? " Внимание: позиция портала на сервере берётся из первого объекта типа instance_portal."
            : "";
        _info.Text = $"{_current.Display} · {map.Width}×{map.Height} · {pos}{portalNote}";
    }

    private void View_Paint(object? sender, PaintEventArgs e)
    {
        var map = _current?.Data;
        if (map == null || _tiles == null) return;
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        double tilePx = map.TileW * _scale;

        foreach (var m in map.Markers)
        {
            float cx = (float)(m.X * tilePx + tilePx / 2);
            float cy = (float)(m.Y * tilePx + tilePx / 2);
            float r = Math.Max(3f, (float)(tilePx * 0.2));
            using var brush = new SolidBrush(Color.FromArgb(200, MarkerColor(m.Type)));
            g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
            if (tilePx >= 10)
            {
                using var font = new Font("Segoe UI", 7.5f);
                using var tb = new SolidBrush(Color.FromArgb(230, Color.White));
                g.DrawString(m.Name, font, tb, cx + r + 1, cy - r);
            }
            if (string.Equals(m.Name, _npcId, StringComparison.OrdinalIgnoreCase))
            {
                using var pen = new Pen(Color.Yellow, 2f);
                g.DrawRectangle(pen, (float)(m.X * tilePx), (float)(m.Y * tilePx), (float)tilePx, (float)tilePx);
            }
        }

        if (_hoverCell.HasValue && _hoverCell.Value != _selectedCell)
        {
            using var b = new SolidBrush(Color.FromArgb(60, Color.White));
            g.FillRectangle(b, (float)(_hoverCell.Value.X * tilePx), (float)(_hoverCell.Value.Y * tilePx), (float)tilePx, (float)tilePx);
        }
        if (_selectedCell.HasValue)
        {
            using var b = new SolidBrush(Color.FromArgb(90, Color.Lime));
            using var p = new Pen(Color.Lime, 2f);
            g.FillRectangle(b, (float)(_selectedCell.Value.X * tilePx), (float)(_selectedCell.Value.Y * tilePx), (float)tilePx, (float)tilePx);
            g.DrawRectangle(p, (float)(_selectedCell.Value.X * tilePx), (float)(_selectedCell.Value.Y * tilePx), (float)tilePx, (float)tilePx);
        }
    }

    private static Color MarkerColor(string type) => type.ToLowerInvariant() switch
    {
        "merchant" => Color.Gold,
        "board" => Color.DodgerBlue,
        "instance_portal" => Color.MediumPurple,
        "dummy" => Color.Gray,
        "storage" => Color.Peru,
        _ => Color.LimeGreen
    };

    private void Place()
    {
        var map = _current?.Data;
        if (map == null) return;
        if (!_selectedCell.HasValue) return;
        var (x, y) = _selectedCell.Value;
        try
        {
            TiledNpcWriter.RemoveFromAllMaps(_contentDir, _npcId);
            TiledNpcWriter.Upsert(_current.FilePath, _npcId, _npcType, x, y, map.TileW, map.TileH);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось записать карту:\n" + ex.Message,
                "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        PlacedZoneId = _current.ZoneId;
        PlacedTileX = x;
        PlacedTileY = y;
        DialogResult = DialogResult.OK;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tiles?.Dispose();
            foreach (var img in _tilesetImages.Values) img.Dispose();
            _tilesetImages.Clear();
        }
        base.Dispose(disposing);
    }

    // ── модели ───────────────────────────────────────────────────────────────

    private sealed class MapEntry
    {
        public string Display { get; init; } = "";
        public string FilePath { get; init; } = "";
        public string ZoneId { get; init; } = "";
        public int SectorCol { get; init; }
        public int SectorRow { get; init; }
        public bool IsSector => SectorRow >= 0;
        public MapData? Data { get; set; }
        public override string ToString() => Display;
    }

    private sealed class MapData
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public int TileW { get; init; }
        public int TileH { get; init; }
        public List<TsInfo> Tilesets { get; } = new();
        public List<long[]> TileLayers { get; } = new();
        public List<NpcMarker> Markers { get; } = new();
        public bool HasGroundTiles { get; set; }
    }

    private sealed class TsInfo
    {
        public int FirstGid { get; init; }
        public int Columns { get; init; }
        public int Tw { get; init; }
        public int Th { get; init; }
        public string Image { get; init; } = "";
    }

    private sealed class NpcMarker
    {
        public int X { get; init; }
        public int Y { get; init; }
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
    }
}