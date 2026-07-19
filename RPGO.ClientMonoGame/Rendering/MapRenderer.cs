using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RPGGame.ClientMonoGame.Data;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Rendering;

public class MapRenderer
{
    private WorldMap? _currentMap;
    private string _playerName = "";
    private int _playerLevel = 1;

    // Выбор сущности
    private string? _selectedEntityType;
    private string? _selectedEntityName;
    private int _selectedEntityX, _selectedEntityY;
    private string? _selectedEntityId;
    private int _moveTargetX = -1, _moveTargetY = -1;

    // Визуальная интерполяция
    private readonly Dictionary<string, (float X, float Y)> _visPos = new();
    private readonly Dictionary<string, (int X, int Y)> _visTarget = new();
    private readonly object _stateLock = new();
    private DateTime _lastVisTime = DateTime.UtcNow;
    private const float VisCellsPerSec = 6f;

    // Всплывающий текст
    private readonly List<FloatingText> _floatingTexts = new();

    // Видимая область
    private int _viewStartX, _viewStartY, _viewEndX, _viewEndY;

    // Фактический размер клетки (подгоняется под экран, чтобы не было зазоров)
    private float _cellW = 22f;
    private float _cellH = 18f;
    private float _gridOX = 4f;
    private float _gridOY = 18f;

    // Масштаб карты (зум колесом мыши)
    private float _zoom = 1f;
    public float Zoom => _zoom;
    public void ChangeZoom(float delta)
    {
        _zoom = Math.Clamp(_zoom + delta, 1.5f, 3f);
    }

    // Плавная позиция камеры (float), следует за интерполированной позицией игрока
    private float _camX = 50f;
    private float _camY = 50f;

    // Базовые размеры клеток
    private const float BaseCellW = 22f;
    private const float BaseCellH = 18f;
    private const float HeaderH = 0f;
    private const float LeftMargin = 4f;

    // События
    public event Action<EntityInfo?>? SelectionChanged;
    public event Action<EntityInfo, int, int>? InteractRequested;
    public event Action<int, int>? MoveRequested;

    public void SetPlayerName(string name) => _playerName = name;
    public void SetPlayerLevel(int level) => _playerLevel = level;
    public int GetPlayerX() => GetCenterX();
    public int GetPlayerY() => GetCenterY();

    public void SetMap(WorldMap map)
    {
        lock (_stateLock) { _currentMap = map; }
        InvalidateVisual();
    }

    public void SpawnFloatingText(float mapX, float mapY, string text, Color color)
    {
        lock (_stateLock)
        {
            _floatingTexts.Add(new FloatingText
            {
                X = mapX, Y = mapY, Text = text, Color = color, StartTime = DateTime.UtcNow
            });
        }
    }

    public EntityInfo? GetSelectedEntity()
    {
        if (_selectedEntityType == null) return null;
        // Берём полные данные (включая HP/MaxHp/Level) из списка сущностей клетки
        var list = GetEntitiesAt(_selectedEntityX, _selectedEntityY);
        foreach (var e in list)
        {
            bool same = _selectedEntityType == "monster" || _selectedEntityType == "player" || _selectedEntityType == "corpse"
                ? e.Id == _selectedEntityId
                : e.Type == _selectedEntityType && e.X == _selectedEntityX && e.Y == _selectedEntityY;
            if (same) return e;
        }
        // Фолбэк, если точного совпадения нет
        return new EntityInfo
        {
            Type = _selectedEntityType,
            Name = _selectedEntityName ?? "",
            X = _selectedEntityX,
            Y = _selectedEntityY,
            Id = _selectedEntityId
        };
    }

    public void SelectEntity(EntityInfo entity, int mapX, int mapY)
    {
        StartInteraction(entity, mapX, mapY);
    }

