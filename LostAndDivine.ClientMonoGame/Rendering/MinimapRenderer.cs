using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LostAndDivine.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LostAndDivine.ClientMonoGame.Rendering;

/// <summary>
/// Миникарта зоны: рельеф (тайлы/препятствия), игроки, монстры, NPC, порталы.
/// Рельеф кэшируется в текстуру и перестраивается только при смене карты/тайлов.
/// </summary>
public class MinimapRenderer
{
    public const int Size = 170;
    public const int PanelTop = 12;
    public const int PanelMarginRight = 12;

    private readonly int _texSize = Size;
    private WorldMap? _map;
    private byte[]? _tileData;
    private byte[]? _obstacleData;
    private int _mapW, _mapH;
    // Секторный открытый мир (main): рельеф посекторно (sector_data)
    private readonly Dictionary<(int Col, int Row), SectorData> _sectors = new();
    private bool _sectorMode;
    private string _playerName = "";
    private readonly HashSet<string> _partyMembers = new(StringComparer.OrdinalIgnoreCase);
    private bool _terrainDirty = true;
    private Texture2D? _terrainTex;
    private Color[]? _terrainColors;
    private bool _hasView;
    private Rectangle _viewBounds;
    // Окно миникарты в секторном мире: показываем не весь мир, а квадрат
    // FocusWindow x FocusWindow клеток вокруг игрока.
    private const int FocusWindow = 100;
    private int _winX0, _winY0, _lastWinX, _lastWinY;
    // Данные карты обновляются с сетевого потока (по частям), а читаются
    // с потока отрисовки — без лока возможен разрыв: новые данные при старых
    // размерах, что даёт IndexOutOfRange в RebuildTerrain.
    private readonly object _lock = new();

    // Цвета рельефа — как у окна карты мира.
    private static readonly Color GroundA = new(112, 148, 96);
    private static readonly Color GroundB = new(104, 140, 90);
    private static readonly Color VoidColor = new(24, 26, 34);
    private static readonly Color MissingColor = new(40, 40, 45);

    // Кэш пикселей тайлсетов: цвет клетки миникарты = примерный цвет тайла
    // (центральная точка тайла в тайлсете), как в окне карты мира.
    private readonly Dictionary<string, TilesetPixels> _tilesetCache = new();

    private sealed class TilesetPixels
    {
        public Color[] Data = Array.Empty<Color>();
        public int TexWidth;
        public int TilePx = 1;
        public int Cols;
    }

    public Rectangle GetPanelRect(int screenW)
        => new(screenW - Size - PanelMarginRight, PanelTop, Size, Size);

    public void SetPlayerName(string name) => _playerName = name ?? "";

    public void SetPartyMembers(IEnumerable<string> names)
    {
        _partyMembers.Clear();
        foreach (var n in names) _partyMembers.Add(n);
    }

    public void SetMap(WorldMap map)
    {
        lock (_lock)
        {
            _map = map;
            if (map == null) return;
            _mapW = map.Width;
            _mapH = map.Height;
            _terrainDirty = true;
        }
    }

    public void SetTileData(byte[]? data, int width, int height)
    {
        lock (_lock)
        {
            _tileData = data;
            _mapW = width;
            _mapH = height;
            _terrainDirty = true;
        }
    }

    public void SetObstacleData(byte[]? data, int width, int height)
    {
        lock (_lock)
        {
            _obstacleData = data;
            _mapW = width;
            _mapH = height;
            _terrainDirty = true;
        }
    }

    public void SetViewBounds(Rectangle viewBounds)
    {
        _viewBounds = viewBounds;
        _hasView = true;
    }

    /// <summary>Сбрасывает секторы открытого мира (смена зоны).</summary>
    public void ClearSectors()
    {
        lock (_lock)
        {
            _sectors.Clear();
            _sectorMode = false;
            _terrainDirty = true;
        }
    }

    /// <summary>Применяет сектор открытого мира (main) к рельефу миникарты.</summary>
    public void SetSectorData(SectorData sector)
    {
        lock (_lock)
        {
            // Секторы приходят только для main; принимаем даже если текущая карта
            // ещё старая (гонка: sector_data приходит раньше map_update после
            // zone_transition). Использование гейтится зоной в Draw.
            if (sector.TileData == null) return;
            _sectors[(sector.Col, sector.Row)] = sector;
            _sectorMode = true;
            _terrainDirty = true;
        }
    }

    public void ClearViewBounds() => _hasView = false;

