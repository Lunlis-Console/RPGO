using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Networking;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Windows;

/// <summary>
/// Окно «Карта мира»: рельеф всего открытого мира из секторов (sector_data),
/// выбор масштаба (колесо/кнопки), панорамирование перетаскиванием, сетка и
/// подписи секторов, метка игрока. Недостающие секторы запрашиваются у сервера
/// на лету в пределах видимой области.
///
/// Область карты окна имеет те же пропорции, что и мир (3000x1700), поэтому при
/// минимальном зуме (x1) карта вписывается в окно точь-в-точь, а тайлы всегда
/// квадратные: текстура мира (1 пиксель = 1 тайл) рисуется в прямоугольник
/// размера WorldSize * scale с единым масштабом.
/// </summary>
public class WorldMapWindow : GameWindow
{
    private readonly GameClient _client;

    // Тайлы секторов: глобальные координаты уже в секторе (Col/Row).
    private readonly Dictionary<(int Col, int Row), SectorData> _sectors = new();
    private readonly HashSet<(int Col, int Row)> _requested = new();

    // Очередь секторов, чьи пиксели ещё не записаны в текстуру. Секторы приходят
    // в потоке приёма сети, а текстура строится в Draw (UI-поток), небольшими
    // порциями за кадр — поток приёма не блокируется тяжёлым SetData, из-за чего
    // все секторы доезжают быстрее, а карта заполняется плавно.
    private readonly Queue<(int Col, int Row)> _dirty = new();
    private bool _needsClear;

    // Рельеф всего мира кэшируется в одну текстуру (3000x1700), обновляется посекторно.
    private Texture2D? _worldTex;
    private Color[]? _colors;
    private readonly object _lock = new();

    // Камера карты. Zoom x1 = «весь мир вписан точно в окно», x1..x16.
    private float _zoom = 1f;
    private float _panX, _panY;            // смещение камеры в клетках мира
    private bool _dragging;
    private int _dragStartX, _dragStartY;
    private float _dragStartPanX, _dragStartPanY;

    private int _playerX = -1;
    private int _playerY = -1;
    private bool _lastVisible;

    // Цвета рельефа — те же, что у миникарты.
    private static readonly Color GroundA = new(112, 148, 96);
    private static readonly Color GroundB = new(104, 140, 90);
    private static readonly Color VoidColor = new(24, 26, 34);
    private static readonly Color MissingColor = new(40, 40, 45);

    // Кэш пикселей тайлсетов: цвет каждого пикселя карты = примерный цвет тайла
    // (центральная точка тайла в тайлсете), чтобы вода/лес/камень были различимы.
    private readonly Dictionary<string, TilesetPixels> _tilesetCache = new();

    private sealed class TilesetPixels
    {
        public Color[] Data = Array.Empty<Color>();
        public int TexWidth;
        public int TilePx = 1;
        public int Cols;
    }

    private const int ControlH = 30;

    public WorldMapWindow(GameClient client)
    {
        _client = client;
        Title = "Карта мира";
        // Область карты по умолчанию 1920x1088 (3000x1700 в масштабе 0.64) — те же
        // пропорции, что и у мира. Если окно не влезает в экран — уменьшаем, сохраняя
        // пропорции (тогда «вписать» всё равно совпадёт с рамкой точно). Резервируем
        // запас по краям: окно центрируется с отступом >= 50 сверху и не должно
        // вылезать за нижнюю границу игрового окна.
        float aw = 1920f, ah = 1088f;
        var g = GameMain.Instance?.Graphics;
        if (g != null)
        {
            float s = Math.Min(1f, Math.Min((g.PreferredBackBufferWidth - 40f) / aw,
                                            (g.PreferredBackBufferHeight - 140f) / ah));
            aw *= s;
            ah *= s;
        }
        Width = (int)Math.Round(aw) + 16;   // ContentW == aw
        Height = (int)Math.Round(ah) + 66;  // ContentH - ControlH == ah
        Visible = false;
    }