    public void ActivateSelection()
    {
        if (_selectedEntityType == null) return;
        if (_selectedEntityType == "move")
        {
            _moveTargetX = _selectedEntityX;
            _moveTargetY = _selectedEntityY;
            MoveRequested?.Invoke(_selectedEntityX, _selectedEntityY);
        }
        else
        {
            InteractRequested?.Invoke(GetSelection()!, _selectedEntityX, _selectedEntityY);
        }
    }

    private void StartInteraction(EntityInfo entity, int mapX, int mapY)
    {
        _selectedEntityType = entity.Type;
        _selectedEntityName = entity.Name;
        _selectedEntityX = mapX;
        _selectedEntityY = mapY;
        _selectedEntityId = entity.Id;
        _moveTargetX = _moveTargetY = -1;
        SelectionChanged?.Invoke(GetSelection());
        InvalidateVisual();
    }

    private EntityInfo? GetSelection()
    {
        if (_selectedEntityType == null) return null;
        return new EntityInfo
        {
            Type = _selectedEntityType, Name = _selectedEntityName ?? "",
            Level = 0, Hp = 0, MaxHp = 0,
            X = _selectedEntityX, Y = _selectedEntityY, Id = _selectedEntityId
        };
    }

    public void HandleClick(float screenX, float screenY, float offsetX, float offsetY, float areaW, float areaH)
    {
        if (_currentMap == null) return;
        lock (_stateLock) { ComputeView(_currentMap, GetCenterX(), GetCenterY(), offsetX, offsetY, areaW, areaH); }
        if (!ScreenToMap(screenX, screenY, areaW, areaH, out int mapX, out int mapY)) return;

        var entitiesOnCell = GetEntitiesAt(mapX, mapY);
        if (entitiesOnCell.Count == 0)
        {
            HandleEmptyCellClick(mapX, mapY);
            return;
        }
        if (entitiesOnCell.Count == 1)
        {
            HandleSingleEntityClick(entitiesOnCell[0], mapX, mapY);
            return;
        }
        // Несколько сущностей — открываем окно выбора
        EntityPickRequested?.Invoke(entitiesOnCell, mapX, mapY);
    }

    // Запрос окна выбора сущности, когда в клетке несколько сущностей
    public event Action<List<EntityInfo>, int, int>? EntityPickRequested;

    private List<EntityInfo> GetEntitiesAt(int mapX, int mapY)
    {
        var list = new List<EntityInfo>();
        if (_currentMap == null) return list;
        foreach (var p in _currentMap.Players.Where(p => p.X == mapX && p.Y == mapY && p.Name != _playerName))
            list.Add(new EntityInfo { Type = "player", Name = p.Name, Level = p.Level, Hp = p.Health, MaxHp = p.MaxHealth, X = mapX, Y = mapY, Id = p.Id.ToString() });
        foreach (var m in _currentMap.Monsters.Where(m => m.X == mapX && m.Y == mapY))
            list.Add(new EntityInfo { Type = "monster", Name = m.Name, Level = m.Level, Hp = m.Health, MaxHp = m.MaxHealth, X = mapX, Y = mapY, Id = m.Id.ToString() });
        if (_currentMap.Merchant != null && _currentMap.Merchant.X == mapX && _currentMap.Merchant.Y == mapY)
            list.Add(new EntityInfo { Type = "merchant", Name = _currentMap.Merchant.Name, X = mapX, Y = mapY });
        if (_currentMap.Board != null && _currentMap.Board.X == mapX && _currentMap.Board.Y == mapY)
            list.Add(new EntityInfo { Type = "board", Name = "Доска заданий", X = mapX, Y = mapY });
        foreach (var c in _currentMap.Collectibles?.Where(c => c.X == mapX && c.Y == mapY) ?? Enumerable.Empty<CollectiblePosition>())
            list.Add(new EntityInfo { Type = "collectible", Name = c.Name, X = mapX, Y = mapY, Id = c.Id });
        foreach (var cs in _currentMap.Corpses?.Where(cs => cs.X == mapX && cs.Y == mapY) ?? Enumerable.Empty<CorpsePosition>())
            list.Add(new EntityInfo { Type = "corpse", Name = cs.MonsterName, Level = cs.Level, X = mapX, Y = mapY, Id = cs.Id.ToString() });
        return list;
    }

