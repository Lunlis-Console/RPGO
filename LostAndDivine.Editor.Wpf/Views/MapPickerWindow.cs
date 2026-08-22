using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Tiled;

namespace LostAndDivine.Editor.Views;

/// <summary>
/// Выбор клетки на Tiled-карте для размещения NPC (WPF-порт NpcMapPickerForm).
/// Тайлы рендерятся в Bitmap (GDI+, как в WinForms-версии) и показываются в Image;
/// маркеры NPC, подсветка курсора и выбранной клетки — WPF-оверлей.
/// </summary>
public sealed class MapPickerWindow : Window
{
    private readonly string _npcId;
    private readonly string _npcType;
    private readonly string _contentDir;

    private readonly ComboBox _mapCombo = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _info = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray };
    private readonly Button _placeBtn;
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = System.Windows.Media.Brushes.Black };
    private readonly System.Windows.Controls.Image _view = new() { Stretch = Stretch.None };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };

    private MapEntry? _current;
    private Bitmap? _tiles;
    private BitmapSource? _tilesSource;
    private readonly Dictionary<string, Bitmap> _tilesetImages = new();
    private double _scale = 1;
    private (int X, int Y)? _hoverCell;
    private (int X, int Y)? _selectedCell;
    private double _minScale = 0.05;
    private double _maxScale = 1;

    public string PlacedZoneId { get; private set; } = "";
    public int PlacedTileX { get; private set; }
    public int PlacedTileY { get; private set; }
    /// <summary>true, если пользователь нажал «Очистить» — NPC удалён со всех карт.</summary>
    public bool Cleared { get; private set; }

    public MapPickerWindow(string npcId, string npcName, string npcType, string contentDir)
    {
        _npcId = npcId;
        _npcType = npcType;
        _contentDir = contentDir;
        Title = $"Размещение NPC {npcId} ({npcName}) — выбор клетки";
        Width = 1100;
        Height = 700;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── верх ──
        var top = new DockPanel { Margin = new Thickness(8, 6, 8, 4) };
        var mapLabel = new TextBlock { Text = "Карта:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(mapLabel, Dock.Left);
        var hint = new TextBlock
        {
            Text = "Клик по клетке — выбор, колесо — прокрутка, Ctrl+колесо — масштаб, двойной клик — разместить",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = System.Windows.Media.Brushes.Gray
        };
        top.Children.Add(hint);
        top.Children.Add(_mapCombo);
        top.Children.Add(mapLabel);
        Grid.SetRow(top, 0);

        // ── центр ──
        var canvasHost = new Grid();
        canvasHost.Children.Add(_view);
        canvasHost.Children.Add(_overlay);
        _scroll.Content = canvasHost;
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.NearestNeighbor);
        _mapCombo.SelectionChanged += (s, e) => SwitchMap();
        _view.MouseLeftButtonUp += (s, e) =>
        {
            var cell = CellFrom(e.GetPosition(_view));
            if (!cell.HasValue) return;
            _selectedCell = cell;
            _placeBtn.IsEnabled = true;
            DrawOverlay();
            UpdateInfo();
        };
        _view.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Place(); } };
        _view.PreviewMouseMove += (s, e) =>
        {
            var cell = CellFrom(e.GetPosition(_view));
            if (cell != _hoverCell)
            {
                _hoverCell = cell;
                DrawOverlay();
                UpdateInfo();
            }
        };
        _view.PreviewMouseWheel += (s, e) =>
        {
            // Зум только при зажатом Ctrl. Иначе колесо панорамирует карту —
            // это просто прокрутка одного Bitmap'а и работает плавно. Раньше
            // колесо вызывало Zoom -> полный ререндер тайлов на каждый «тик»,
            // из-за чего скролл лагал.
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                Zoom(e.Delta > 0 ? 1.25 : 0.8);
                e.Handled = true;
            }
        };
        Grid.SetRow(_scroll, 1);

        // ── низ ──
        var bottom = new DockPanel { Margin = new Thickness(8, 4, 8, 8) };
        _placeBtn = new Button { Content = "Разместить здесь", Width = 160, Height = 32, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = false };
        _placeBtn.Click += (s, e) => Place();
        var clear = new Button { Content = "Очистить размещение", Width = 170, Height = 32, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        clear.Click += (s, e) => ClearPlacement();
        var cancel = new Button { Content = "Отмена", Width = 100, Height = 32, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (s, e) => Close();
        DockPanel.SetDock(_info, Dock.Left);
        bottom.Children.Add(_placeBtn);
        bottom.Children.Add(clear);
        bottom.Children.Add(cancel);
        bottom.Children.Add(_info);
        Grid.SetRow(bottom, 2);

        grid.Children.Add(top);
        grid.Children.Add(_scroll);
        grid.Children.Add(bottom);
        Content = grid;

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
                if (string.Equals(id, "main", StringComparison.OrdinalIgnoreCase)) continue;
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
                        ZoneId = ZoneIds.Main,
                        SectorCol = col,
                        SectorRow = row
                    });
                }
            }
        }

        foreach (var m in maps)
        {
            var data = ParseMap(m);
            m.Data = data;
            if (m.IsSector && (data == null || !data.HasGroundTiles)) continue;
            _mapCombo.Items.Add(m);
        }
        _mapCombo.DisplayMemberPath = nameof(MapEntry.Display);
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
                    "npc", "merchant", "board", "instance_portal", "dummy", "storage", "wanderer"
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
                        if (!data.HasGroundTiles)
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
        _placeBtn.IsEnabled = false;
        if (_current == null || _current.Data == null)
        {
            _view.Source = null;
            _info.Text = "Не удалось прочитать карту.";
            return;
        }

        var map = _current.Data;
        double pxW = map.Width * map.TileW;
        double pxH = map.Height * map.TileH;
        double vw = Math.Max(300, _scroll.ViewportWidth - 24);
        double vh = Math.Max(200, _scroll.ViewportHeight - 24);
        double fit = Math.Min(vw / pxW, vh / pxH);
        _maxScale = Math.Min(1.0, 2048.0 / pxW);
        _maxScale = Math.Min(_maxScale, 2048.0 / pxH);
        _minScale = Math.Max(fit * 0.2, 0.02);
        _scale = Math.Clamp(fit, _minScale, _maxScale);
        RenderTiles();
        _scroll.ScrollToHome();
        UpdateInfo();
    }

    private void Zoom(double factor)
    {
        if (_current == null) return;
        double oldScale = _scale;
        double ns = Math.Clamp(_scale * factor, _minScale, _maxScale);
        if (Math.Abs(ns - _scale) < 0.0001) return;

        // Позиция курсора в координатах bitmap'а (до зума), чтобы после
        // перерисовки та же точка осталась под курсором (зум к курсору, а
        // не к левому верхнему углу).
        var m = Mouse.GetPosition(_view);
        double contentX = m.X;
        double contentY = m.Y;
        double viewX = m.X - _scroll.HorizontalOffset;
        double viewY = m.Y - _scroll.VerticalOffset;

        _scale = ns;
        RenderTiles();

        double ratio = _scale / oldScale;
        double newX = contentX * ratio - viewX;
        double newY = contentY * ratio - viewY;
        _scroll.ScrollToHorizontalOffset(newX);
        _scroll.ScrollToVerticalOffset(newY);
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
            g.Clear(System.Drawing.Color.Black);
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
        _tilesSource = Gdi.ToBitmapSource(bmp);
        _view.Source = _tilesSource;
        _view.Width = bw;
        _view.Height = bh;
        _overlay.Width = bw;
        _overlay.Height = bh;
        DrawOverlay();
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

    // ── оверлей: маркеры NPC, курсор, выбор ──────────────────────────────────

    private void DrawOverlay()
    {
        var map = _current?.Data;
        _overlay.Children.Clear();
        if (map == null) return;
        double tilePx = map.TileW * _scale;

        foreach (var m in map.Markers)
        {
            double cx = m.X * tilePx + tilePx / 2;
            double cy = m.Y * tilePx + tilePx / 2;
            double r = Math.Max(3, tilePx * 0.2);
            var brush = MarkerBrush(m.Type);
            _overlay.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = brush,
                Margin = new Thickness(cx - r, cy - r, 0, 0)
            });
            if (tilePx >= 10)
            {
                var lbl = new TextBlock
                {
                    Text = m.Name,
                    FontSize = 10,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(cx + r + 1, cy - r, 0, 0)
                };
                _overlay.Children.Add(lbl);
            }
            if (string.Equals(m.Name, _npcId, StringComparison.OrdinalIgnoreCase))
            {
                _overlay.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = tilePx,
                    Height = tilePx,
                    Stroke = System.Windows.Media.Brushes.Yellow,
                    StrokeThickness = 2,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    Margin = new Thickness(m.X * tilePx, m.Y * tilePx, 0, 0)
                });
            }
        }

        if (_hoverCell.HasValue && _hoverCell.Value != _selectedCell)
        {
            _overlay.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = tilePx,
                Height = tilePx,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255)),
                Margin = new Thickness(_hoverCell.Value.X * tilePx, _hoverCell.Value.Y * tilePx, 0, 0)
            });
        }
        if (_selectedCell.HasValue)
        {
            _overlay.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = tilePx,
                Height = tilePx,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 0, 255, 0)),
                Stroke = System.Windows.Media.Brushes.Lime,
                StrokeThickness = 2,
                Margin = new Thickness(_selectedCell.Value.X * tilePx, _selectedCell.Value.Y * tilePx, 0, 0)
            });
        }
    }

    private static SolidColorBrush MarkerBrush(string type) => type.ToLowerInvariant() switch
    {
        "merchant" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 215, 0)),
        "board" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 144, 255)),
        "instance_portal" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
        "dummy" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128)),
        "storage" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 133, 63)),
        "wanderer" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 200, 120)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 205, 50))
    };

    // ── координаты и информация ───────────────────────────────────────────────

    private (int X, int Y)? CellFrom(System.Windows.Point p)
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
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        PlacedZoneId = _current.ZoneId;
        PlacedTileX = x;
        PlacedTileY = y;
        DialogResult = true;
    }

    private void ClearPlacement()
    {
        try
        {
            // Полностью убираем NPC со всех Tiled-карт (аналог «разместить», но без
            // последующей вставки) — размещение очищается.
            TiledNpcWriter.RemoveFromAllMaps(_contentDir, _npcId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось очистить карту:\n" + ex.Message,
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Cleared = true;
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _tiles?.Dispose();
        foreach (var img in _tilesetImages.Values) img.Dispose();
        _tilesetImages.Clear();
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