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
    private string _playerName = "";
    private readonly HashSet<string> _partyMembers = new(StringComparer.OrdinalIgnoreCase);
    private bool _terrainDirty = true;
    private Texture2D? _terrainTex;
    private Color[]? _terrainColors;
    private bool _hasView;
    private Rectangle _viewBounds;
    // Данные карты обновляются с сетевого потока (по частям), а читаются
    // с потока отрисовки — без лока возможен разрыв: новые данные при старых
    // размерах, что даёт IndexOutOfRange в RebuildTerrain.
    private readonly object _lock = new();

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

    public void ClearViewBounds() => _hasView = false;

    private void RebuildTerrain()
    {
        _terrainDirty = false;
        WorldMap? map; byte[]? tileData; byte[]? obstacleData; int mapW, mapH;
        lock (_lock)
        {
            map = _map; tileData = _tileData; obstacleData = _obstacleData;
            mapW = _mapW; mapH = _mapH;
        }
        if (map == null || mapW <= 0 || mapH <= 0) return;

        _terrainTex ??= new Texture2D(GameMain.Instance!.GraphicsDevice, _texSize, _texSize);
        _terrainColors ??= new Color[_texSize * _texSize];

        bool isSandy = map.ZoneId == "arena";
        var groundA = isSandy ? new Color(176, 160, 118) : new Color(112, 148, 96);
        var groundB = isSandy ? new Color(168, 152, 112) : new Color(104, 140, 90);
        var feature = isSandy ? new Color(156, 140, 102) : new Color(96, 130, 82);
        var blocked = new Color(46, 52, 64);

        bool hasTiles = tileData != null && tileData.Length == mapW * mapH;
        bool hasObs = obstacleData != null && obstacleData.Length == mapW * mapH;

        for (int py = 0; py < _texSize; py++)
        {
            int my = Math.Clamp((int)((py + 0.5f) / _texSize * mapH), 0, mapH - 1);
            for (int px = 0; px < _texSize; px++)
            {
                int mx = Math.Clamp((int)((px + 0.5f) / _texSize * mapW), 0, mapW - 1);

                Color c = ((mx + my) & 1) == 0 ? groundA : groundB;
                if (hasObs && obstacleData![my * mapW + mx] != 0)
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
        WorldMap? map;
        lock (_lock) { map = _map; }
        if (map == null || map.Width <= 0 || map.Height <= 0) return;
        if (_terrainDirty) RebuildTerrain();

        sb.Draw(SpriteCache.Pixel, panel, new Color(15, 17, 24, 230));
        UIHelper.DrawRectOutline(sb, panel, new Color(90, 95, 115));

        int pad = 4;
        int labelH = 16;
        var mapRect = new Rectangle(panel.X + pad, panel.Y + pad, panel.Width - pad * 2, panel.Height - pad * 2 - labelH);
        sb.Draw(SpriteCache.Pixel, mapRect, new Color(28, 32, 42));
        if (_terrainTex != null)
            sb.Draw(_terrainTex, mapRect, Color.White);

        if (_hasView && _viewBounds.Width > 0 && _viewBounds.Height > 0)
        {
            var vr = ViewToRect(map, mapRect);
            var outline = new Color(255, 255, 255, 130);
            sb.Draw(SpriteCache.Pixel, new Rectangle(vr.X, vr.Y, vr.Width, 1), outline);
            sb.Draw(SpriteCache.Pixel, new Rectangle(vr.X, vr.Y + vr.Height - 1, vr.Width, 1), outline);
            sb.Draw(SpriteCache.Pixel, new Rectangle(vr.X, vr.Y, 1, vr.Height), outline);
            sb.Draw(SpriteCache.Pixel, new Rectangle(vr.X + vr.Width - 1, vr.Y, 1, vr.Height), outline);
        }

        DrawPoints(sb, mapRect, map);

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
        int cx = area.X + (int)((mx + 0.5f) / map.Width * area.Width);
        int cy = area.Y + (int)((my + 0.5f) / map.Height * area.Height);
        sb.Draw(SpriteCache.Pixel, new Rectangle(cx - size / 2, cy - size / 2, size, size), color);
    }

    private void DrawPoints(SpriteBatch sb, Rectangle area, WorldMap map)
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