    private void HandleEmptyCellClick(int mapX, int mapY)
    {
        if (_selectedEntityType == "move" && _selectedEntityX == mapX && _selectedEntityY == mapY)
        {
            _moveTargetX = mapX; _moveTargetY = mapY;
            MoveRequested?.Invoke(mapX, mapY);
            return;
        }
        ClearSelectedEntity();
        _selectedEntityType = "move";
        _selectedEntityName = "Точка назначения";
        _selectedEntityX = mapX; _selectedEntityY = mapY;
        _moveTargetX = _moveTargetY = -1;
        SelectionChanged?.Invoke(GetSelection());
        InvalidateVisual();
    }

    private void HandleSingleEntityClick(EntityInfo entity, int mapX, int mapY)
    {
        bool sameEntity = _selectedEntityType == entity.Type && (
            entity.Type == "monster" || entity.Type == "corpse"
                ? _selectedEntityId != null && _selectedEntityId == entity.Id
                : _selectedEntityX == mapX && _selectedEntityY == mapY);
        if (sameEntity)
        {
            if (entity.Type != "player")
                InteractRequested?.Invoke(entity, mapX, mapY);
            _moveTargetX = _moveTargetY = -1;
            return;
        }
        StartInteraction(entity, mapX, mapY);
    }

    private void ClearSelectedEntity()
    {
        _selectedEntityType = null; _selectedEntityName = null;
        _selectedEntityX = _selectedEntityY = 0; _selectedEntityId = null;
        SelectionChanged?.Invoke(null);
    }

    private int GetCenterX()
    {
        var map = _currentMap;
        if (map == null) return 50;
        var me = map.Players.FirstOrDefault(p => p.Name == _playerName);
        return me?.X ?? (map.Merchant?.X ?? 50);
    }

    private int GetCenterY()
    {
        var map = _currentMap;
        if (map == null) return 50;
        var me = map.Players.FirstOrDefault(p => p.Name == _playerName);
        return me?.Y ?? (map.Merchant?.Y ?? 50);
    }

    // Вычисляет вьюпорт карты (начало/конец и размер клетки) — используется и при отрисовке, и при клике,
    // чтобы координаты клика всегда совпадали с тем, что нарисовано в текущем кадре.
    private void ComputeView(WorldMap map, int centerX, int centerY, float offsetX, float offsetY, float areaW, float areaH)
    {
        float cellW = BaseCellW * _zoom;
        float cellH = BaseCellH * _zoom;
        float availW = areaW - LeftMargin - 4;
        float availH = areaH - HeaderH - 4;
        int cols = Math.Max(1, (int)(availW / cellW));
        int rows = Math.Max(1, (int)(availH / cellH));

        int startX, startY, endX, endY;
        if (map.Width <= cols)
        {
            startX = 0;
            endX = map.Width - 1;
        }
        else
        {
            startX = centerX - cols / 2;
            if (startX < 0) startX = 0;
            endX = startX + cols - 1;
            if (endX > map.Width - 1)
            {
                endX = map.Width - 1;
                startX = Math.Max(0, endX - cols + 1);
            }
        }
        if (map.Height <= rows)
        {
            startY = 0;
            endY = map.Height - 1;
        }
        else
        {
            startY = centerY - rows / 2;
            if (startY < 0) startY = 0;
            endY = startY + rows - 1;
            if (endY > map.Height - 1)
            {
                endY = map.Height - 1;
                startY = Math.Max(0, endY - rows + 1);
            }
        }
        int viewW = endX - startX + 1, viewH = endY - startY + 1;
        _viewStartX = startX; _viewStartY = startY; _viewEndX = endX; _viewEndY = endY;

        _gridOX = offsetX + LeftMargin;
        _gridOY = offsetY + HeaderH;
        _cellW = availW / viewW;
        _cellH = availH / viewH;
    }