    public void SetSectorData(SectorData sector)
    {
        if (sector.TileData == null) return;
        lock (_lock)
        {
            _sectors[(sector.Col, sector.Row)] = sector;
            _requested.Remove((sector.Col, sector.Row));
            _dirty.Enqueue((sector.Col, sector.Row));
        }
    }

    public void SetPlayerPosition(int x, int y)
    {
        _playerX = x;
        _playerY = y;
    }

    /// <summary>Сбрасывает секторы (перезагрузка карты).</summary>
    public void ResetSectors()
    {
        lock (_lock)
        {
            _sectors.Clear();
            _requested.Clear();
            _dirty.Clear();
            _needsClear = true;
        }
    }

    protected override void OnHidden()
    {
        base.OnHidden();
        _lastVisible = false;
        _dragging = false;
    }

    private void UpdateSectorTexture(SectorData sector)
    {
        var device = GameMain.Instance?.GraphicsDevice;
        if (device == null) return;

        if (_worldTex == null)
        {
            _worldTex = new Texture2D(device, BalanceStatic.WorldWidth, BalanceStatic.WorldHeight);
            _colors = new Color[BalanceStatic.WorldWidth * BalanceStatic.WorldHeight];
            Array.Clear(_colors, 0, _colors.Length);
            // Инициализируем пустые сектора цветом «пустоты».
            for (int i = 0; i < _colors.Length; i++) _colors[i] = VoidColor;
            _worldTex.SetData(_colors);
        }

        // Пиксели тайлсета сектора (кэшируются один раз на тайлсет).
        TilesetPixels? ts = GetTilesetPixels(sector);

        int size = BalanceStatic.SectorSize;
        var region = new Color[size * size];
        for (int ly = 0; ly < size; ly++)
        {
            for (int lx = 0; lx < size; lx++)
            {
                int li = ly * size + lx;
                byte tileId = sector.TileData != null && li < sector.TileData.Length ? sector.TileData[li] : (byte)255;

                Color c;
                if (tileId == 0)
                {
                    // Пустая клетка — трава (шахматка, как на миникарте).
                    c = ((lx + ly) & 1) == 0 ? GroundA : GroundB;
                }
                else if (tileId == 255 || ts == null)
                {
                    c = MissingColor;
                }
                else
                {
                    // Центральный пиксель тайла из тайлсета.
                    int tCol = (tileId - 1) % ts.Cols;
                    int tRow = (tileId - 1) / ts.Cols;
                    int cxp = tCol * ts.TilePx + ts.TilePx / 2;
                    int cyp = tRow * ts.TilePx + ts.TilePx / 2;
                    int idx = cyp * ts.TexWidth + cxp;
                    c = (cxp < ts.TexWidth && idx >= 0 && idx < ts.Data.Length) ? ts.Data[idx] : MissingColor;
                }

                bool obs = sector.ObstacleData != null
                    && li < sector.ObstacleData.Length && sector.ObstacleData[li] != 0;
                if (obs) c = Darken(c, 0.55f);
                region[li] = c;
            }
        }

        int ox = sector.Col * size;
        int oy = sector.Row * size;
        for (int ly = 0; ly < size; ly++)
            Array.Copy(region, ly * size, _colors!, (oy + ly) * BalanceStatic.WorldWidth + ox, size);

        _worldTex.SetData(0, new Rectangle(ox, oy, size, size), region, 0, region.Length);
    }

    private TilesetPixels? GetTilesetPixels(SectorData sector)
    {
        if (string.IsNullOrEmpty(sector.TilesetId)) return null;
        string key = $"{sector.TilesetId}_{sector.TileWidth}";
        if (_tilesetCache.TryGetValue(key, out var cached)) return cached;

        var tex = SpriteCache.GetTileset(sector.TilesetId, sector.TileWidth, out int cols, out _);
        if (tex == null) return null;

        var data = new Color[tex.Width * tex.Height];
        tex.GetData(data);
        var ts = new TilesetPixels
        {
            Data = data,
            TexWidth = tex.Width,
            TilePx = Math.Max(1, sector.TileWidth),
            Cols = Math.Max(1, cols)
        };
        _tilesetCache[key] = ts;
        return ts;
    }