    private Color GetTileColor(SectorData sector, int lx, int ly, Color groundA, Color groundB)
    {
        int size = BalanceStatic.SectorSize;
        int li = ly * size + lx;
        byte tileId = sector.TileData != null && li < sector.TileData.Length ? sector.TileData[li] : (byte)255;

        if (tileId == 0)
            return ((lx + ly) & 1) == 0 ? groundA : groundB;
        if (tileId == 255) return MissingColor;

        var ts = GetTilesetPixels(sector);
        if (ts == null) return MissingColor;

        int tCol = (tileId - 1) % ts.Cols;
        int tRow = (tileId - 1) / ts.Cols;
        int cxp = tCol * ts.TilePx + ts.TilePx / 2;
        int cyp = tRow * ts.TilePx + ts.TilePx / 2;
        int idx = cyp * ts.TexWidth + cxp;
        return cxp < ts.TexWidth && idx >= 0 && idx < ts.Data.Length ? ts.Data[idx] : MissingColor;
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
        => new Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));

    /// <summary>Рельеф окна вокруг игрока в секторном мире (FocusWindow x FocusWindow клеток).</summary>
    private void RebuildTerrainWindow(int ox, int oy)
    {
        WorldMap? map; bool sectorMode;
        Dictionary<(int Col, int Row), SectorData> sectors;
        lock (_lock)
        {
            map = _map; sectorMode = _sectorMode;
            sectors = new Dictionary<(int, int), SectorData>(_sectors);
        }
        if (map == null || !sectorMode) return;

        _terrainTex ??= new Texture2D(GameMain.Instance!.GraphicsDevice, _texSize, _texSize);
        _terrainColors ??= new Color[_texSize * _texSize];

        bool isSandy = map.ZoneId == "arena";
        var groundA = isSandy ? new Color(176, 160, 118) : GroundA;
        var groundB = isSandy ? new Color(168, 152, 112) : GroundB;

        int winW = Math.Min(FocusWindow, map.Width);
        int winH = Math.Min(FocusWindow, map.Height);

        for (int py = 0; py < _texSize; py++)
        {
            int my = oy + Math.Clamp((int)((py + 0.5f) / _texSize * winH), 0, winH - 1);
            for (int px = 0; px < _texSize; px++)
            {
                int mx = ox + Math.Clamp((int)((px + 0.5f) / _texSize * winW), 0, winW - 1);

                Color c = ((mx + my) & 1) == 0 ? groundA : groundB;

                SectorData? sector = null;
                if (mx < BalanceStatic.WorldWidth && my < BalanceStatic.WorldHeight)
                    sectors.TryGetValue((mx / BalanceStatic.SectorSize, my / BalanceStatic.SectorSize), out sector);
                if (sector == null || sector.TileData == null)
                {
                    c = VoidColor;
                }
                else
                {
                    int lx = mx % BalanceStatic.SectorSize;
                    int ly = my % BalanceStatic.SectorSize;
                    int li = ly * BalanceStatic.SectorSize + lx;
                    bool obs = sector.ObstacleData != null
                        && li < sector.ObstacleData.Length && sector.ObstacleData[li] != 0;
                    c = GetTileColor(sector, lx, ly, groundA, groundB);
                    if (obs) c = Darken(c, 0.55f);
                }

                _terrainColors[py * _texSize + px] = c;
            }
        }
        _terrainTex.SetData(_terrainColors);
    }

    private void RebuildTerrain()
    {
        _terrainDirty = false;
        WorldMap? map; byte[]? tileData; byte[]? obstacleData; int mapW, mapH; bool sectorMode;
        Dictionary<(int Col, int Row), SectorData> sectors;
        lock (_lock)
        {
            map = _map; tileData = _tileData; obstacleData = _obstacleData;
            mapW = _mapW; mapH = _mapH; sectorMode = _sectorMode;
            sectors = new Dictionary<(int, int), SectorData>(_sectors);
        }
        if (map == null || mapW <= 0 || mapH <= 0) return;

        _terrainTex ??= new Texture2D(GameMain.Instance!.GraphicsDevice, _texSize, _texSize);
        _terrainColors ??= new Color[_texSize * _texSize];

        bool isSandy = map.ZoneId == "arena";
        var groundA = isSandy ? new Color(176, 160, 118) : new Color(112, 148, 96);
        var groundB = isSandy ? new Color(168, 152, 112) : new Color(104, 140, 90);
        var feature = isSandy ? new Color(156, 140, 102) : new Color(96, 130, 82);
        var blocked = new Color(46, 52, 64);
        var voidColor = new Color(24, 26, 34);

        bool hasTiles = tileData != null && tileData.Length == mapW * mapH;
        bool hasObs = obstacleData != null && obstacleData.Length == mapW * mapH;

        for (int py = 0; py < _texSize; py++)
        {
            int my = Math.Clamp((int)((py + 0.5f) / _texSize * mapH), 0, mapH - 1);
            for (int px = 0; px < _texSize; px++)
            {
                int mx = Math.Clamp((int)((px + 0.5f) / _texSize * mapW), 0, mapW - 1);

                Color c = ((mx + my) & 1) == 0 ? groundA : groundB;

                if (sectorMode)
                {
                    // Секторный мир: рельеф из секторов; незагруженный сектор — пустота
                    SectorData? sector = null;
                    if (mx < BalanceStatic.WorldWidth && my < BalanceStatic.WorldHeight)
                        sectors.TryGetValue((mx / BalanceStatic.SectorSize, my / BalanceStatic.SectorSize), out sector);
                    if (sector == null || sector.TileData == null)
                    {
                        c = voidColor;
                    }
                    else
                    {
                        int lx = mx % BalanceStatic.SectorSize;
                        int ly = my % BalanceStatic.SectorSize;
                        int li = ly * BalanceStatic.SectorSize + lx;
                        bool obs = sector.ObstacleData != null
                            && li < sector.ObstacleData.Length && sector.ObstacleData[li] != 0;
                        c = GetTileColor(sector, lx, ly, groundA, groundB);
                        if (obs) c = Darken(c, 0.55f);
                    }
                }
                else if (hasObs && obstacleData![my * mapW + mx] != 0)
                    c = blocked;
                else if (hasTiles)
                {
                    byte t = tileData![my * mapW + mx];
                    if (t == 255) c = blocked;
                    else if (t != 0) c = feature;
                }

                _terrainColors[py * _texSize + px] = c;
            }
        }
        _terrainTex.SetData(_terrainColors);
    }

    public void Draw(SpriteBatch sb, Rectangle panel, int centerX, int centerY)
    {
        WorldMap? map; bool focus;
        lock (_lock) { map = _map; focus = _sectorMode && _map != null && string.Equals(_map.ZoneId, BalanceStatic.MainZoneId, StringComparison.Ordinal); }
        if (map == null || map.Width <= 0 || map.Height <= 0) return;

        // Секторный мир: окно FocusWindow x FocusWindow клеток вокруг игрока
        if (focus)
        {
            int winW = Math.Min(FocusWindow, map.Width);
            int winH = Math.Min(FocusWindow, map.Height);
            _winX0 = Math.Clamp(centerX - winW / 2, 0, map.Width - winW);
            _winY0 = Math.Clamp(centerY - winH / 2, 0, map.Height - winH);
            if (_terrainDirty || _winX0 != _lastWinX || _winY0 != _lastWinY)
            {
                _terrainDirty = false;
                _lastWinX = _winX0;
                _lastWinY = _winY0;
                RebuildTerrainWindow(_winX0, _winY0);
            }
        }
        else if (_terrainDirty)
        {
            RebuildTerrain();
        }

        sb.Draw(SpriteCache.Pixel, panel, new Color(15, 17, 24, 230));
        UIHelper.DrawRectOutline(sb, panel, new Color(90, 95, 115));

        int pad = 4;
        int labelH = 16;
        var mapRect = new Rectangle(panel.X + pad, panel.Y + pad, panel.Width - pad * 2, panel.Height - pad * 2 - labelH);
        sb.Draw(SpriteCache.Pixel, mapRect, new Color(28, 32, 42));
        if (_terrainTex != null)
            sb.Draw(_terrainTex, mapRect, Color.White);

        if (!focus && _hasView && _viewBounds.Width > 0 && _viewBounds.Height > 0)
        {
            var vr = ViewToRect(map, mapRect);
            var outline = new Color(255, 255, 255, 130);
            sb.Draw(SpriteCache.Pixel, new Rectangle(vr.X, vr.Y, vr.Width, 1), outline);
            sb.Draw(SpriteCache.Pixel, new Rectangle(vr.X, vr.Y + vr.Height - 1, vr.Width, 1), outline);
            sb.Draw(SpriteCache.Pixel, new Rectangle(vr.X, vr.Y, 1, vr.Height), outline);
            sb.Draw(SpriteCache.Pixel, new Rectangle(vr.X + vr.Width - 1, vr.Y, 1, vr.Height), outline);
        }

        DrawPoints(sb, mapRect, map, focus);

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font != null && !string.IsNullOrEmpty(map.ZoneName))
        {
            string label = map.PvPEnabled ? $"[PvP] {map.ZoneName}" : map.ZoneName;
            var sz = font.MeasureString(label);
            sb.DrawString(font, label, new Vector2(panel.X + (panel.Width - sz.X) / 2, panel.Y + panel.Height - labelH), new Color(180, 200, 220));
        }

        if (font != null)
        {
            string coordText = $"[{centerX}, {centerY}]";
            sb.DrawString(font, coordText, new Vector2(panel.X + 6, panel.Y + 4), new Color(190, 160, 20));
        }
    }

    private Rectangle ViewToRect(WorldMap map, Rectangle area)
    {
        float fx = _viewBounds.X / (float)map.Width;
        float fy = _viewBounds.Y / (float)map.Height;
        float fw = _viewBounds.Width / (float)map.Width;
        float fh = _viewBounds.Height / (float)map.Height;
        int x = area.X + (int)(fx * area.Width);
        int y = area.Y + (int)(fy * area.Height);
        int w = (int)(fw * area.Width);
        int h = (int)(fh * area.Height);
        return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
    }

    private void DrawDot(SpriteBatch sb, WorldMap map, Rectangle area, int mx, int my, Color color, int size)
    {
        // Секторный мир: точки за пределами окна не рисуем
        if (_sectorMode)
        {
            if (mx < _winX0 || mx >= _winX0 + FocusWindow || my < _winY0 || my >= _winY0 + FocusWindow)
                return;
            int cx = area.X + (int)((mx - _winX0 + 0.5f) / FocusWindow * area.Width);
            int cy = area.Y + (int)((my - _winY0 + 0.5f) / FocusWindow * area.Height);
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx - size / 2, cy - size / 2, size, size), color);
            return;
        }
        int cx2 = area.X + (int)((mx + 0.5f) / map.Width * area.Width);
        int cy2 = area.Y + (int)((my + 0.5f) / map.Height * area.Height);
        sb.Draw(SpriteCache.Pixel, new Rectangle(cx2 - size / 2, cy2 - size / 2, size, size), color);
    }

    private void DrawPoints(SpriteBatch sb, Rectangle area, WorldMap map, bool focus)
    {
        if (map == null) return;

        foreach (var p in map.Portals)
            DrawDot(sb, map, area, p.X, p.Y, new Color(190, 110, 255), 4);
        if (map.InstanceExitPortal != null)
            DrawDot(sb, map, area, map.InstanceExitPortal.X, map.InstanceExitPortal.Y, new Color(90, 220, 110), 4);
        if (map.InstanceChest != null)
            DrawDot(sb, map, area, map.InstanceChest.X, map.InstanceChest.Y, new Color(255, 205, 70), 4);

        if (map.Merchant != null)
            DrawDot(sb, map, area, map.Merchant.X, map.Merchant.Y, new Color(255, 210, 90), 4);
        if (map.Board != null)
            DrawDot(sb, map, area, map.Board.X, map.Board.Y,
                string.IsNullOrEmpty(map.Board.QuestIndicator) ? new Color(255, 210, 90) : new Color(255, 230, 80),
                string.IsNullOrEmpty(map.Board.QuestIndicator) ? 4 : 5);

        foreach (var n in map.Npcs)
        {
            if (n.Type == "merchant" || n.Type == "board") continue;
            bool quest = !string.IsNullOrEmpty(n.QuestIndicator);
            DrawDot(sb, map, area, n.X, n.Y, quest ? new Color(255, 230, 80) : new Color(225, 220, 180), quest ? 4 : 3);
        }

        foreach (var c in map.Collectibles)
            DrawDot(sb, map, area, c.X, c.Y, new Color(120, 200, 180), 2);

        foreach (var c in map.Corpses)
            DrawDot(sb, map, area, c.X, c.Y, new Color(150, 150, 150), 2);

        foreach (var m in map.Monsters)
            DrawDot(sb, map, area, m.X, m.Y, new Color(220, 70, 70), 2);

        foreach (var p in map.Players)
        {
            if (string.Equals(p.Name, _playerName, StringComparison.OrdinalIgnoreCase)) continue;
            Color c = _partyMembers.Contains(p.Name) ? new Color(80, 220, 120) : new Color(90, 170, 255);
            DrawDot(sb, map, area, p.X, p.Y, c, 4);
        }

        var me = map.Players.FirstOrDefault(p => string.Equals(p.Name, _playerName, StringComparison.OrdinalIgnoreCase));
        if (me != null)
        {
            DrawDot(sb, map, area, me.X, me.Y, Color.Black, 6);
            DrawDot(sb, map, area, me.X, me.Y, Color.White, 4);
        }
    }
}