    private bool ScreenToMap(float sx, float sy, float areaW, float areaH, out int mapX, out int mapY)
    {
        mapX = mapY = -1;
        int col = (int)((sx - _gridOX) / _cellW);
        int row = (int)((sy - _gridOY) / _cellH);
        if (col < 0 || row < 0) return false;
        mapX = _viewStartX + col; mapY = _viewStartY + row;
        if (mapX < 0 || mapX >= (_currentMap?.Width ?? 100) || mapY < 0 || mapY >= (_currentMap?.Height ?? 100)) return false;
        return true;
    }

    private void InvalidateVisual()
    {
        AdvanceVisPositions();
    }

    public void Draw(SpriteBatch sb, float offsetX, float offsetY, float areaW, float areaH)
    {
        WorldMap? map;
        lock (_stateLock) { map = _currentMap; }

        var font = SpriteCache.Font;
        var fontSmall = SpriteCache.FontSmall ?? font;
        if (font == null) return;

        // Фон
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)offsetX, (int)offsetY, (int)areaW, (int)areaH), new Color(235, 240, 225));

        if (map == null)
        {
            sb.DrawString(font, "Карта загружается...", new Vector2(offsetX + 10, offsetY + 10), Color.Gray);
            return;
        }

        var me = map.Players.FirstOrDefault(p => p.Name == _playerName);

        // Целевая позиция камеры: интерполированная позиция игрока (плавная),
        // либо дискретная клетка, если интерполяция ещё не готова.
        float targetX = me?.X ?? (map.Merchant?.X ?? 50);
        float targetY = me?.Y ?? (map.Merchant?.Y ?? 50);
        if (me != null)
        {
            lock (_stateLock)
            {
                if (_visPos.TryGetValue($"player:{me.Name}", out var v))
                {
                    targetX = v.X;
                    targetY = v.Y;
                }
            }
        }

        // Плавное следование камеры за целью (lerp). Чем меньше camLerp, тем мягче.
        float camLerp = 0.12f;
        _camX += (targetX - _camX) * camLerp;
        _camY += (targetY - _camY) * camLerp;

        int centerX = (int)Math.Round(_camX);
        int centerY = (int)Math.Round(_camY);

        ComputeView(map, centerX, centerY, offsetX, offsetY, areaW, areaH);

        // Тайлы (слой земли) — спрайт травы на каждую клетку (размер подогнан под экран)
        var grass = SpriteCache.GetGrassSprite();
        int viewW = _viewEndX - _viewStartX + 1;
        int viewH = _viewEndY - _viewStartY + 1;
        for (int y = 0; y < viewH; y++)
        {
            float ty = _gridOY + y * _cellH;

            for (int x = 0; x < viewW; x++)
            {
                float tx = _gridOX + x * _cellW;
                if (grass != null)
                    sb.Draw(grass, new Rectangle((int)tx, (int)ty, (int)Math.Ceiling(_cellW), (int)Math.Ceiling(_cellH)), Color.White);
                else
                    sb.Draw(SpriteCache.Pixel, new Rectangle((int)tx, (int)ty, (int)Math.Ceiling(_cellW), (int)Math.Ceiling(_cellH)), Color.LightGreen);
            }
        }

        // Визуальная интерполяция
        var liveKeys = new HashSet<string>();
        foreach (var p in map.Players) { var k = $"player:{p.Name}"; SetVisTarget(k, p.X, p.Y); liveKeys.Add(k); }
        foreach (var m in map.Monsters) { var k = $"monster:{m.Id}"; SetVisTarget(k, m.X, m.Y); liveKeys.Add(k); }
        lock (_stateLock)
        {
            foreach (var k in _visTarget.Keys.ToList())
                if (!liveKeys.Contains(k)) { _visTarget.Remove(k); _visPos.Remove(k); }
        }
        AdvanceVisPositions();

        // Путь
        if (_moveTargetX >= 0 && _moveTargetY >= 0 && me != null)
        {
            int mx = map.Merchant?.X ?? -1, my = map.Merchant?.Y ?? -1;
            int bx = map.Board?.X ?? -1, by = map.Board?.Y ?? -1;
            var pathDots = ClientPathfinding.FindPath(me.X, me.Y, _moveTargetX, _moveTargetY, mx, my, bx, by, map.Width, map.Height);
            if (pathDots.Count == 0 && (me.X != _moveTargetX || me.Y != _moveTargetY)) _moveTargetX = _moveTargetY = -1;
            else if (me.X == _moveTargetX && me.Y == _moveTargetY) _moveTargetX = _moveTargetY = -1;
            else
            {
                var pathColor = new Color(100, 149, 237, 180);
                foreach (var (px, py) in pathDots)
                {
                    if (px >= _viewStartX && px <= _viewEndX && py >= _viewStartY && py <= _viewEndY)
                    {
                        float dotX = _gridOX + (px - _viewStartX) * _cellW + _cellW / 2 - 3;
                        float dotY = _gridOY + (py - _viewStartY) * _cellH + _cellH / 2 - 3;
                        sb.Draw(SpriteCache.Pixel, new Rectangle((int)dotX, (int)dotY, 6, 6), pathColor);
                    }
                }
            }
        }

        // Сущности
        DrawEntities(sb, font, fontSmall, offsetX, offsetY, _viewStartX, _viewStartY, _viewEndX, _viewEndY, me);

        // Легенда
        int legendY = (int)(_gridOY + viewH * _cellH + 4);
        DrawLegend(sb, font, fontSmall, offsetX, legendY);

        // Координаты — в правом верхнем углу карты, как HUD-плашка (поверх тайлов)
        {
            string coordText = $"КАРТА [{centerX}, {centerY}]";
            var coordSize = font.MeasureString(coordText);
            int pad = 6;
            int boxW = (int)coordSize.X + pad * 2;
            int boxH = (int)coordSize.Y + pad * 2;
            int boxX = (int)(offsetX + areaW - boxW - 8);
            int boxY = (int)(offsetY + 4);
            sb.Draw(SpriteCache.Pixel, new Rectangle(boxX, boxY, boxW, boxH), new Color(35, 37, 45, 210));
            sb.Draw(SpriteCache.Pixel, new Rectangle(boxX, boxY, boxW, 2), new Color(90, 95, 115));
            sb.DrawString(font, coordText, new Vector2(boxX + pad, boxY + pad), new Color(160, 200, 255));
        }
    }

    private void DrawEntities(SpriteBatch sb, SpriteFont font, SpriteFont fontSmall, float offsetX, float offsetY, int startX, int startY, int endX, int endY, PlayerPosition? me)
    {
        WorldMap? map;
        lock (_stateLock) { map = _currentMap; }
        if (map == null) return;

        // Статичные объекты (ниже монстров/игроков): торговец, доска, сбор, трупы
        void DrawStatic(Texture2D? spr, int wx, int wy, Color tint)
        {
            if (wx < startX || wx > endX || wy < startY || wy > endY) return;
            float px = _gridOX + (wx - startX) * _cellW;
            float py = _gridOY + (wy - startY) * _cellH;
            if (spr != null)
                sb.Draw(spr, new Rectangle((int)px - 2, (int)py - 2, (int)_cellW + 4, (int)_cellH + 4), tint);
            else
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)px, (int)py, (int)_cellW, (int)_cellH), tint);
        }

        if (map.Merchant != null)
        {
            var trader = SpriteCache.GetTraderSprite();
            DrawStatic(trader, map.Merchant.X, map.Merchant.Y, Color.White);
        }
        if (map.Board != null)
        {
            var board = SpriteCache.GetBoardSprite();
            DrawStatic(board, map.Board.X, map.Board.Y, Color.White);
        }
        foreach (var cl in map.Collectibles)
        {
            var cs = SpriteCache.GetCollectibleSprite();
            DrawStatic(cs, cl.X, cl.Y, Color.White);
        }
        foreach (var cp in map.Corpses ?? Enumerable.Empty<CorpsePosition>())
        {
            var corpse = SpriteCache.GetCorpseSprite();
            DrawStatic(corpse, cp.X, cp.Y, new Color(200, 200, 200));
        }

        // Монстры
        foreach (var m in map.Monsters)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"monster:{m.Id}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float px = _gridOX + (v.X - startX) * _cellW + 3;
            float py = _gridOY + (v.Y - startY) * _cellH;

            var sprite = SpriteCache.GetMonsterSprite(m.TemplateId);
            if (sprite != null)
                sb.Draw(sprite, new Rectangle((int)px - 2, (int)py - 2, (int)_cellW + 4, (int)_cellH + 4), Color.White);
            else
            {
                int diff = m.Level - _playerLevel;
                Color color = diff switch
                {
                    <= -3 => Color.Green, <= -1 => Color.LightGreen,
                    <= 1 => Color.Gray, <= 3 => Color.Orange, _ => Color.Red
                };
                sb.DrawString(font, m.Symbol.ToString(), new Vector2(px, py), color);
            }
        }

        // Игроки
        foreach (var p in map.Players)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"player:{p.Name}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float px = _gridOX + (v.X - startX) * _cellW + 3;
            float py = _gridOY + (v.Y - startY) * _cellH;

            var playerSprite = SpriteCache.GetPlayerSprite();
            if (playerSprite != null)
                sb.Draw(playerSprite, new Rectangle((int)px - 2, (int)py - 2, (int)_cellW + 4, (int)_cellH + 4), Color.White);
            else
                sb.DrawString(font, "P", new Vector2(px, py), p.Name == _playerName ? Color.Goldenrod : Color.Green);

            // Имя
            string nick = p.Name;
            var nickSize = fontSmall.MeasureString(nick);
            float nx = _gridOX + (v.X - startX) * _cellW + _cellW / 2 - nickSize.X / 2;
            float ny = py - 14;
            sb.DrawString(fontSmall, nick, new Vector2(nx + 1, ny + 1), Color.Black);
            sb.DrawString(fontSmall, nick, new Vector2(nx, ny), p.Name == _playerName ? Color.Goldenrod : Color.LightGray);
        }

        // Всплывающий текст
        lock (_stateLock)
        {
            for (int i = _floatingTexts.Count - 1; i >= 0; i--)
            {
                var ft = _floatingTexts[i];
                float elapsed = (float)(DateTime.UtcNow - ft.StartTime).TotalMilliseconds;
                if (elapsed >= ft.DurationMs) { _floatingTexts.RemoveAt(i); continue; }
                float t = elapsed / ft.DurationMs;
                int alpha = 255 - (int)(t * 200); if (alpha < 0) alpha = 0;
                float rise = t * 1.2f;
                float fpx = _gridOX + (ft.X - startX) * _cellW + _cellW / 2;
                float fpy = _gridOY + (ft.Y - startY - rise) * _cellH - 4;
                var c = new Color(ft.Color.R, ft.Color.G, ft.Color.B, (byte)alpha);
                sb.DrawString(font, ft.Text, new Vector2(fpx - 8, fpy), c);
            }
        }

        // Выделение
        if (_selectedEntityType != null)
        {
            int hx = _selectedEntityX, hy = _selectedEntityY;
            string? hkey = null;
            if (_selectedEntityType == "monster" && _selectedEntityId != null) hkey = $"monster:{_selectedEntityId}";
            else if (_selectedEntityType == "player" && _selectedEntityName != null) hkey = $"player:{_selectedEntityName}";
            if (_selectedEntityType == "merchant" && map.Merchant != null) { hx = map.Merchant.X; hy = map.Merchant.Y; }
            else if (_selectedEntityType == "board" && map.Board != null) { hx = map.Board.X; hy = map.Board.Y; }
            if (hkey != null) { lock (_stateLock) { if (_visPos.TryGetValue(hkey, out var hv)) { hx = (int)Math.Round(hv.X); hy = (int)Math.Round(hv.Y); } } }
            if (hx >= startX && hx <= endX && hy >= startY && hy <= endY)
            {
                float tx = _gridOX + (hx - startX) * _cellW;
                float ty = _gridOY + (hy - startY) * _cellH;
                Color hc = _selectedEntityType switch
                {
                    "monster" => Color.Red, "move" => Color.DodgerBlue,
                    "corpse" => Color.Gray, _ => Color.LimeGreen
                };
                DrawRect(sb, tx + 1, ty + 1, _cellW - 2, _cellH - 2, hc, 2);
            }
        }
    }

    private void DrawLegend(SpriteBatch sb, SpriteFont font, SpriteFont fontSmall, float offsetX, float legendY)
    {
        void Legend(float x, string sym, Color symColor, string label)
        {
            sb.DrawString(font, sym, new Vector2(offsetX + x, legendY), symColor);
            sb.DrawString(fontSmall, label, new Vector2(offsetX + x + 12, legendY + 2), Color.Black);
        }
        Legend(4, "P", Color.Green, "вы");
        Legend(50, "$", Color.Gold, "торговец");
        Legend(130, "Q", Color.MediumPurple, "доска заданий");
        Legend(250, "*", Color.LimeGreen, "сбор");
        Legend(140, "■", Color.Green, "легкий");
        Legend(200, "■", Color.Gray, "равный");
        Legend(260, "■", Color.Orange, "сложный");
        Legend(320, "■", Color.Red, "опасный");
    }

    private void DrawRect(SpriteBatch sb, float x, float y, float w, float h, Color color, int thickness = 1)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, (int)w, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)(y + h - thickness), (int)w, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, thickness, (int)h), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)(x + w - thickness), (int)y, thickness, (int)h), color);
    }

    private void AdvanceVisPositions()
    {
        var now = DateTime.UtcNow;
        float dt = (float)(now - _lastVisTime).TotalSeconds;
        if (dt > 0.1f) dt = 0.1f;
        _lastVisTime = now;
        float step;
        try
        {
            float mult = 1f;
            var playerName = GameMain.Instance?.Client.PlayerName ?? "";
            if (playerName.Equals("test", StringComparison.OrdinalIgnoreCase)
                || playerName.Equals("тест", StringComparison.OrdinalIgnoreCase))
                mult = 10f;
            else
            {
                var st = GameMain.Instance?.Client.Status;
                if (st?.MoveIntervalMs > 0)
                    mult = 500f / st.MoveIntervalMs;
            }
            float visSpeed = VisCellsPerSec * mult;
            step = visSpeed * dt;
        }
        catch
        {
            step = VisCellsPerSec * dt;
        }
        if (step < 0.0001f) step = 0.0001f;
        lock (_stateLock)
        {
            foreach (var kv in _visTarget)
            {
                var key = kv.Key; var tgt = kv.Value;
                if (!_visPos.TryGetValue(key, out var v)) { _visPos[key] = (tgt.X, tgt.Y); continue; }
                float dx = tgt.X - v.X, dy = tgt.Y - v.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist <= step || dist < 0.001f) _visPos[key] = (tgt.X, tgt.Y);
                else { float inv = step / dist; _visPos[key] = (v.X + dx * inv, v.Y + dy * inv); }
            }
        }
    }

    private void SetVisTarget(string key, int tx, int ty)
    {
        lock (_stateLock)
        {
            _visTarget[key] = (tx, ty);
            if (!_visPos.ContainsKey(key)) _visPos[key] = (tx, ty);
        }
    }

    public (int X, int Y)? GetEntityCell(string key)
    {
        lock (_stateLock)
        {
            if (_visTarget.TryGetValue(key, out var t)) return (t.X, t.Y);
            return null;
        }
    }
}

public sealed class FloatingText
{
    public float X, Y;
    public string Text = "";
    public Color Color;
    public DateTime StartTime;
    public int DurationMs = 1000;
}
