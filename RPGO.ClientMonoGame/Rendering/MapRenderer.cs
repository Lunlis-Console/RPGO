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

    // Имена участников группы (без себя) — для подсветки ников на карте.
    private readonly HashSet<string> _partyMemberNames = new(StringComparer.OrdinalIgnoreCase);

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

    // Всплывающий текст
    private readonly List<FloatingText> _floatingTexts = new();
    private static readonly Random _rng = new();

    // Снаряды
    private readonly List<ClientProjectile> _projectiles = new();

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
    private DateTime _lastFrameTime = DateTime.UtcNow;

    // Базовые размеры клеток
    private const float BaseCellW = 22f;
    private const float BaseCellH = 18f;
    private const float HeaderH = 0f;
    private const float LeftMargin = 4f;

    // Масштаб спрайтов сущностей (игроки, монстры) относительно клетки
    private const float EntityScale = 3.0f;

    // События
    public event Action<EntityInfo?>? SelectionChanged;
    public event Action<EntityInfo, int, int>? InteractRequested;
    public event Action<int, int>? MoveRequested;

    public void SetPlayerName(string name) => _playerName = name;
    public void SetPlayerLevel(int level) => _playerLevel = level;
    public void SetPlayerDead(bool dead)
    {
        if (dead && !_isDead) { _deathFrame = 0; _deathAnimStart = DateTime.UtcNow; }
        _isDead = dead;
    }
    public void SetWeaponSubtype(string? subtype)
    {
        if (_weaponSubtype != subtype)
        {
            Logger.Debug($"SetWeaponSubtype: '{_weaponSubtype}' -> '{subtype}'");
            _weaponSubtype = subtype;
            _weaponLogOnce = true;
        }
    }

    public void SetShieldSubtype(string? subtype)
    {
        if (_shieldSubtype != subtype)
        {
            Logger.Debug($"SetShieldSubtype: '{_shieldSubtype}' -> '{subtype}'");
            _shieldSubtype = subtype;
        }
    }

    public void TriggerAttack()
    {
        _isAttacking = true;
        _attackAnimStart = DateTime.UtcNow;
    }

    // Направление взгляда локального игрока ("down" | "up" | "left" | "right").
    // Вычисляется в AdvanceVisPositions по фактическому вектору движения.
    private string _localFacing = "down";

    // Игрок в данный момент движется (интерполяция не завершена).
    private bool _isMoving;

    // Игрок мёртв — показываем death-анимацию вместо walk/idle.
    private bool _isDead;
    private int _deathFrame;
    private DateTime _deathAnimStart;

    // Атака — показываем attack-анимацию вместо idle/walk.
    private bool _isAttacking;
    private DateTime _attackAnimStart;

    // Подтип оружия в правой руке (для оверлея). null = нет оружия.
    private string? _weaponSubtype;
    private bool _weaponLogOnce = true;

    // Подтип щита в левой руке (для оверлея). null = нет щита.
    private string? _shieldSubtype;

    // Итоговое направление локального игрока:
    //  - пока игрок ДВИЖЕТСЯ к цели — смотрим по направлению движения
    //    (иначе «идёт боком»);
    //  - когда стоит и действует с выбранной целью (атака монстра,
    //    сбор/лут предмета) — поворот в её сторону.
    // Хорошая основа для будущих анимаций.
    private string GetLocalFacing()
    {
        if (_isMoving) return _localFacing;

        var map = _currentMap;
        if (map != null && _selectedEntityType != null && _selectedEntityType != "move"
            && !(_selectedEntityType == "player" && _selectedEntityName == _playerName))
        {
            int? tx = null, ty = null;
            if (_selectedEntityType == "monster" && _selectedEntityId != null)
            {
                var m = map.Monsters.FirstOrDefault(mm => mm.Id.ToString() == _selectedEntityId);
                if (m != null) { tx = m.X; ty = m.Y; }
            }
            else if (_selectedEntityType == "player" && _selectedEntityName != null)
            {
                var pl = map.Players.FirstOrDefault(pp => pp.Name == _selectedEntityName);
                if (pl != null) { tx = pl.X; ty = pl.Y; }
            }
            else { tx = _selectedEntityX; ty = _selectedEntityY; }

        if (tx.HasValue && ty.HasValue)
        {
            var me = map.Players.FirstOrDefault(p => p.Name == _playerName);
            if (me != null)
            {
                int ddx = tx.Value - me.X;
                int ddy = ty.Value - me.Y;
                int manhattan = Math.Abs(ddx) + Math.Abs(ddy);
                // Смотрим на цель только когда игрок уже у неё (на соседней/
                // той же клетке). Пока в пути — смотрим по движению, иначе
                // из-за пауз между шагами сервера спрайт мерцает (цель↔движение).
                if (manhattan <= 1 && (ddx != 0 || ddy != 0))
                {
                    // Запоминаем показанный взгляд в _localFacing, чтобы после
                    // снятия цели (смерть моба) спрайт не «прыгал» на
                    // устаревшее направление движения, а оставался куда смотрел.
                    string dir = (Math.Abs(ddx) > Math.Abs(ddy))
                        ? (ddx < 0 ? "left" : "right")
                        : (ddy < 0 ? "up" : "down");
                    _localFacing = dir;
                    return dir;
                }
            }
        }
        }
        return _localFacing;
    }

    /// <summary>Обновляет список ников участников группы (без себя) для подсветки на карте.</summary>
    public void SetPartyMembers(IEnumerable<string> names)
    {
        _partyMemberNames.Clear();
        foreach (var n in names) _partyMemberNames.Add(n);
    }
    public int GetPlayerX() => GetCenterX();
    public int GetPlayerY() => GetCenterY();

    public void SetMap(WorldMap map)
    {
        lock (_stateLock) { _currentMap = map; }
    }

    public void SpawnFloatingText(float mapX, float mapY, string text, Color color, bool isCrit = false)
    {
        lock (_stateLock)
        {
            // Небольшой случайный разброс по X, чтобы цифры не накладывались друг на друга
            float jitterX = (float)(_rng.NextDouble() - 0.5) * 0.6f;
            _floatingTexts.Add(new FloatingText
            {
                X = mapX + jitterX,
                Y = mapY,
                Text = text,
                Color = color,
                StartTime = DateTime.UtcNow,
                Scale = isCrit ? 1.35f : 1f
            });
        }
    }

    // Всплывающий текст над самим игроком (опыт / повышение уровня)
    public void SpawnFloatingTextAtPlayer(string text, Color color, bool isCrit = false)
    {
        (float X, float Y) v;
        lock (_stateLock)
        {
            if (!_visPos.TryGetValue($"player:{_playerName}", out v))
                return;
        }
        SpawnFloatingText(v.X, v.Y - 0.6f, text, color, isCrit);
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
        // Запоминаем клетку назначения для отрисовки пути (вейпоинта)
        // не только для пустой клетки ("move"), но и для целей действия
        // (монстр/труп/предмет) — чтобы путь рисовался и при движении к цели.
        _moveTargetX = mapX;
        _moveTargetY = mapY;
        SelectionChanged?.Invoke(GetSelection());
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
        int clickCX = (int)Math.Round(_camX);
        int clickCY = (int)Math.Round(_camY);
        lock (_stateLock) { ComputeView(_currentMap, clickCX, clickCY, offsetX, offsetY, areaW, areaH); }
        float subCellX = (_camX - clickCX) * _cellW;
        float subCellY = (_camY - clickCY) * _cellH;
        _gridOX -= subCellX;
        _gridOY -= subCellY;
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

    public void HandleRightClick(float screenX, float screenY, float offsetX, float offsetY, float areaW, float areaH)
    {
        if (_currentMap == null) return;
        int clickCX = (int)Math.Round(_camX);
        int clickCY = (int)Math.Round(_camY);
        lock (_stateLock) { ComputeView(_currentMap, clickCX, clickCY, offsetX, offsetY, areaW, areaH); }
        float subCellX = (_camX - clickCX) * _cellW;
        float subCellY = (_camY - clickCY) * _cellH;
        _gridOX -= subCellX;
        _gridOY -= subCellY;
        if (!ScreenToMap(screenX, screenY, areaW, areaH, out int mapX, out int mapY)) return;

        var entitiesOnCell = GetEntitiesAt(mapX, mapY);
        if (entitiesOnCell.Count == 1)
        {
            HandleSingleEntityRightClick(entitiesOnCell[0], mapX, mapY);
        }
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
        foreach (var n in _currentMap.Npcs?.Where(n => n.X == mapX && n.Y == mapY && n.Type != "merchant" && n.Type != "board") ?? Enumerable.Empty<NpcPosition>())
            list.Add(new EntityInfo { Type = "npc", Name = n.Name, X = mapX, Y = mapY, Id = n.Id });
        return list;
    }

    private void HandleEmptyCellClick(int mapX, int mapY)
    {
        ClearSelection();
        _selectedEntityType = "move";
        _selectedEntityName = "Точка назначения";
        _selectedEntityX = mapX; _selectedEntityY = mapY;
        _moveTargetX = mapX; _moveTargetY = mapY;
        SelectionChanged?.Invoke(GetSelection());
        MoveRequested?.Invoke(mapX, mapY);
    }

    private void HandleSingleEntityClick(EntityInfo entity, int mapX, int mapY)
    {
        StartInteraction(entity, mapX, mapY);
        if (entity.Type != "player")
            InteractRequested?.Invoke(entity, mapX, mapY);
    }

    private void HandleSingleEntityRightClick(EntityInfo entity, int mapX, int mapY)
    {
        StartInteraction(entity, mapX, mapY);
    }

    public void ClearSelection()
    {
        _selectedEntityType = null; _selectedEntityName = null;
        _selectedEntityX = _selectedEntityY = 0; _selectedEntityId = null;
        _moveTargetX = _moveTargetY = -1;
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
        if (col < -1 || row < -1) return false;
        col = Math.Clamp(col, 0, (int)(areaW / _cellW));
        row = Math.Clamp(row, 0, (int)(areaH / _cellH));
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
        var now = DateTime.UtcNow;
        float dt = (float)(now - _lastFrameTime).TotalSeconds;
        if (dt > 0.1f) dt = 0.1f;
        _lastFrameTime = now;

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

        // Визуальная интерполяция — ДО чтения позиции камеры,
        // чтобы камера следовала за ТЕКУЩЕЙ, а не вчерашней визуальной позицией.
        var liveKeys = new HashSet<string>();
        foreach (var p in map.Players) { var k = $"player:{p.Name}"; SetVisTarget(k, p.X, p.Y); liveKeys.Add(k); }
        foreach (var m in map.Monsters) { var k = $"monster:{m.Id}"; SetVisTarget(k, m.X, m.Y); liveKeys.Add(k); }
        lock (_stateLock)
        {
            foreach (var k in _visTarget.Keys.ToList())
                if (!liveKeys.Contains(k)) { _visTarget.Remove(k); _visPos.Remove(k); }
        }
        AdvanceVisPositions();

        // Целевая позиция камеры: интерполированная позиция игрока (плавная).
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

        // Камера = визуальная (уже плавная) позиция игрока.
        _camX = targetX;
        _camY = targetY;

        int centerX = (int)Math.Floor(_camX);
        int centerY = (int)Math.Floor(_camY);

        ComputeView(map, centerX, centerY, offsetX, offsetY, areaW, areaH);

        // Sub-cell offset: сдвигаем всю сетку на дробную часть позиции камеры,
        // чтобы тайлы и сущности рисовались непрерывно между клетками.
        float subCellX = (_camX - centerX) * _cellW;
        float subCellY = (_camY - centerY) * _cellH;
        _gridOX -= subCellX;
        _gridOY -= subCellY;

        // Тайлы (слой земли) — спрайт травы на каждую клетку (+2px по краям из-за sub-cell offset)
        var grass = SpriteCache.GetGrassSprite();
        int viewW = _viewEndX - _viewStartX + 1;
        int viewH = _viewEndY - _viewStartY + 1;
        for (int y = -1; y <= viewH + 1; y++)
        {
            float ty = _gridOY + y * _cellH;
            if (ty > offsetY + areaH) continue;

            for (int x = -1; x <= viewW + 1; x++)
            {
                float tx = _gridOX + x * _cellW;
                if (tx > offsetX + areaW) continue;
                if (grass != null)
                    sb.Draw(grass, new Rectangle((int)tx, (int)ty, (int)Math.Ceiling(_cellW) + 2, (int)Math.Ceiling(_cellH) + 2), Color.White);
                else
                    sb.Draw(SpriteCache.Pixel, new Rectangle((int)tx, (int)ty, (int)Math.Ceiling(_cellW) + 2, (int)Math.Ceiling(_cellH) + 2), Color.LightGreen);
            }
        }

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
        Rectangle EntityRect(float px, float py)
        {
            int w = (int)(_cellW * EntityScale);
            int h = (int)(_cellH * EntityScale);
            return new Rectangle((int)px - w / 2 + (int)(_cellW / 2), (int)py - h / 2 + (int)(_cellH / 2), w, h);
        }

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

        if (map.Merchant != null && map.Merchant.X >= startX && map.Merchant.X <= endX && map.Merchant.Y >= startY && map.Merchant.Y <= endY)
        {
            var trader = SpriteCache.GetTraderSprite();
            DrawStatic(trader, map.Merchant.X, map.Merchant.Y, Color.White);
            var mFont = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (mFont != null)
            {
                var mSize = mFont.MeasureString(map.Merchant.Name);
                float mpx = _gridOX + (map.Merchant.X - startX) * _cellW + _cellW / 2;
                float mpy = _gridOY + (map.Merchant.Y - startY) * _cellH - mSize.Y - 4;
                sb.DrawString(mFont, map.Merchant.Name, new Vector2(mpx - mSize.X / 2 + 1, mpy + 1), Color.Black);
                sb.DrawString(mFont, map.Merchant.Name, new Vector2(mpx - mSize.X / 2, mpy), Color.White);
            }
        }
        if (map.Board != null && map.Board.X >= startX && map.Board.X <= endX && map.Board.Y >= startY && map.Board.Y <= endY)
        {
            var board = SpriteCache.GetBoardSprite();
            DrawStatic(board, map.Board.X, map.Board.Y, Color.White);
            var bFont = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (bFont != null)
            {
                var bSize = bFont.MeasureString(map.Board.Name);
                float bpx = _gridOX + (map.Board.X - startX) * _cellW + _cellW / 2;
                float bpy = _gridOY + (map.Board.Y - startY) * _cellH - bSize.Y - 4;
                sb.DrawString(bFont, map.Board.Name, new Vector2(bpx - bSize.X / 2 + 1, bpy + 1), Color.Black);
                sb.DrawString(bFont, map.Board.Name, new Vector2(bpx - bSize.X / 2, bpy), Color.White);
            }
        }
        foreach (var npc in map.Npcs ?? Enumerable.Empty<NpcPosition>())
        {
            if (npc.Type == "merchant" || npc.Type == "board") continue;
            var trader = SpriteCache.GetTraderSprite();
            DrawStatic(trader, npc.X, npc.Y, Color.LightBlue);
            if (npc.X >= startX && npc.X <= endX && npc.Y >= startY && npc.Y <= endY)
            {
                var npcFont = SpriteCache.FontSmall ?? SpriteCache.Font;
                if (npcFont != null)
                {
                    var nSize = npcFont.MeasureString(npc.Name);
                    float npx = _gridOX + (npc.X - startX) * _cellW + _cellW / 2;
                    float npy = _gridOY + (npc.Y - startY) * _cellH - nSize.Y - 4;
                    sb.DrawString(npcFont, npc.Name, new Vector2(npx - nSize.X / 2 + 1, npy + 1), Color.Black);
                    sb.DrawString(npcFont, npc.Name, new Vector2(npx - nSize.X / 2, npy), Color.White);
                }
                if (!string.IsNullOrEmpty(npc.QuestIndicator))
                {
                    var iFont = SpriteCache.FontSmall ?? SpriteCache.Font;
                    if (iFont != null)
                    {
                        string icon = npc.QuestIndicator == "available" ? "!" : "?";
                        Color iconColor = npc.QuestIndicator == "ready" ? Color.Yellow
                                        : npc.QuestIndicator == "available" ? Color.Yellow
                                        : new Color(160, 160, 160);
                        var sz = iFont.MeasureString(icon);
                        var nSz = iFont.MeasureString(npc.Name);
                        float px = _gridOX + (npc.X - startX) * _cellW + _cellW / 2 - sz.X;
                        float py = _gridOY + (npc.Y - startY) * _cellH - sz.Y * 2 - 4 - nSz.Y - 4;
                        sb.DrawString(iFont, icon, new Vector2(px, py), iconColor, 0f, Vector2.Zero, 2f, SpriteEffects.None, 0f);
                    }
                }
            }
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

        // === Фаза 1: отрисовка ВСЕХ спрайтов (монстры + игроки) ===
        foreach (var m in map.Monsters)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"monster:{m.Id}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float px = _gridOX + (v.X - startX) * _cellW + 3;
            float py = _gridOY + (v.Y - startY) * _cellH;

            var sprite = SpriteCache.GetMonsterSprite(m.TemplateId);
            if (sprite != null)
                sb.Draw(sprite, EntityRect(px, py), Color.White);
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

        foreach (var p in map.Players)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"player:{p.Name}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float px = _gridOX + (v.X - startX) * _cellW + 3;
            float py = _gridOY + (v.Y - startY) * _cellH;

            bool isLocal = p.Name == _playerName;
            string facing = isLocal ? GetLocalFacing() : "down";

            // Выбор анимации: death при смерти, attack при атаке, walk при движении, idle при стоянии
            SpriteAnimation? playerAnim = null;
            bool useAttackAnim = false;
            if (isLocal && _isDead)
                playerAnim = SpriteCache.GetPlayerDeathAnimation(facing);
            else if (isLocal && _isAttacking)
            {
                playerAnim = SpriteCache.GetPlayerAttackAnimation(facing);
                if (playerAnim != null) useAttackAnim = true;
                else playerAnim = SpriteCache.GetPlayerAnimation(facing);
            }
            else if (isLocal)
                playerAnim = _isMoving
                    ? SpriteCache.GetPlayerAnimation(facing)
                    : SpriteCache.GetAnimation($"player_idle_{facing}");

            if (playerAnim != null)
            {
                int frame;
                if (isLocal && _isDead)
                {
                    float elapsed = (float)(DateTime.UtcNow - _deathAnimStart).TotalSeconds;
                    _deathFrame = Math.Min((int)(elapsed / playerAnim.FrameDuration), playerAnim.FrameCount - 1);
                    frame = _deathFrame;
                }
                else if (isLocal && useAttackAnim)
                {
                    float elapsed = (float)(DateTime.UtcNow - _attackAnimStart).TotalSeconds;
                    int atkFrame = (int)(elapsed / playerAnim.FrameDuration);
                    if (atkFrame >= playerAnim.FrameCount)
                    {
                        _isAttacking = false;
                        atkFrame = 0;
                    }
                    frame = atkFrame;
                }
                else
                {
                    float frameDuration = playerAnim.FrameDuration;
                    // Walk-анимация синхронизирована со скоростью движения
                    if (_isMoving)
                    {
                        int moveMs = 500;
                        try { var st = GameMain.Instance?.Client.Status; if (st?.MoveIntervalMs > 0) moveMs = st.MoveIntervalMs; } catch { }
                        frameDuration = (moveMs / 1000f) / playerAnim.FrameCount;
                    }
                    frame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / frameDuration) % playerAnim.FrameCount;
                }
                var src = playerAnim.GetSourceRect(frame);
                sb.Draw(playerAnim.Sheet, EntityRect(px, py), src, Color.White);

                // Оружие поверх персонажа (только для локального игрока)
                if (isLocal && !string.IsNullOrEmpty(_weaponSubtype))
                {
                    SpriteAnimation? weaponAnim;
                    bool useWeaponAttack = false;
                    if (_isAttacking)
                    {
                        weaponAnim = SpriteCache.GetWeaponAttackAnimation(_weaponSubtype, facing);
                        if (weaponAnim != null) useWeaponAttack = true;
                        else weaponAnim = SpriteCache.GetWeaponAnimation(_weaponSubtype, facing, _isMoving);
                    }
                    else
                        weaponAnim = SpriteCache.GetWeaponAnimation(_weaponSubtype, facing, _isMoving);
                    if (_weaponLogOnce) { Logger.Debug($"WeaponOverlay: subtype={_weaponSubtype} facing={facing} anim={(weaponAnim != null ? "OK" : "NULL")}"); _weaponLogOnce = false; }
                    if (weaponAnim != null)
                    {
                        int wFrame;
                        if (useWeaponAttack)
                        {
                            float elapsed = (float)(DateTime.UtcNow - _attackAnimStart).TotalSeconds;
                            wFrame = (int)(elapsed / weaponAnim.FrameDuration);
                            if (wFrame >= weaponAnim.FrameCount) wFrame = weaponAnim.FrameCount - 1;
                        }
                        else
                        {
                            float wFrameDur = weaponAnim.FrameDuration;
                            if (_isMoving)
                            {
                                int moveMs = 500;
                                try { var st = GameMain.Instance?.Client.Status; if (st?.MoveIntervalMs > 0) moveMs = st.MoveIntervalMs; } catch { }
                                wFrameDur = (moveMs / 1000f) / weaponAnim.FrameCount;
                            }
                            wFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / wFrameDur) % weaponAnim.FrameCount;
                        }
                        var wSrc = weaponAnim.GetSourceRect(wFrame);
                        sb.Draw(weaponAnim.Sheet, EntityRect(px, py), wSrc, Color.White);
                    }
                }

                // Щит поверх персонажа (только для локального игрока)
                if (isLocal && !string.IsNullOrEmpty(_shieldSubtype))
                {
                    SpriteAnimation? shieldAnim;
                    bool useShieldAttack = false;
                    if (_isAttacking)
                    {
                        shieldAnim = SpriteCache.GetShieldAttackAnimation(facing);
                        if (shieldAnim != null) useShieldAttack = true;
                        else shieldAnim = SpriteCache.GetShieldAnimation(facing, _isMoving);
                    }
                    else
                        shieldAnim = SpriteCache.GetShieldAnimation(facing, _isMoving);
                    if (shieldAnim != null)
                    {
                        int sFrame;
                        if (useShieldAttack)
                        {
                            float elapsed = (float)(DateTime.UtcNow - _attackAnimStart).TotalSeconds;
                            sFrame = (int)(elapsed / shieldAnim.FrameDuration);
                            if (sFrame >= shieldAnim.FrameCount) sFrame = shieldAnim.FrameCount - 1;
                        }
                        else
                        {
                            float sFrameDur = shieldAnim.FrameDuration;
                            if (_isMoving)
                            {
                                int moveMs = 500;
                                try { var st = GameMain.Instance?.Client.Status; if (st?.MoveIntervalMs > 0) moveMs = st.MoveIntervalMs; } catch { }
                                sFrameDur = (moveMs / 1000f) / shieldAnim.FrameCount;
                            }
                            sFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / sFrameDur) % shieldAnim.FrameCount;
                        }
                        var sSrc = shieldAnim.GetSourceRect(sFrame);
                        sb.Draw(shieldAnim.Sheet, EntityRect(px, py), sSrc, Color.White);
                    }
                }
            }
            else
            {
                var playerSprite = isLocal
                    ? SpriteCache.GetPlayerSprite(facing)
                    : SpriteCache.GetPlayerSprite("down");
                if (playerSprite != null)
                {
                    sb.Draw(playerSprite, EntityRect(px, py), Color.White);

                    // Оружие поверх (статичный fallback)
                    if (isLocal && !string.IsNullOrEmpty(_weaponSubtype))
                    {
                        SpriteAnimation? weaponAnim;
                        bool useWeaponAttack = false;
                        if (_isAttacking)
                        {
                            weaponAnim = SpriteCache.GetWeaponAttackAnimation(_weaponSubtype, facing);
                            if (weaponAnim != null) useWeaponAttack = true;
                            else weaponAnim = SpriteCache.GetWeaponAnimation(_weaponSubtype, facing, _isMoving);
                        }
                        else
                            weaponAnim = SpriteCache.GetWeaponAnimation(_weaponSubtype, facing, _isMoving);
                        if (weaponAnim != null)
                        {
                            int wFrame;
                            if (useWeaponAttack)
                            {
                                float elapsed = (float)(DateTime.UtcNow - _attackAnimStart).TotalSeconds;
                                wFrame = (int)(elapsed / weaponAnim.FrameDuration);
                                if (wFrame >= weaponAnim.FrameCount) wFrame = weaponAnim.FrameCount - 1;
                            }
                            else
                            {
                                float wFrameDur = weaponAnim.FrameDuration;
                                if (_isMoving)
                                {
                                    int moveMs = 500;
                                    try { var st = GameMain.Instance?.Client.Status; if (st?.MoveIntervalMs > 0) moveMs = st.MoveIntervalMs; } catch { }
                                    wFrameDur = (moveMs / 1000f) / weaponAnim.FrameCount;
                                }
                                wFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / wFrameDur) % weaponAnim.FrameCount;
                            }
                            var wSrc = weaponAnim.GetSourceRect(wFrame);
                            sb.Draw(weaponAnim.Sheet, EntityRect(px, py), wSrc, Color.White);
                        }
                    }

                    // Щит поверх (статичный fallback)
                    if (isLocal && !string.IsNullOrEmpty(_shieldSubtype))
                    {
                        SpriteAnimation? shieldAnim;
                        bool useShieldAttack = false;
                        if (_isAttacking)
                        {
                            shieldAnim = SpriteCache.GetShieldAttackAnimation(facing);
                            if (shieldAnim != null) useShieldAttack = true;
                            else shieldAnim = SpriteCache.GetShieldAnimation(facing, _isMoving);
                        }
                        else
                            shieldAnim = SpriteCache.GetShieldAnimation(facing, _isMoving);
                        if (shieldAnim != null)
                        {
                            int sFrame;
                            if (useShieldAttack)
                            {
                                float elapsed = (float)(DateTime.UtcNow - _attackAnimStart).TotalSeconds;
                                sFrame = (int)(elapsed / shieldAnim.FrameDuration);
                                if (sFrame >= shieldAnim.FrameCount) sFrame = shieldAnim.FrameCount - 1;
                            }
                            else
                            {
                                float sFrameDur = shieldAnim.FrameDuration;
                                if (_isMoving)
                                {
                                    int moveMs = 500;
                                    try { var st = GameMain.Instance?.Client.Status; if (st?.MoveIntervalMs > 0) moveMs = st.MoveIntervalMs; } catch { }
                                    sFrameDur = (moveMs / 1000f) / shieldAnim.FrameCount;
                                }
                                sFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / sFrameDur) % shieldAnim.FrameCount;
                            }
                            var sSrc = shieldAnim.GetSourceRect(sFrame);
                            sb.Draw(shieldAnim.Sheet, EntityRect(px, py), sSrc, Color.White);
                        }
                    }
                }
                else
                {
                    Color fbColor = isLocal ? Color.Goldenrod : Color.LightGray;
                    sb.DrawString(font, "P", new Vector2(px, py), fbColor);
                }
            }
        }

        // === Фаза 2: имена + HP-бары ПОВЕРХ всех спрайтов ===
        foreach (var m in map.Monsters)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"monster:{m.Id}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float py = _gridOY + (v.Y - startY) * _cellH;
            float centerX = _gridOX + (v.X - startX) * _cellW + _cellW / 2;

            // Имя + уровень монстра
            string mname = $"{m.Name} [{m.Level}]";
            var mnameSize = fontSmall.MeasureString(mname);
            float mny = py - 26;
            sb.DrawString(fontSmall, mname, new Vector2(centerX - mnameSize.X / 2 + 1, mny + 1), Color.Black);
            sb.DrawString(fontSmall, mname, new Vector2(centerX - mnameSize.X / 2, mny), Color.White);

            // HP bar под именем
            if (m.MaxHealth > 0)
            {
                float barW = 34;
                float barH = 3;
                float barX = centerX - barW / 2;
                float barY = py - 8;
                float hpPct = Math.Clamp((float)m.Health / m.MaxHealth, 0f, 1f);
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)barX, (int)barY, (int)barW, (int)barH), new Color(40, 10, 10));
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)barX, (int)barY, (int)(barW * hpPct), (int)barH), new Color(180, 40, 40));
            }
        }

        foreach (var p in map.Players)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"player:{p.Name}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float py = _gridOY + (v.Y - startY) * _cellH;
            float centerX = _gridOX + (v.X - startX) * _cellW + _cellW / 2;

            Color groupColor = new Color(110, 230, 130);
            Color nickColor = p.Name == _playerName
                ? Color.Goldenrod
                : (_partyMemberNames.Contains(p.Name) ? groupColor : Color.LightGray);

            // Имя + уровень
            string nick = $"{p.Name} [{p.Level}]";
            var nickSize = fontSmall.MeasureString(nick);
            float ny = py - 26;
            sb.DrawString(fontSmall, nick, new Vector2(centerX - nickSize.X / 2 + 1, ny + 1), Color.Black);
            sb.DrawString(fontSmall, nick, new Vector2(centerX - nickSize.X / 2, ny), nickColor);

            // HP bar под именем
            if (p.MaxHealth > 0)
            {
                float barW = 34;
                float barH = 3;
                float barX = centerX - barW / 2;
                float barY = py - 8;
                float hpPct = Math.Clamp((float)p.Health / p.MaxHealth, 0f, 1f);
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)barX, (int)barY, (int)barW, (int)barH), new Color(40, 10, 10));
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)barX, (int)barY, (int)(barW * hpPct), (int)barH), new Color(180, 40, 40));
            }
        }

        // Снаряды
        ProjectileRenderer.Draw(sb, startX, startY, _gridOX, _gridOY, _cellW, _cellH);

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
                Vector2 origin = font.MeasureString(ft.Text) / 2f;
                float scale = ft.Scale;
                // Чёрная обводка (4 смещённые копии) для чёткости и читаемости
                // поверх любого фона — стандартный приём ММОРПГ для всплывающего текста.
                var outline = new Color((byte)0, (byte)0, (byte)0, (byte)(alpha * 0.8f));
                float o = 1f * scale;
                sb.DrawString(font, ft.Text, new Vector2(fpx - o, fpy), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx + o, fpy), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy - o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy + o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                // Цветной текст поверх обводки
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy), c, 0f, origin, scale, SpriteEffects.None, 0f);
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
        Legend(4, "P", Color.Goldenrod, "вы");
        Legend(50, "$", Color.Gold, "торговец");
        Legend(130, "Q", Color.MediumPurple, "доска заданий");
        Legend(250, "*", Color.LimeGreen, "сбор");
        Legend(140, "■", Color.Green, "легкий");
        Legend(200, "■", Color.Gray, "равный");
        Legend(260, "■", Color.Orange, "сложный");
        Legend(320, "■", Color.Red, "опасный");
        Legend(380, "P", new Color(110, 230, 130), "группа");
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

        float visSpeed;
        try
        {
            var st = GameMain.Instance?.Client.Status;
            int moveMs = st?.MoveIntervalMs > 0 ? st.MoveIntervalMs : 500;
            visSpeed = 1000f / moveMs;
        }
        catch
        {
            visSpeed = 2f;
        }
        float step = visSpeed * dt;
        if (step < 0.0001f) step = 0.0001f;
        _isMoving = false;
        lock (_stateLock)
        {
            foreach (var kv in _visTarget)
            {
                var key = kv.Key; var tgt = kv.Value;
                if (!_visPos.TryGetValue(key, out var v)) { _visPos[key] = (tgt.X, tgt.Y); continue; }
                float dx = tgt.X - v.X, dy = tgt.Y - v.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (key == $"player:{_playerName}")
                {
                    if (dist > 1.5f)
                    {
                        _visPos[key] = (tgt.X, tgt.Y);
                        _isMoving = false;
                        continue;
                    }
                    if (dist > 0.0001f)
                    {
                        _isMoving = true;
                        if (Math.Abs(dx) > Math.Abs(dy)) _localFacing = dx < 0 ? "left" : "right";
                        else _localFacing = dy < 0 ? "up" : "down";
                    }
                }

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
    public float Scale = 1f;
}

public sealed class ClientProjectile
{
    public string Id = "";
    public double StartX, StartY, TargetX, TargetY;
    public string VisualType = "arrow";
    public int FlightMs = 350;
    public DateTime SpawnTime;
}

public static class ProjectileRenderer
{
    private static readonly List<ClientProjectile> _active = new();
    private static readonly object _lock = new();

    public static void Spawn(string id, double sx, double sy, double tx, double ty, string visualType, int flightMs)
    {
        lock (_lock)
        {
            _active.RemoveAll(p => p.Id == id);
            _active.Add(new ClientProjectile
            {
                Id = id, StartX = sx, StartY = sy, TargetX = tx, TargetY = ty,
                VisualType = visualType, FlightMs = flightMs, SpawnTime = DateTime.UtcNow
            });
        }
    }

    public static void OnHit(string id)
    {
        lock (_lock) { _active.RemoveAll(p => p.Id == id); }
    }

    public static void Draw(SpriteBatch sb, int startX, int startY, float gridOX, float gridOY, float cellW, float cellH)
    {
        List<ClientProjectile> snapshot;
        lock (_lock) { snapshot = _active.ToList(); }

        foreach (var p in snapshot)
        {
            float elapsed = (float)(DateTime.UtcNow - p.SpawnTime).TotalMilliseconds;
            float t = Math.Clamp(elapsed / p.FlightMs, 0f, 1f);

            double cx = p.StartX + (p.TargetX - p.StartX) * t;
            double cy = p.StartY + (p.TargetY - p.StartY) * t;

            float px = gridOX + (float)(cx - startX) * cellW + cellW / 2f;
            float py = gridOY + (float)(cy - startY) * cellH + cellH / 2f;

            if (p.VisualType == "arrow")
            {
                double dx = p.TargetX - p.StartX;
                double dy = p.TargetY - p.StartY;
                float angle = (float)Math.Atan2(dy, dx);
                var tex = SpriteCache.Pixel;
                sb.Draw(tex, new Rectangle((int)px - 4, (int)py - 1, 8, 2),
                    null, new Color(204, 170, 68), angle,
                    new Vector2(0, 0.5f), SpriteEffects.None, 0f);
            }
            else
            {
                var tex = SpriteCache.Pixel;
                int r = 4;
                sb.Draw(tex, new Rectangle((int)px - r, (int)py - r, r * 2, r * 2),
                    new Color(96, 160, 255));
            }
        }

        lock (_lock)
        {
            _active.RemoveAll(p =>
                (DateTime.UtcNow - p.SpawnTime).TotalMilliseconds > p.FlightMs + 100);
        }
    }
}