    private static Color Darken(Color c, float f)
    {
        return new Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
    }

    /// <summary>Область карты внутри окна (все вычисления масштаба — от неё).</summary>
    private Rectangle MapArea => new(ContentX, ContentY, ContentW, ContentH - ControlH);

    /// <summary>Масштаб «весь мир точно в окно» (пропорции области = пропорциям мира).</summary>
    private float FitScale
    {
        get
        {
            var a = MapArea;
            return Math.Min(a.Width / (float)BalanceStatic.WorldWidth,
                            a.Height / (float)BalanceStatic.WorldHeight);
        }
    }

    /// <summary>Видимая область мира в клетках при текущем масштабе (панорама зажата).</summary>
    private (int ViewW, int ViewH) ComputeView()
    {
        var a = MapArea;
        float scale = FitScale * _zoom;
        int vw = Math.Max(1, (int)Math.Round(a.Width / scale));
        int vh = Math.Max(1, (int)Math.Round(a.Height / scale));
        _panX = Math.Clamp(_panX, 0, Math.Max(0, BalanceStatic.WorldWidth - vw));
        _panY = Math.Clamp(_panY, 0, Math.Max(0, BalanceStatic.WorldHeight - vh));
        return (vw, vh);
    }

    private void RequestVisibleSectors()
    {
        int size = BalanceStatic.SectorSize;
        var (viewW, viewH) = ComputeView();
        int c0 = Math.Clamp((int)(_panX / size), 0, BalanceStatic.SectorCols - 1);
        int c1 = Math.Clamp((int)((_panX + viewW) / size), 0, BalanceStatic.SectorCols - 1);
        int r0 = Math.Clamp((int)(_panY / size), 0, BalanceStatic.SectorRows - 1);
        int r1 = Math.Clamp((int)((_panY + viewH) / size), 0, BalanceStatic.SectorRows - 1);

        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                if (_sectors.ContainsKey((c, r))) continue;
                if (!_requested.Add((c, r))) continue;
                _ = _client.SendAsync("sector_request", new { Col = c, Row = r });
            }
        }
    }

    private void CenterOnPlayer()
    {
        if (_playerX < 0) return;
        var a = MapArea;
        float scale = FitScale * _zoom;
        _panX = _playerX - a.Width / (2f * scale);
        _panY = _playerY - a.Height / (2f * scale);
        ComputeView();
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) { base.Update(gameTime, keyboard, mouse); return; }

        if (!_lastVisible)
        {
            _lastVisible = true;
            CenterOnPlayer();
        }

        var area = MapArea;
        bool overMap = area.Contains(mouse.X, mouse.Y);

        // Масштаб колесом мыши.
        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (overMap && wheel != 0)
        {
            _zoom = Math.Clamp(_zoom + (wheel > 0 ? 0.12f : -0.12f), 1f, 16f);
            CenterOnPlayer();
        }

        // Панорамирование перетаскиванием.
        bool pressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        if (pressed && overMap)
        {
            _dragging = true;
            _dragStartX = mouse.X;
            _dragStartY = mouse.Y;
            _dragStartPanX = _panX;
            _dragStartPanY = _panY;
        }
        if (_dragging && mouse.LeftButton == ButtonState.Pressed)
        {
            float scale = FitScale * _zoom;
            _panX = _dragStartPanX - (mouse.X - _dragStartX) / scale;
            _panY = _dragStartPanY - (mouse.Y - _dragStartY) / scale;
            ComputeView();
        }
        if (released) _dragging = false;

        // Кнопки масштаба.
        int by = ContentY + ContentH - ControlH + 4;
        var minus = new Rectangle(ContentX + 4, by, 26, ControlH - 8);
        var plus = new Rectangle(ContentX + 4 + 30, by, 26, ControlH - 8);
        var fit = new Rectangle(ContentX + ContentW - 74, by, 70, ControlH - 8);
        if (pressed)
        {
            if (minus.Contains(mouse.X, mouse.Y)) _zoom = Math.Max(1f, _zoom / 1.25f);
            else if (plus.Contains(mouse.X, mouse.Y)) _zoom = Math.Min(16f, _zoom * 1.25f);
            else if (fit.Contains(mouse.X, mouse.Y)) { _zoom = 1f; _panX = 0; _panY = 0; }
        }

        RequestVisibleSectors();

        base.Update(gameTime, keyboard, mouse);
    }

    public override void Draw(SpriteBatch sb)
    {
        base.Draw(sb, Mouse.GetState());
        if (!Visible) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        var mouse = Mouse.GetState();

        var area = MapArea;

        // Строим текстуру из пришедших секторов (порциями за кадр).
        UpdateDirtySectors();

        float scale = FitScale * _zoom;
        var (viewW, viewH) = ComputeView();

        // Прямое отображение «клетка мира -> пиксель окна»: экран(p) = area + (p - pan) * scale.
        // Единый масштаб -> тайлы всегда квадратные, зум центрируется на игроке.

        // Подложка карты.
        sb.Draw(SpriteCache.Pixel, area, new Color(18, 20, 28));
        if (_worldTex != null)
        {
            int srcW = Math.Min(viewW, BalanceStatic.WorldWidth - (int)_panX);
            int srcH = Math.Min(viewH, BalanceStatic.WorldHeight - (int)_panY);
            var src = new Rectangle((int)_panX, (int)_panY, Math.Max(1, srcW), Math.Max(1, srcH));
            var dest = new Rectangle(area.X, area.Y,
                                     Math.Max(1, (int)Math.Round(srcW * scale)),
                                     Math.Max(1, (int)Math.Round(srcH * scale)));
            sb.Draw(_worldTex, dest, src, Color.White);
        }

        int top = area.Y;
        int bottom = area.Y + area.Height;
        int left = area.X;
        int right = area.X + area.Width;
        int size = BalanceStatic.SectorSize;
        var gridColorStrong = new Color(60, 62, 70);
        var labelColor = new Color(80, 84, 92);
        float sectorPx = size * scale;

        // Сетка секторов + подписи.
        int c0 = Math.Max(0, (int)(_panX / size));
        int c1 = Math.Min(BalanceStatic.SectorCols, (int)((_panX + viewW) / size) + 1);
        int r0 = Math.Max(0, (int)(_panY / size));
        int r1 = Math.Min(BalanceStatic.SectorRows, (int)((_panY + viewH) / size) + 1);

        bool drawLabels = sectorPx >= 48;
        for (int c = c0; c <= c1; c++)
        {
            int sx = (int)(area.X + (c * size - _panX) * scale);
            if (sx < left || sx > right) continue;
            sb.Draw(SpriteCache.Pixel, new Rectangle(sx, top, 1, bottom - top), gridColorStrong);
            if (drawLabels)
            {
                for (int r = r0; r <= r1; r++)
                {
                    int sy = (int)(area.Y + (r * size - _panY) * scale);
                    if (sy + 2 < top || sy + 2 > bottom) continue;
                    string label = $"{c}_{r}";
                    var lsz = font.MeasureString(label);
                    if (sx + 3 < left || sx + 3 + lsz.X > right) continue;
                    sb.DrawString(font, label, new Vector2(sx + 3, sy + 2), labelColor);
                }
            }
        }
        for (int r = r0; r <= r1; r++)
        {
            int sy = (int)(area.Y + (r * size - _panY) * scale);
            if (sy < top || sy > bottom) continue;
            sb.Draw(SpriteCache.Pixel, new Rectangle(left, sy, right - left, 1), gridColorStrong);
        }

        // Граница мира (совпадает с рамкой окна при минимальном зуме).
        UIHelper.DrawRectOutline(sb, new Rectangle((int)(area.X - _panX * scale), (int)(area.Y - _panY * scale),
            (int)(BalanceStatic.WorldWidth * scale), (int)(BalanceStatic.WorldHeight * scale)),
            new Color(120, 130, 155));

        // Метка игрока (только если внутри области карты).
        if (_playerX >= 0 && _playerY >= 0)
        {
            int px = (int)(area.X + (_playerX - _panX) * scale);
            int py = (int)(area.Y + (_playerY - _panY) * scale);
            if (px >= left && px <= right && py >= top && py <= bottom)
            {
                sb.Draw(SpriteCache.Pixel, new Rectangle(px - 5, py - 5, 11, 11), Color.Black);
                sb.Draw(SpriteCache.Pixel, new Rectangle(px - 3, py - 3, 7, 7), Color.White);
            }
        }

        // Индикатор загрузки: пока получены не все секторы мира (например, только что
        // вошли в игру или после /reload), поверх карты рисуется прогресс.
        int loaded = CountLoaded();
        int total = TotalSectors;
        if (loaded < total)
        {
            int bw = 320, bh = 80;
            var box = new Rectangle(area.X + (area.Width - bw) / 2, area.Y + (area.Height - bh) / 2, bw, bh);
            sb.Draw(SpriteCache.Pixel, box, new Color(12, 14, 20, 225));
            UIHelper.DrawRectOutline(sb, box, new Color(60, 70, 90));
            sb.DrawString(font, "Загрузка карты...", new Vector2(box.X + 14, box.Y + 10), new Color(215, 220, 230));
            int pbarX = box.X + 14, pbarY = box.Y + 40, pbarW = box.Width - 28, pbarH = 12;
            sb.Draw(SpriteCache.Pixel, new Rectangle(pbarX, pbarY, pbarW, pbarH), new Color(30, 34, 44));
            int fillW = (int)(pbarW * (loaded / (float)total));
            if (fillW > 0)
                sb.Draw(SpriteCache.Pixel, new Rectangle(pbarX, pbarY, fillW, pbarH), new Color(95, 150, 100));
            sb.DrawString(font, $"{loaded} / {total}", new Vector2(box.X + 14, box.Y + 58), new Color(160, 170, 190));
        }

        // Панель управления масштабом.
        int by = ContentY + ContentH - ControlH + 4;
        DrawButtonHover(sb, "-", new Rectangle(ContentX + 4, by, 26, ControlH - 8), mouse, new Color(45, 55, 75));
        DrawButtonHover(sb, "+", new Rectangle(ContentX + 34, by, 26, ControlH - 8), mouse, new Color(45, 55, 75));
        DrawButtonHover(sb, "Вписать", new Rectangle(ContentX + ContentW - 74, by, 70, ControlH - 8), mouse, new Color(45, 55, 75));

        string scaleText = $"Масштаб: {scale:F2}px/кл · Секторов: {CountLoaded()}";
        sb.DrawString(font, scaleText, new Vector2(ContentX + 68, by + 4), new Color(180, 195, 215));
    }

    private int CountLoaded()
    {
        lock (_lock) return _sectors.Count;
    }

    private int TotalSectors => BalanceStatic.SectorCols * BalanceStatic.SectorRows;

    private const int MaxSectorUpdatesPerFrame = 64;

    /// <summary>Применяет пришедшие секторы к текстуре карты (UI-поток, порциями).</summary>
    private void UpdateDirtySectors()
    {
        if (_needsClear)
        {
            _worldTex?.Dispose();
            _worldTex = null;
            _colors = null;
            _needsClear = false;
        }
        for (int i = 0; i < MaxSectorUpdatesPerFrame; i++)
        {
            SectorData? sector;
            lock (_lock)
            {
                if (_dirty.Count == 0) return;
                var key = _dirty.Dequeue();
                sector = _sectors.TryGetValue(key, out var s) ? s : null;
            }
            if (sector != null) UpdateSectorTexture(sector);
        }
    }
}