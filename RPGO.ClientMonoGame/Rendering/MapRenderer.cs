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
    private string? _selectedEntityInfo;
    private int _moveTargetX = -1, _moveTargetY = -1;

    // Кэш маршрута до точки движения: BFS не пересчитывается каждый кадр,
    // а только при смене цели/карты/клетки игрока.
    private List<(int X, int Y)>? _cachedPath;
    private int _cachedFromX = -1, _cachedFromY = -1;
    private int _cachedTargetX = -1, _cachedTargetY = -1;
    private int _cachedMerchantX = -1, _cachedMerchantY = -1;
    private int _cachedBoardX = -1, _cachedBoardY = -1;
    private byte[]? _cachedObstacleData;
    private int _hoverTileX = -1, _hoverTileY = -1;
    private string _hoverCursorType = "";

    // Визуальная интерполяция
    private readonly Dictionary<string, (float X, float Y)> _visPos = new();
    private readonly Dictionary<string, (int X, int Y)> _visTarget = new();
    private readonly Dictionary<string, int> _visMoveMs = new();
    private readonly object _stateLock = new();
    private DateTime _lastVisTime = DateTime.UtcNow;

    private byte[]? _tileData;
    private int _tileMapWidth;
    private int _tileMapHeight;
    private string _tilesetId = "";
    private int _tileSize = 32;

    private byte[]? _obstacleData;
    private int _obstacleWidth;
    private int _obstacleHeight;

    private byte[]? _objectData;
    private int _objectMapWidth;
    private int _objectMapHeight;
    private string _objectTilesetId = "";
    private int _objectTileSize = 32;

    public void SetObjectLayerData(byte[]? data, int width, int height, string tilesetId = "", int tileSize = 32)
    {
        _objectData = data;
        _objectMapWidth = width;
        _objectMapHeight = height;
        _objectTilesetId = tilesetId;
        _objectTileSize = Math.Max(1, tileSize);
    }

    public void SetTileData(byte[]? data, int width, int height, string tilesetId = "", int tileSize = 32)
    {
        _tileData = data;
        _tileMapWidth = width;
        _tileMapHeight = height;
        _tilesetId = tilesetId;
        _tileSize = tileSize;
    }

    public void SetObstacleData(byte[]? data, int width, int height)
    {
        _obstacleData = data;
        _obstacleWidth = width;
        _obstacleHeight = height;
    }

    /// <summary>Непроходима ли клетка (данные препятствий с сервера).</summary>
    public bool IsBlocked(int x, int y)
    {
        if (_obstacleData == null) return false;
        if (x < 0 || y < 0 || x >= _obstacleWidth || y >= _obstacleHeight) return false;
        int idx = y * _obstacleWidth + x;
        if (idx < 0 || idx >= _obstacleData.Length) return false;
        return _obstacleData[idx] != 0;
    }
    private readonly List<FloatingText> _floatingTexts = new();
    private static readonly Random _rng = new();

    // Снаряды
    private readonly List<ClientProjectile> _projectiles = new();

    // Видимая область
    private int _viewStartX, _viewStartY, _viewEndX, _viewEndY;

    // Spatial hash для быстрого поиска сущностей по координатам — O(1) вместо O(N)
    private readonly Dictionary<(int X, int Y), List<EntityInfo>> _spatialHash = new();

    // Фактический размер клетки (подгоняется под экран, чтобы не было зазоров)
    private float _cellW = 22f;
    private float _cellH = 22f;
    private float _gridOX = 4f;
    private float _gridOY = 18f;

    // Масштаб карты (зум колесом мыши)
    private float _zoom = 1.5f;
    public float Zoom => _zoom;
    public void ChangeZoom(float delta)
    {
        _zoom = Math.Clamp(_zoom + delta, 1f, 4f);
    }

    // Плавная позиция камеры (float), следует за интерполированной позицией игрока
    private float _camX = 50f;
    private float _camY = 50f;
    private DateTime _lastFrameTime = DateTime.UtcNow;

    public (int X, int Y) CameraCenter => ((int)Math.Floor(_camX), (int)Math.Floor(_camY));

    // Базовые размеры клеток (квадратные, как тайлы в тайлсете 32x32)
    private const float BaseCellW = 22f;
    private const float BaseCellH = 22f;
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
        if (dead && !_isDead)
        {
            _deathFrame = 0;
            _deathAnimStart = DateTime.UtcNow;
            if (_visPos.TryGetValue($"player:{_playerName}", out var v))
            {
                _localDeathX = (int)Math.Round(v.X);
                _localDeathY = (int)Math.Round(v.Y);
            }
        }
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

    public void SetOffHandWeaponSubtype(string? subtype)
    {
        if (_offWeaponSubtype != subtype)
        {
            Logger.Debug($"SetOffHandWeaponSubtype: '{_offWeaponSubtype}' -> '{subtype}'");
            _offWeaponSubtype = subtype;
        }
    }

    public void SetTwoHanded(bool twoHanded)
    {
        if (_isTwoHanded != twoHanded)
        {
            Logger.Debug($"SetTwoHanded: {_isTwoHanded} -> {twoHanded}");
            _isTwoHanded = twoHanded;
        }
    }

    public void TriggerAttack(string hand = "main")
    {
        if (hand == "off")
        {
            if (!_offAttackActive)
                _offAttackStart = DateTime.UtcNow;
            _offAttackActive = true;
            _mainAttackActive = false;
        }
        else
        {
            if (!_mainAttackActive)
                _mainAttackStart = DateTime.UtcNow;
            _mainAttackActive = true;
            _offAttackActive = false;
        }
    }

    // Направление взгляда локального игрока ("down" | "up" | "left" | "right").
    // Вычисляется в AdvanceVisPositions по фактическому вектору движения.
    private string _localFacing = "down";

    // Игрок в данный момент движется (интерполяция не завершена).
    private bool _isMoving;

    // Активен ли бафф «Подавляющий огонь» — рисовать конус
    private bool _suppressingFireActive;
    public void SetSuppressingFireActive(bool active) => _suppressingFireActive = active;

    // Игрок мёртв — показываем death-анимацию вместо walk/idle.
    private bool _isDead;
    private int _deathFrame;
    private DateTime _deathAnimStart;
    private int _localDeathX;
    private int _localDeathY;

    // Атака основной рукой
    private bool _mainAttackActive;
    private DateTime _mainAttackStart;

    // Атака второй рукой
    private bool _offAttackActive;
    private DateTime _offAttackStart;

    // Подтип оружия в правой руке (для оверлея). null = нет оружия.
    private string? _weaponSubtype;
    private bool _weaponLogOnce = true;

    // Подтип левого (второго) оружия (для оверлея). null = нет оружия.
    private string? _offWeaponSubtype;

    // Подтип щита в левой руке (для оверлея). null = нет щита.
    private string? _shieldSubtype;

    // Двуручное оружие — специальная анимация тела и нет off-hand.
    private bool _isTwoHanded;

    // Per-player visual state for remote players
    private readonly Dictionary<string, RemotePlayerState> _remotePlayers = new();
    // Per-player movement state for remote players (visPos != visTarget)
    private readonly Dictionary<string, bool> _remoteMoving = new();
    // Callback: local player changed facing → send to server
    internal Action<string>? OnFacingChanged;
    private string _lastRenderFacing = "down";

private sealed class RemotePlayerState
{
    public string Facing = "down";
    public string WeaponSubtype = "";
    public string OffWeaponSubtype = "";
    public string ShieldSubtype = "";
    public bool IsTwoHanded;
    public bool MainAttackActive;
    public bool OffAttackActive;
    public DateTime MainAttackStart;
    public DateTime OffAttackStart;
    public bool IsDead;
    public DateTime DeathStart;
    public int DeathX;
    public int DeathY;
}

    public void UpdateRemotePlayer(string name, string facing, string weaponSub, string offWeaponSub, string shieldSub, bool twoHanded, bool isDead, int deathX, int deathY)
    {
        if (!_remotePlayers.TryGetValue(name, out var state))
        {
            state = new RemotePlayerState();
            _remotePlayers[name] = state;
        }
        state.Facing = facing;
        state.WeaponSubtype = weaponSub;
        state.OffWeaponSubtype = offWeaponSub;
        state.ShieldSubtype = shieldSub;
        state.IsTwoHanded = twoHanded;
        if (isDead && !state.IsDead)
        {
            state.IsDead = true;
            state.DeathStart = DateTime.UtcNow;
            state.DeathX = deathX;
            state.DeathY = deathY;
        }
        else if (!isDead)
        {
            state.IsDead = false;
        }
    }

    public void TriggerRemoteAttack(string playerName, string hand)
    {
        if (!_remotePlayers.TryGetValue(playerName, out var state)) return;
        if (hand == "off")
        {
            if (!state.OffAttackActive)
                state.OffAttackStart = DateTime.UtcNow;
            state.OffAttackActive = true;
            state.MainAttackActive = false;
        }
        else
        {
            if (!state.MainAttackActive)
                state.MainAttackStart = DateTime.UtcNow;
            state.MainAttackActive = true;
            state.OffAttackActive = false;
        }
    }

    public void DrawSkillEffects(SpriteBatch sb, float offsetX, float offsetY, float areaW, float areaH)
    {
        // Координаты сетки уже посчитаны в Draw() — используем те же _gridOX/_viewStart/_cell*
        SkillEffectManager.Draw(sb, _gridOX, _gridOY, _viewStartX, _viewStartY, _cellW, _cellH);
        HazardRenderer.Draw(sb, _gridOX, _gridOY, _viewStartX, _viewStartY, _cellW, _cellH);

        // OnPlayer VFX поверх локального персонажа
        var me = _currentMap?.Players.FirstOrDefault(p => p.Name == _playerName);
        if (me != null)
        {
            float vx = me.X, vy = me.Y;
            lock (_stateLock)
            {
                if (_visPos.TryGetValue($"player:{_playerName}", out var v))
                { vx = v.X; vy = v.Y; }
            }
            float sx = _gridOX + (vx - _viewStartX) * _cellW + _cellW / 2f;
            float sy = _gridOY + (vy - _viewStartY) * _cellH + _cellH / 2f;
            SkillEffectManager.DrawOnPlayer(sb, sx, sy, _cellW, _cellH);
        }
    }

    public Point? GetRemotePlayerPos(string name)
    {
        lock (_stateLock)
        {
            if (_currentMap == null) return null;
            var p = _currentMap.Players.FirstOrDefault(pp => pp.Name == name);
            if (p == null) return null;
            return new Point(p.X, p.Y);
        }
    }

    public Point? GetSelectedMapPos()
    {
        if (_selectedEntityType == null || _selectedEntityType == "move") return null;
        return new Point(_selectedEntityX, _selectedEntityY);
    }

    public void UpdateRemotePlayerFacing(string name, string facing)
    {
        if (_remotePlayers.TryGetValue(name, out var state))
            state.Facing = facing;
    }

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
                if ((manhattan <= 1 || _mainAttackActive || _offAttackActive) && (ddx != 0 || ddy != 0))
                {
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

    /// <summary>Видимая область главной карты (в координатах карты) — для рамки на миникарте.</summary>
    public Rectangle GetViewBounds()
    {
        lock (_stateLock)
        {
            return new Rectangle(_viewStartX, _viewStartY,
                Math.Max(0, _viewEndX - _viewStartX + 1),
                Math.Max(0, _viewEndY - _viewStartY + 1));
        }
    }

    public void SetMap(WorldMap map)
    {
        lock (_stateLock)
        {
            _currentMap = map;
            RebuildSpatialHash(map);
        }
    }

    private void RebuildSpatialHash(WorldMap map)
    {
        _spatialHash.Clear();
        foreach (var p in map.Players)
        {
            if (p.Name == _playerName) continue;
            var key = (p.X, p.Y);
            if (!_spatialHash.TryGetValue(key, out var list))
            {
                list = new List<EntityInfo>();
                _spatialHash[key] = list;
            }
            list.Add(new EntityInfo { Type = "player", Name = p.Name, Level = p.Level, Hp = p.Health, MaxHp = p.MaxHealth, X = p.X, Y = p.Y, Id = p.Id.ToString() });
        }
        foreach (var m in map.Monsters)
        {
            var key = (m.X, m.Y);
            if (!_spatialHash.TryGetValue(key, out var list))
            {
                list = new List<EntityInfo>();
                _spatialHash[key] = list;
            }
            list.Add(new EntityInfo { Type = "monster", Name = m.Name, Level = m.Level, Hp = m.Health, MaxHp = m.MaxHealth, X = m.X, Y = m.Y, Id = m.Id.ToString() });
        }
        if (map.Merchant != null)
        {
            var key = (map.Merchant.X, map.Merchant.Y);
            if (!_spatialHash.TryGetValue(key, out var list))
            {
                list = new List<EntityInfo>();
                _spatialHash[key] = list;
            }
            list.Add(new EntityInfo { Type = "merchant", Name = map.Merchant.Name, X = map.Merchant.X, Y = map.Merchant.Y });
        }
        if (map.Board != null)
        {
            var key = (map.Board.X, map.Board.Y);
            if (!_spatialHash.TryGetValue(key, out var list))
            {
                list = new List<EntityInfo>();
                _spatialHash[key] = list;
            }
            list.Add(new EntityInfo { Type = "board", Name = "Доска заданий", X = map.Board.X, Y = map.Board.Y });
        }
        if (map.StorageChest != null)
        {
            var key = (map.StorageChest.X, map.StorageChest.Y);
            if (!_spatialHash.TryGetValue(key, out var list))
            {
                list = new List<EntityInfo>();
                _spatialHash[key] = list;
            }
            list.Add(new EntityInfo { Type = "storage_chest", Name = "Склад", X = map.StorageChest.X, Y = map.StorageChest.Y });
        }
        foreach (var c in map.Collectibles ?? Enumerable.Empty<CollectiblePosition>())
        {
            var key = (c.X, c.Y);
            if (!_spatialHash.TryGetValue(key, out var list))
            {
                list = new List<EntityInfo>();
                _spatialHash[key] = list;
            }
            list.Add(new EntityInfo { Type = "collectible", Name = c.Name, X = c.X, Y = c.Y, Id = c.Id });
        }
        foreach (var cs in map.Corpses ?? Enumerable.Empty<CorpsePosition>())
        {
            var key = (cs.X, cs.Y);
            if (!_spatialHash.TryGetValue(key, out var list))
            {
                list = new List<EntityInfo>();
                _spatialHash[key] = list;
            }
            list.Add(new EntityInfo { Type = "corpse", Name = cs.MonsterName, Level = cs.Level, X = cs.X, Y = cs.Y, Id = cs.Id.ToString() });
        }
        foreach (var n in map.Npcs ?? Enumerable.Empty<NpcPosition>())
        {
            if (n.Type == "merchant" || n.Type == "board") continue;
            var key = (n.X, n.Y);
            if (!_spatialHash.TryGetValue(key, out var list))
            {
                list = new List<EntityInfo>();
                _spatialHash[key] = list;
            }
            list.Add(new EntityInfo { Type = "npc", Name = n.Name, X = n.X, Y = n.Y, Id = n.Id });
        }
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
                Scale = isCrit ? 1.6f : 1.2f
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
        var map = _currentMap;
        if (map != null)
        {
            // Сначала ищем по ID по всей карте (сущность могла переместиться)
            if (_selectedEntityType == "monster" && _selectedEntityId != null)
            {
                var mon = map.Monsters.FirstOrDefault(m => m.Id.ToString() == _selectedEntityId);
                if (mon != null)
                    return new EntityInfo { Type = "monster", Name = mon.Name, Level = mon.Level,
                        Hp = mon.Health, MaxHp = mon.MaxHealth, X = mon.X, Y = mon.Y, Id = mon.Id.ToString() };
            }
            if (_selectedEntityType == "player" && _selectedEntityName != null)
            {
                var pl = map.Players.FirstOrDefault(p => p.Name == _selectedEntityName);
                if (pl != null)
                    return new EntityInfo { Type = "player", Name = pl.Name, Level = pl.Level,
                        Hp = pl.Health, MaxHp = pl.MaxHealth, X = pl.X, Y = pl.Y, Id = pl.Id.ToString() };
            }
            if (_selectedEntityType == "corpse" && _selectedEntityId != null)
            {
                var corpse = map.Corpses.FirstOrDefault(c => c.Id.ToString() == _selectedEntityId);
                if (corpse != null)
                    return new EntityInfo { Type = "corpse", Name = corpse.MonsterName, Level = corpse.Level,
                        X = corpse.X, Y = corpse.Y, Id = corpse.Id.ToString() };
            }
        }
        // Фолбэк, если точного совпадения нет
        return new EntityInfo
        {
            Type = _selectedEntityType,
            Name = _selectedEntityName ?? "",
            X = _selectedEntityX,
            Y = _selectedEntityY,
            Id = _selectedEntityId,
            Info = _selectedEntityInfo
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
        _selectedEntityInfo = entity.Info;
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
            X = _selectedEntityX, Y = _selectedEntityY, Id = _selectedEntityId,
            Info = _selectedEntityInfo
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

        var portalInfo = GetPortalSelection(mapX, mapY);
        if (portalInfo != null)
        {
            HandleSingleEntityClick(portalInfo, mapX, mapY);
            return;
        }

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
        if (entitiesOnCell.Count == 0)
        {
            HandleEmptyCellRightClick(mapX, mapY);
        }
        else if (entitiesOnCell.Count == 1)
        {
            HandleSingleEntityRightClick(entitiesOnCell[0], mapX, mapY);
        }
        else
        {
            EntityPickRequested?.Invoke(entitiesOnCell, mapX, mapY);
        }
    }

    private void HandleEmptyCellRightClick(int mapX, int mapY)
    {
        if (IsBlocked(mapX, mapY)) return;
        ClearSelection();
        _selectedEntityType = "move";
        _selectedEntityName = "Точка назначения";
        _selectedEntityX = mapX; _selectedEntityY = mapY;
        _moveTargetX = mapX; _moveTargetY = mapY;
        SelectionChanged?.Invoke(GetSelection());
        MoveRequested?.Invoke(mapX, mapY);
    }

    public string GetCursorType(float screenX, float screenY, float areaW, float areaH)
    {
        if (_currentMap == null) return "main";
        if (!ScreenToMap(screenX, screenY, areaW, areaH, out int mapX, out int mapY))
        {
            _hoverTileX = _hoverTileY = -1;
            return "main";
        }
        _hoverTileX = mapX;
        _hoverTileY = mapY;

        string ct;
        if (_currentMap.Portals != null && _currentMap.Portals.Any(p => p.X == mapX && p.Y == mapY))
        {
            ct = "portal";
            _hoverCursorType = ct;
            return ct;
        }

        var entities = GetEntitiesAt(mapX, mapY);
        if (entities.Count > 0)
        {
            var first = entities[0];
            ct = first.Type switch
            {
                "monster" => "attack",
                "corpse" => "loot",
                "collectible" => "harvest",
                "npc" or "merchant" or "board" or "storage_chest" => "talk",
                "player" when _currentMap?.PvPEnabled == true => "attack",
                "player" => "player",
                _ => "main"
            };
            _hoverCursorType = ct;
            return ct;
        }

        ct = IsBlocked(mapX, mapY) ? "blockmoving" : "moving";
        _hoverCursorType = ct;
        return ct;
    }

    public void ClearHoverTile() { _hoverTileX = _hoverTileY = -1; _hoverCursorType = ""; }

    // Запрос окна выбора сущности, когда в клетке несколько сущностей
    public event Action<List<EntityInfo>, int, int>? EntityPickRequested;

    private List<EntityInfo> GetEntitiesAt(int mapX, int mapY)
    {
        if (_spatialHash.TryGetValue((mapX, mapY), out var list))
            return list;
        return new List<EntityInfo>();
    }

    private EntityInfo? GetPortalSelection(int mapX, int mapY)
    {
        var map = _currentMap;
        if (map == null) return null;
        if (map.Portals != null)
        {
            var portal = map.Portals.FirstOrDefault(p => p.X == mapX && p.Y == mapY);
            if (portal != null)
            {
                return new EntityInfo
                {
                    Type = "portal",
                    Name = "Портал",
                    X = mapX,
                    Y = mapY,
                    Id = portal.TargetZone,
                    Info = string.IsNullOrEmpty(portal.TargetZoneName) ? portal.TargetZone : portal.TargetZoneName
                };
            }
        }
        if (map.InstanceExitPortal != null && map.InstanceExitPortal.X == mapX && map.InstanceExitPortal.Y == mapY)
        {
            var exit = map.InstanceExitPortal;
            return new EntityInfo
            {
                Type = "portal",
                Name = "Выход из подземелья",
                X = mapX,
                Y = mapY,
                Id = "instance_exit",
                Info = string.IsNullOrEmpty(exit.TargetZoneName) ? "Главный мир" : exit.TargetZoneName
            };
        }
        return null;
    }

    private void HandleEmptyCellClick(int mapX, int mapY)
    {
        if (IsBlocked(mapX, mapY)) return;
        ClearSelection();
        _selectedEntityType = "move";
        _selectedEntityName = "Точка назначения";
        _selectedEntityX = mapX; _selectedEntityY = mapY;
        _moveTargetX = mapX; _moveTargetY = mapY;
        SelectionChanged?.Invoke(GetSelection());
    }

    private void HandleSingleEntityClick(EntityInfo entity, int mapX, int mapY)
    {
        StartInteraction(entity, mapX, mapY);
    }

    private void HandleSingleEntityRightClick(EntityInfo entity, int mapX, int mapY)
    {
        StartInteraction(entity, mapX, mapY);
        InteractRequested?.Invoke(entity, mapX, mapY);
    }

    public void ClearSelection()
    {
        _selectedEntityType = null; _selectedEntityName = null;
        _selectedEntityX = _selectedEntityY = 0; _selectedEntityId = null;
        _selectedEntityInfo = null;
        _moveTargetX = _moveTargetY = -1;
        ClearPathCache();
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
        float availW = areaW - LeftMargin - 4;
        float availH = areaH - HeaderH - 4;

        // Квадратные клетки: сколько влезет при идеальном размере
        float baseCell = BaseCellW * _zoom;
        int cols = Math.Max(1, (int)(availW / baseCell));
        int rows = Math.Max(1, (int)(availH / baseCell));

        // Фактический размер — берём меньший, чтобы влезло и по W и по H
        _cellW = _cellH = Math.Min(availW / cols, availH / rows);

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

        // Центрируем сетку
        float gridTotalW = viewW * _cellW;
        float gridTotalH = viewH * _cellH;
        _gridOX = offsetX + LeftMargin + (availW - gridTotalW) / 2f;
        _gridOY = offsetY + HeaderH + (availH - gridTotalH) / 2f;
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

        sb.Draw(SpriteCache.Pixel, new Rectangle((int)offsetX, (int)offsetY, (int)areaW, (int)areaH), new Color(235, 240, 225));

        if (map == null)
        {
            sb.DrawString(font, "Карта загружается...", new Vector2(offsetX + 10, offsetY + 10), Color.Gray);
            return;
        }

        var me = map.Players.FirstOrDefault(p => p.Name == _playerName);
        UpdateVisualInterpolation(map);

        float targetX = me?.X ?? (map.Merchant?.X ?? 50);
        float targetY = me?.Y ?? (map.Merchant?.Y ?? 50);
        if (me != null)
        {
            lock (_stateLock)
            {
                if (_visPos.TryGetValue($"player:{me.Name}", out var v))
                { targetX = v.X; targetY = v.Y; }
            }
        }
        _camX = targetX;
        _camY = targetY;

        int centerX = (int)Math.Floor(_camX);
        int centerY = (int)Math.Floor(_camY);
        ComputeView(map, centerX, centerY, offsetX, offsetY, areaW, areaH);

        float subCellX = (_camX - centerX) * _cellW;
        float subCellY = (_camY - centerY) * _cellH;
        _gridOX -= subCellX;
        _gridOY -= subCellY;

        DrawTiles(sb, map, offsetX, offsetY, areaW, areaH);
        DrawPortalsAndObjects(sb, map);
        DrawPathDots(sb, map, me);
        DrawEntities(sb, font, fontSmall, offsetX, offsetY, _viewStartX, _viewStartY, _viewEndX, _viewEndY, me);
        DrawDeathMarkers(sb);
        DrawObjectLayer(sb, offsetX, offsetY, areaW, areaH);
        int viewH = _viewEndY - _viewStartY + 1;
        int legendY = (int)(_gridOY + viewH * _cellH + 4);
        DrawLegend(sb, font, fontSmall, offsetX, legendY);
    }

    private void UpdateVisualInterpolation(WorldMap map)
    {
        var liveKeys = new HashSet<string>();
        foreach (var p in map.Players) { var k = $"player:{p.Name}"; SetVisTarget(k, p.X, p.Y); liveKeys.Add(k); }
        foreach (var m in map.Monsters)
        {
            var k = $"monster:{m.Id}";
            SetVisTarget(k, m.X, m.Y);
            _visMoveMs[k] = m.MoveIntervalMs > 0 ? m.MoveIntervalMs : 500;
            liveKeys.Add(k);
        }
        lock (_stateLock)
        {
            foreach (var k in _visTarget.Keys.ToList())
                if (!liveKeys.Contains(k)) { _visTarget.Remove(k); _visPos.Remove(k); _visMoveMs.Remove(k); }
        }
        AdvanceVisPositions();
    }

    private void DrawTiles(SpriteBatch sb, WorldMap map, float offsetX, float offsetY, float areaW, float areaH)
    {
        bool isSandy = map.ZoneId == "arena";
        var grass = isSandy ? SpriteCache.GetSandSprite() : SpriteCache.GetGrassSprite();
        int viewW = _viewEndX - _viewStartX + 1;
        int viewH = _viewEndY - _viewStartY + 1;

        bool hasTileset = _tileData != null
            && _tileMapWidth == map.Width && _tileMapHeight == map.Height
            && _tileData.Length == map.Width * map.Height;

        Texture2D? tilesetTex = null;
        int tilesetCols = 1, tilesetRows = 1;
        if (hasTileset && !string.IsNullOrEmpty(_tilesetId))
        {
            tilesetTex = SpriteCache.GetTileset(_tilesetId, _tileSize, out tilesetCols, out tilesetRows);
            if (tilesetTex == null) hasTileset = false;
        }

        int tilePxW = (int)Math.Ceiling(_cellW);
        int tilePxH = (int)Math.Ceiling(_cellH);

        if (hasTileset && tilesetTex != null)
        {
            int srcTileW = Math.Max(1, _tileSize);
            int srcTileH = Math.Max(1, _tileSize);
            for (int y = -1; y <= viewH + 1; y++)
            {
                float ty = _gridOY + y * _cellH;
                if (ty > offsetY + areaH) continue;
                for (int x = -1; x <= viewW + 1; x++)
                {
                    float tx = _gridOX + x * _cellW;
                    if (tx > offsetX + areaW) continue;
                    int mx = _viewStartX + x;
                    int my = _viewStartY + y;
                    if (mx < 0 || my < 0 || mx >= _tileMapWidth || my >= _tileMapHeight) continue;
                    byte tileId = _tileData![my * _tileMapWidth + mx];
                    if (tileId == 0)
                    {
                        sb.Draw(grass, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), Color.White);
                        continue;
                    }
                    if (tileId == 255)
                    {
                        sb.Draw(SpriteCache.Pixel, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), new Color(40, 40, 45));
                        continue;
                    }
                    int tCol = (tileId - 1) % tilesetCols;
                    int tRow = (tileId - 1) / tilesetCols;
                    if (tRow >= tilesetRows || tCol >= tilesetCols)
                    {
                        sb.Draw(grass, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), Color.White);
                        continue;
                    }
                    var src = new Rectangle(tCol * srcTileW, tRow * srcTileH, srcTileW, srcTileH);
                    sb.Draw(tilesetTex, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), src, Color.White);
                }
            }
        }
        else if (_tileData != null && _tileMapWidth > 0 && _tileMapHeight > 0
                 && _tileMapWidth == map.Width && _tileMapHeight == map.Height
                 && _tileData.Length == map.Width * map.Height)
        {
            for (int y = -1; y <= viewH + 1; y++)
            {
                float ty = _gridOY + y * _cellH;
                if (ty > offsetY + areaH) continue;
                for (int x = -1; x <= viewW + 1; x++)
                {
                    float tx = _gridOX + x * _cellW;
                    if (tx > offsetX + areaW) continue;
                    int mx = _viewStartX + x;
                    int my = _viewStartY + y;
                    if (mx < 0 || my < 0 || mx >= _tileMapWidth || my >= _tileMapHeight)
                    {
                        sb.Draw(SpriteCache.Pixel, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), new Color(40, 40, 45));
                        continue;
                    }
                    byte tileId = _tileData[my * _tileMapWidth + mx];
                    if (tileId == 255)
                        sb.Draw(SpriteCache.Pixel, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), new Color(40, 40, 45));
                    else if (grass != null)
                        sb.Draw(grass, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), Color.White);
                    else
                        sb.Draw(SpriteCache.Pixel, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), Color.LightGreen);
                }
            }
        }
        else
        {
            for (int y = -1; y <= viewH + 1; y++)
            {
                float ty = _gridOY + y * _cellH;
                if (ty > offsetY + areaH) continue;
                for (int x = -1; x <= viewW + 1; x++)
                {
                    float tx = _gridOX + x * _cellW;
                    if (tx > offsetX + areaW) continue;
                    if (grass != null)
                        sb.Draw(grass, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), Color.White);
                    else
                        sb.Draw(SpriteCache.Pixel, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), Color.LightGreen);
                }
            }
        }
    }

    /// <summary>
    /// Рисует слой объектов (деревья и т.п.) поверх всех сущностей. 0 — прозрачная клетка.
    /// </summary>
    private void DrawObjectLayer(SpriteBatch sb, float offsetX, float offsetY, float areaW, float areaH)
    {
        if (_objectData == null || _objectMapWidth <= 0 || _objectMapHeight <= 0) return;
        if (string.IsNullOrEmpty(_objectTilesetId)) return;

        Texture2D? tex = SpriteCache.GetTileset(_objectTilesetId, _objectTileSize, out int cols, out int rows);
        if (tex == null) return;

        int tilePxW = (int)Math.Ceiling(_cellW);
        int tilePxH = (int)Math.Ceiling(_cellH);
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
                int mx = _viewStartX + x;
                int my = _viewStartY + y;
                if (mx < 0 || my < 0 || mx >= _objectMapWidth || my >= _objectMapHeight) continue;
                byte tileId = _objectData[my * _objectMapWidth + mx];
                if (tileId == 0) continue; // пустая клетка — ничего не рисуем

                int tCol = (tileId - 1) % cols;
                int tRow = (tileId - 1) / cols;
                if (tRow >= rows || tCol >= cols) continue;
                var src = new Rectangle(tCol * _objectTileSize, tRow * _objectTileSize, _objectTileSize, _objectTileSize);
                sb.Draw(tex, new Rectangle((int)tx, (int)ty, tilePxW + 2, tilePxH + 2), src, Color.White);
            }
        }
    }

    private void DrawPortalsAndObjects(SpriteBatch sb, WorldMap map)
    {
        if (map.Portals != null)
        {
            foreach (var portal in map.Portals)
            {
                int px = portal.X, py = portal.Y;
                if (px >= _viewStartX && px <= _viewEndX && py >= _viewStartY && py <= _viewEndY)
                {
                    var portalTex = SpriteCache.GetSprite("portal");
                    float ptx = _gridOX + (px - _viewStartX) * _cellW;
                    float pty = _gridOY + (py - _viewStartY) * _cellH;
                    if (portalTex != null)
                    {
                        sb.Draw(portalTex, new Rectangle((int)ptx - 2, (int)pty - 2, (int)_cellW + 4, (int)_cellH + 4), Color.White);
                    }
                    else
                    {
                        sb.Draw(SpriteCache.Pixel, new Rectangle((int)ptx, (int)pty, (int)_cellW, (int)_cellH), new Color(120, 60, 200, 180));
                        sb.Draw(SpriteCache.Pixel, new Rectangle((int)ptx + 2, (int)pty + 2, (int)_cellW - 4, (int)_cellH - 4), new Color(160, 100, 255, 200));
                    }
                }
            }
        }
        if (map.InstanceExitPortal != null)
        {
            int px = map.InstanceExitPortal.X, py = map.InstanceExitPortal.Y;
            if (px >= _viewStartX && px <= _viewEndX && py >= _viewStartY && py <= _viewEndY)
            {
                float ptx = _gridOX + (px - _viewStartX) * _cellW;
                float pty = _gridOY + (py - _viewStartY) * _cellH;
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)ptx, (int)pty, (int)_cellW, (int)_cellH), new Color(60, 180, 80, 180));
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)ptx + 2, (int)pty + 2, (int)_cellW - 4, (int)_cellH - 4), new Color(100, 220, 120, 200));
            }
        }
        if (map.InstanceChest != null)
        {
            int px = map.InstanceChest.X, py = map.InstanceChest.Y;
            if (px >= _viewStartX && px <= _viewEndX && py >= _viewStartY && py <= _viewEndY)
            {
                var chestSheet = SpriteCache.Get("chest_ss");
                if (chestSheet != null)
                {
                    int frameIdx = map.InstanceChest.IsLocked ? 2 : 3;
                    var srcRect = new Rectangle(frameIdx * 64, 0, 64, 64);
                    float ptx = _gridOX + (px - _viewStartX) * _cellW;
                    float pty = _gridOY + (py - _viewStartY) * _cellH;
                    sb.Draw(chestSheet, new Rectangle((int)ptx - 2, (int)pty - 2, (int)_cellW + 4, (int)_cellH + 4), srcRect, Color.White);
                }
                else
                {
                    float ptx = _gridOX + (px - _viewStartX) * _cellW;
                    float pty = _gridOY + (py - _viewStartY) * _cellH;
                    Color chestColor = map.InstanceChest.IsLocked
                        ? new Color(120, 80, 40, 200) : new Color(220, 180, 50, 200);
                    sb.Draw(SpriteCache.Pixel, new Rectangle((int)ptx, (int)pty, (int)_cellW, (int)_cellH), chestColor);
                    sb.Draw(SpriteCache.Pixel, new Rectangle((int)ptx + 4, (int)pty + 4, (int)_cellW - 8, (int)_cellH - 8),
                        map.InstanceChest.IsLocked ? new Color(90, 60, 30, 200) : new Color(255, 215, 80, 220));
                }
            }
        }
    }

    private void DrawPathDots(SpriteBatch sb, WorldMap map, PlayerPosition? me)
    {
        if (_moveTargetX < 0 || _moveTargetY < 0 || me == null) return;
        int mx = map.Merchant?.X ?? -1, my = map.Merchant?.Y ?? -1;
        int bx = map.Board?.X ?? -1, by = map.Board?.Y ?? -1;

        // Пересчитываем маршрут только когда изменились входные данные BFS:
        // цель движения, позиция торговца/доски, препятствия или клетка игрока.
        if (_cachedPath == null
            || _cachedTargetX != _moveTargetX || _cachedTargetY != _moveTargetY
            || _cachedMerchantX != mx || _cachedMerchantY != my
            || _cachedBoardX != bx || _cachedBoardY != by
            || !ReferenceEquals(_cachedObstacleData, _obstacleData)
            || _cachedFromX != me.X || _cachedFromY != me.Y)
        {
            _cachedPath = ClientPathfinding.FindPath(me.X, me.Y, _moveTargetX, _moveTargetY, mx, my, bx, by, map.Width, map.Height, IsBlocked);
            _cachedFromX = me.X; _cachedFromY = me.Y;
            _cachedTargetX = _moveTargetX; _cachedTargetY = _moveTargetY;
            _cachedMerchantX = mx; _cachedMerchantY = my;
            _cachedBoardX = bx; _cachedBoardY = by;
            _cachedObstacleData = _obstacleData;
        }

        if (_cachedPath.Count == 0 && (me.X != _moveTargetX || me.Y != _moveTargetY)) { _moveTargetX = _moveTargetY = -1; ClearPathCache(); return; }
        if (me.X == _moveTargetX && me.Y == _moveTargetY) { _moveTargetX = _moveTargetY = -1; ClearPathCache(); return; }
        var pathColor = new Color(220, 200, 80, 180);
        foreach (var (px, py) in _cachedPath)
        {
            if (px >= _viewStartX && px <= _viewEndX && py >= _viewStartY && py <= _viewEndY)
            {
                float dotX = _gridOX + (px - _viewStartX) * _cellW + _cellW / 2 - 3;
                float dotY = _gridOY + (py - _viewStartY) * _cellH + _cellH / 2 - 3;
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)dotX, (int)dotY, 6, 6), pathColor);
            }
        }
    }

    private void ClearPathCache()
    {
        _cachedPath = null;
        _cachedFromX = _cachedFromY = -1;
        _cachedTargetX = _cachedTargetY = -1;
        _cachedMerchantX = _cachedMerchantY = -1;
        _cachedBoardX = _cachedBoardY = -1;
        _cachedObstacleData = null;
    }

    private void DrawDeathMarkers(SpriteBatch sb)
    {
        var deathNow = DateTime.UtcNow;
        foreach (var kvp in _remotePlayers)
        {
            var rp = kvp.Value;
            if (!rp.IsDead) continue;
            var elapsed = (deathNow - rp.DeathStart).TotalSeconds;
            if (elapsed > 60) continue;
            if (rp.DeathX < _viewStartX || rp.DeathX > _viewEndX || rp.DeathY < _viewStartY || rp.DeathY > _viewEndY) continue;
            float ax = _gridOX + (rp.DeathX - _viewStartX) * _cellW + _cellW / 2;
            float ay = _gridOY + (rp.DeathY - _viewStartY) * _cellH + _cellH / 2;
            var ashes = SpriteCache.GetCorpseSprite();
            if (ashes != null)
                sb.Draw(ashes, new Vector2(ax - ashes.Width / 2f, ay - ashes.Height / 2f), Color.White);
        }
        if (_isDead)
        {
            var elapsed = (deathNow - _deathAnimStart).TotalSeconds;
            if (elapsed <= 60)
            {
                float ax = _gridOX + (_localDeathX - _viewStartX) * _cellW + _cellW / 2;
                float ay = _gridOY + (_localDeathY - _viewStartY) * _cellH + _cellH / 2;
                var ashes = SpriteCache.GetCorpseSprite();
                if (ashes != null)
                    sb.Draw(ashes, new Vector2(ax - ashes.Width / 2f, ay - ashes.Height / 2f), Color.White);
            }
        }
    }

    private void DrawEntities(SpriteBatch sb, SpriteFont font, SpriteFont fontSmall, float offsetX, float offsetY, int startX, int startY, int endX, int endY, PlayerPosition? me)
    {
        WorldMap? map;
        lock (_stateLock) { map = _currentMap; }
        if (map == null) return;

        DrawStaticEntities(sb, map, startX, startY, endX, endY);
        DrawMonsterSprites(sb, font, map, startX, startY, endX, endY);
        DrawPlayerSprites(sb, font, map, startX, startY, endX, endY);
        DrawLabelsAndHP(sb, fontSmall, map, startX, startY, endX, endY);
        ProjectileRenderer.Draw(sb, startX, startY, _gridOX, _gridOY, _cellW, _cellH);
        DrawFloatingTexts(sb, font, startX, startY);
        DrawHoverTile(sb);
        DrawSelectionHighlight(sb, map, startX, startY, endX, endY);
    }

    private static Rectangle EntityRect(float px, float py, float cellW, float cellH)
    {
        int w = (int)(cellW * EntityScale);
        int h = (int)(cellH * EntityScale);
        return new Rectangle((int)px - w / 2 + (int)(cellW / 2), (int)py - h / 2 + (int)(cellH / 2), w, h);
    }

    private void DrawStatic(SpriteBatch sb, Texture2D? spr, int wx, int wy, int startX, int startY, int endX, int endY, Color tint)
    {
        if (wx < startX || wx > endX || wy < startY || wy > endY) return;
        float px = _gridOX + (wx - startX) * _cellW;
        float py = _gridOY + (wy - startY) * _cellH;
        if (spr != null)
            sb.Draw(spr, new Rectangle((int)px - 2, (int)py - 2, (int)_cellW + 4, (int)_cellH + 4), tint);
        else
            sb.Draw(SpriteCache.Pixel, new Rectangle((int)px, (int)py, (int)_cellW, (int)_cellH), tint);
    }

    private void DrawStaticEntities(SpriteBatch sb, WorldMap map, int startX, int startY, int endX, int endY)
    {
        if (map.Merchant != null && map.Merchant.X >= startX && map.Merchant.X <= endX && map.Merchant.Y >= startY && map.Merchant.Y <= endY)
        {
            DrawStatic(sb, SpriteCache.GetTraderSprite(), map.Merchant.X, map.Merchant.Y, startX, startY, endX, endY, Color.White);
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
            DrawStatic(sb, SpriteCache.GetBoardSprite(), map.Board.X, map.Board.Y, startX, startY, endX, endY, Color.White);
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
            DrawStatic(sb, SpriteCache.GetTraderSprite(), npc.X, npc.Y, startX, startY, endX, endY, Color.LightBlue);
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
            DrawStatic(sb, SpriteCache.GetCollectibleSprite(), cl.X, cl.Y, startX, startY, endX, endY, Color.White);
        foreach (var cp in map.Corpses ?? Enumerable.Empty<CorpsePosition>())
            DrawStatic(sb, SpriteCache.GetCorpseSprite(), cp.X, cp.Y, startX, startY, endX, endY, new Color(200, 200, 200));

        // Сундук склада (кадр 0 = закрыт)
        if (map.StorageChest != null && map.StorageChest.X >= startX && map.StorageChest.X <= endX
            && map.StorageChest.Y >= startY && map.StorageChest.Y <= endY)
        {
            var chestSheet = SpriteCache.Get("chest_ss");
            if (chestSheet != null)
            {
                var srcRect = new Rectangle(0, 0, 64, 64);
                float px = _gridOX + (map.StorageChest.X - startX) * _cellW;
                float py = _gridOY + (map.StorageChest.Y - startY) * _cellH;
                sb.Draw(chestSheet, new Rectangle((int)px - 2, (int)py - 2, (int)_cellW + 4, (int)_cellH + 4), srcRect, Color.White);
            }
            else
            {
                DrawStatic(sb, null, map.StorageChest.X, map.StorageChest.Y, startX, startY, endX, endY, new Color(180, 140, 60, 200));
            }
        }
    }

    private void DrawMonsterSprites(SpriteBatch sb, SpriteFont font, WorldMap map, int startX, int startY, int endX, int endY)
    {
        foreach (var m in map.Monsters)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"monster:{m.Id}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float px = _gridOX + (v.X - startX) * _cellW + 3;
            float py = _gridOY + (v.Y - startY) * _cellH;

            var sprite = SpriteCache.GetMonsterSprite(m.TemplateId);
            if (sprite != null)
                sb.Draw(sprite, EntityRect(px, py, _cellW, _cellH), Color.White);
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
    }

    private void DrawPlayerSprites(SpriteBatch sb, SpriteFont font, WorldMap map, int startX, int startY, int endX, int endY)
    {
        foreach (var p in map.Players)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"player:{p.Name}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float px = _gridOX + (v.X - startX) * _cellW + 3;
            float py = _gridOY + (v.Y - startY) * _cellH;

            bool isLocal = p.Name == _playerName;
            string facing = isLocal ? GetLocalFacing() : "down";
            if (isLocal && facing != _lastRenderFacing)
            {
                _lastRenderFacing = facing;
                try { OnFacingChanged?.Invoke(facing); } catch { }
            }
            string weaponSub = _weaponSubtype ?? "";
            string offWeaponSub = _offWeaponSubtype ?? "";
            string shieldSub = _shieldSubtype ?? "";
            bool isTwoHanded = _isTwoHanded;
            bool mainAttackActive = _mainAttackActive;
            bool offAttackActive = _offAttackActive;
            DateTime mainAttackStart = _mainAttackStart;
            DateTime offAttackStart = _offAttackStart;
            RemotePlayerState? deadRemote = null;

            if (!isLocal && _remotePlayers.TryGetValue(p.Name, out var rp))
            {
                facing = rp.Facing;
                weaponSub = rp.WeaponSubtype;
                offWeaponSub = rp.OffWeaponSubtype;
                shieldSub = rp.ShieldSubtype;
                isTwoHanded = rp.IsTwoHanded;
                mainAttackActive = rp.MainAttackActive;
                offAttackActive = rp.OffAttackActive;
                mainAttackStart = rp.MainAttackStart;
                offAttackStart = rp.OffAttackStart;
                if (rp.IsDead) deadRemote = rp;
            }

            SpriteAnimation? playerAnim = null;
            bool useAttackAnim = false;
            bool anyBodyAttack = mainAttackActive || offAttackActive;
            bool moving;
            if (isLocal)
                moving = _isMoving;
            else
            {
                (int X, int Y) tgt = ((int)v.X, (int)v.Y);
                lock (_stateLock) { _visTarget.TryGetValue($"player:{p.Name}", out tgt); }
                moving = Math.Abs(tgt.X - v.X) > 0.05f || Math.Abs(tgt.Y - v.Y) > 0.05f;
            }

            DateTime? deathAnimStart = null;

            if (isLocal && _isDead)
            {
                playerAnim = SpriteCache.GetPlayerDeathAnimation(facing);
                deathAnimStart = _deathAnimStart;
            }
            else if (deadRemote != null)
                continue;
            else if (anyBodyAttack)
            {
                if (mainAttackActive)
                {
                    playerAnim = weaponSub == "bow"
                        ? SpriteCache.GetPlayerRangeAttackAnimation(facing)
                        : isTwoHanded
                            ? SpriteCache.GetPlayerTwoHandAttackAnimation(facing)
                            : SpriteCache.GetPlayerAttackAnimation(facing);
                }
                else
                    playerAnim = SpriteCache.GetPlayerSecondAttackAnimation(facing);
                if (playerAnim != null) useAttackAnim = true;
                else playerAnim = SpriteCache.GetPlayerAnimation(facing);
            }
            else
                playerAnim = moving
                    ? SpriteCache.GetPlayerAnimation(facing)
                    : SpriteCache.GetAnimation($"player_idle_{facing}") ?? SpriteCache.GetPlayerAnimation(facing);

            var er = EntityRect(px, py, _cellW, _cellH);
            if (playerAnim != null)
            {
                int frame = ComputeAnimFrame(playerAnim, isLocal, deathAnimStart, useAttackAnim, mainAttackActive, offAttackStart, mainAttackStart, p.Name);
                var src = playerAnim.GetSourceRect(frame);
                sb.Draw(playerAnim.Sheet, er, src, Color.White);
                DrawWeaponOverlay(sb, er, weaponSub, facing, moving, mainAttackActive, offAttackActive, mainAttackStart, offAttackStart, isLocal, isTwoHanded);
                DrawShieldOverlay(sb, er, shieldSub, facing, moving, mainAttackActive, mainAttackStart, isTwoHanded);
                DrawOffWeaponOverlay(sb, er, offWeaponSub, facing, moving, offAttackActive, mainAttackActive, mainAttackStart, offAttackStart, weaponSub, isTwoHanded);
            }
            else
            {
                var playerSprite = SpriteCache.GetPlayerSprite(facing) ?? SpriteCache.GetPlayerSprite("down");
                if (playerSprite != null)
                {
                    sb.Draw(playerSprite, er, Color.White);
                    DrawStaticWeaponOverlay(sb, er, weaponSub, facing, moving, isTwoHanded);
                    DrawStaticShieldOverlay(sb, er, shieldSub, facing, moving, isTwoHanded);
                    DrawStaticOffWeaponOverlay(sb, er, offWeaponSub, facing, moving, isTwoHanded);
                }
                else
                {
                    Color fbColor = isLocal ? Color.Goldenrod : Color.LightGray;
                    sb.DrawString(font, "P", new Vector2(px, py), fbColor);
                }
            }
        }
    }

    private int ComputeAnimFrame(SpriteAnimation anim, bool isLocal, DateTime? deathAnimStart, bool useAttackAnim, bool mainAttackActive, DateTime offAttackStart, DateTime mainAttackStart, string playerName)
    {
        if (deathAnimStart.HasValue)
        {
            float elapsed = (float)(DateTime.UtcNow - deathAnimStart.Value).TotalSeconds;
            int frame = Math.Min((int)(elapsed / anim.FrameDuration), anim.FrameCount - 1);
            if (isLocal) _deathFrame = frame;
            return frame;
        }
        if (useAttackAnim)
        {
            DateTime animStart = mainAttackActive ? mainAttackStart : offAttackStart;
            float elapsed = (float)(DateTime.UtcNow - animStart).TotalSeconds;
            float totalAnimDur = anim.FrameDuration * anim.FrameCount;
            int atkFrame = Math.Min((int)(elapsed / anim.FrameDuration), anim.FrameCount - 1);
            if (elapsed >= totalAnimDur)
            {
                if (isLocal)
                {
                    if (_mainAttackActive) _mainAttackActive = false;
                    if (_offAttackActive) _offAttackActive = false;
                }
                else if (_remotePlayers.TryGetValue(playerName, out var rpClear))
                {
                    rpClear.MainAttackActive = false;
                    rpClear.OffAttackActive = false;
                }
            }
            return atkFrame;
        }
        return (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / anim.FrameDuration) % anim.FrameCount;
    }

    private void DrawWeaponOverlay(SpriteBatch sb, Rectangle er, string weaponSub, string facing, bool moving, bool mainAttackActive, bool offAttackActive, DateTime mainAttackStart, DateTime offAttackStart, bool isLocal, bool isTwoHanded)
    {
        if (string.IsNullOrEmpty(weaponSub)) return;
        SpriteAnimation? weaponAnim = null;
        bool useWeaponSwing = false;
        if (mainAttackActive)
        {
            weaponAnim = weaponSub == "bow"
                ? SpriteCache.GetWeaponRangeAttackAnimation(weaponSub, facing)
                : SpriteCache.GetWeaponAttackAnimation(weaponSub, facing);
            if (weaponAnim != null) useWeaponSwing = true;
            else weaponAnim = SpriteCache.GetWeaponAnimation(weaponSub, facing, moving);
        }
        else if (offAttackActive)
        {
            weaponAnim = SpriteCache.GetOffHandWeaponSecondAttackAnimation(weaponSub, facing);
            if (weaponAnim != null) useWeaponSwing = true;
            if (weaponAnim == null) weaponAnim = SpriteCache.GetOffHandWeaponAttackAnimation(weaponSub, facing);
            if (weaponAnim != null) useWeaponSwing = true;
            if (weaponAnim == null) weaponAnim = SpriteCache.GetWeaponAnimation(weaponSub, facing, moving);
        }
        else
            weaponAnim = SpriteCache.GetWeaponAnimation(weaponSub, facing, moving);
        if (isLocal && _weaponLogOnce) { Logger.Debug($"WeaponOverlay: subtype={weaponSub} facing={facing} anim={(weaponAnim != null ? "OK" : "NULL")}"); _weaponLogOnce = false; }
        if (weaponAnim == null) return;
        int wFrame;
        if (useWeaponSwing)
        {
            DateTime swingStart = mainAttackActive ? mainAttackStart : offAttackStart;
            float elapsed = (float)(DateTime.UtcNow - swingStart).TotalSeconds;
            wFrame = Math.Min((int)(elapsed / weaponAnim.FrameDuration), weaponAnim.FrameCount - 1);
        }
        else
            wFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / weaponAnim.FrameDuration) % weaponAnim.FrameCount;
        sb.Draw(weaponAnim.Sheet, er, weaponAnim.GetSourceRect(wFrame), Color.White);
    }

    private void DrawShieldOverlay(SpriteBatch sb, Rectangle er, string shieldSub, string facing, bool moving, bool mainAttackActive, DateTime mainAttackStart, bool isTwoHanded)
    {
        if (isTwoHanded || string.IsNullOrEmpty(shieldSub)) return;
        SpriteAnimation? shieldAnim;
        bool useShieldAttack = false;
        if (mainAttackActive)
        {
            shieldAnim = SpriteCache.GetShieldAttackAnimation(facing);
            if (shieldAnim != null) useShieldAttack = true;
            else shieldAnim = SpriteCache.GetShieldAnimation(facing, moving);
        }
        else
            shieldAnim = SpriteCache.GetShieldAnimation(facing, moving);
        if (shieldAnim == null) return;
        int sFrame;
        if (useShieldAttack)
        {
            float elapsed = (float)(DateTime.UtcNow - mainAttackStart).TotalSeconds;
            sFrame = Math.Min((int)(elapsed / shieldAnim.FrameDuration), shieldAnim.FrameCount - 1);
        }
        else
            sFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / shieldAnim.FrameDuration) % shieldAnim.FrameCount;
        sb.Draw(shieldAnim.Sheet, er, shieldAnim.GetSourceRect(sFrame), Color.White);
    }

    private void DrawOffWeaponOverlay(SpriteBatch sb, Rectangle er, string offWeaponSub, string facing, bool moving, bool offAttackActive, bool mainAttackActive, DateTime mainAttackStart, DateTime offAttackStart, string weaponSub, bool isTwoHanded)
    {
        if (isTwoHanded || string.IsNullOrEmpty(offWeaponSub)) return;
        SpriteAnimation? offAnim = null;
        bool useOffSwing = false;
        if (offAttackActive)
        {
            bool noMainWeapon = string.IsNullOrEmpty(weaponSub);
            if (noMainWeapon)
            {
                offAnim = SpriteCache.GetWeaponSecondAttackAnimation(offWeaponSub, facing);
                if (offAnim != null) useOffSwing = true;
                if (offAnim == null) offAnim = SpriteCache.GetOffHandWeaponAttackAnimation(offWeaponSub, facing);
                if (offAnim != null) useOffSwing = true;
                if (offAnim == null) offAnim = SpriteCache.GetOffHandWeaponAnimation(offWeaponSub, facing, moving);
            }
            else
            {
                offAnim = SpriteCache.GetWeaponSecondAttackAnimation(offWeaponSub, facing);
                if (offAnim != null) useOffSwing = true;
                if (offAnim == null) offAnim = SpriteCache.GetOffHandWeaponAttackAnimation(offWeaponSub, facing);
                if (offAnim != null) useOffSwing = true;
                if (offAnim == null) offAnim = SpriteCache.GetOffHandWeaponAnimation(offWeaponSub, facing, moving);
            }
        }
        else if (mainAttackActive)
        {
            bool mainIsRanged = weaponSub == "bow" || weaponSub == "staff";
            if (mainIsRanged)
                offAnim = SpriteCache.GetWeaponAttackAnimation(offWeaponSub, facing);
            else
            {
                offAnim = SpriteCache.GetOffHandWeaponAttackAnimation(offWeaponSub, facing);
                if (offAnim != null) useOffSwing = true;
                if (offAnim == null) offAnim = SpriteCache.GetOffHandWeaponAnimation(offWeaponSub, facing, moving);
            }
        }
        else
            offAnim = SpriteCache.GetOffHandWeaponAnimation(offWeaponSub, facing, moving);
        if (offAnim == null) return;
        int offFrame;
        if (useOffSwing)
        {
            DateTime swingStart = offAttackActive ? offAttackStart : mainAttackStart;
            float elapsed = (float)(DateTime.UtcNow - swingStart).TotalSeconds;
            offFrame = Math.Min((int)(elapsed / offAnim.FrameDuration), offAnim.FrameCount - 1);
        }
        else
            offFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / offAnim.FrameDuration) % offAnim.FrameCount;
        sb.Draw(offAnim.Sheet, er, offAnim.GetSourceRect(offFrame), Color.White);
    }

    private void DrawStaticWeaponOverlay(SpriteBatch sb, Rectangle er, string weaponSub, string facing, bool moving, bool isTwoHanded)
    {
        if (string.IsNullOrEmpty(weaponSub)) return;
        SpriteAnimation? weaponAnim = SpriteCache.GetWeaponAnimation(weaponSub, facing, moving);
        if (weaponAnim != null)
        {
            int wFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / weaponAnim.FrameDuration) % weaponAnim.FrameCount;
            sb.Draw(weaponAnim.Sheet, er, weaponAnim.GetSourceRect(wFrame), Color.White);
        }
    }

    private void DrawStaticShieldOverlay(SpriteBatch sb, Rectangle er, string shieldSub, string facing, bool moving, bool isTwoHanded)
    {
        if (isTwoHanded || string.IsNullOrEmpty(shieldSub)) return;
        SpriteAnimation? shieldAnim = SpriteCache.GetShieldAnimation(facing, moving);
        if (shieldAnim != null)
        {
            int sFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / shieldAnim.FrameDuration) % shieldAnim.FrameCount;
            sb.Draw(shieldAnim.Sheet, er, shieldAnim.GetSourceRect(sFrame), Color.White);
        }
    }

    private void DrawStaticOffWeaponOverlay(SpriteBatch sb, Rectangle er, string offWeaponSub, string facing, bool moving, bool isTwoHanded)
    {
        if (isTwoHanded || string.IsNullOrEmpty(offWeaponSub)) return;
        SpriteAnimation? offAnim = SpriteCache.GetOffHandWeaponAnimation(offWeaponSub, facing, moving);
        if (offAnim != null)
        {
            int offFrame = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds / offAnim.FrameDuration) % offAnim.FrameCount;
            sb.Draw(offAnim.Sheet, er, offAnim.GetSourceRect(offFrame), Color.White);
        }
    }

    private void DrawLabelsAndHP(SpriteBatch sb, SpriteFont fontSmall, WorldMap map, int startX, int startY, int endX, int endY)
    {
        foreach (var m in map.Monsters)
        {
            (float X, float Y) v; lock (_stateLock) { if (!_visPos.TryGetValue($"monster:{m.Id}", out v)) continue; }
            int wx = (int)Math.Round(v.X), wy = (int)Math.Round(v.Y);
            if (wx < startX || wx > endX || wy < startY || wy > endY) continue;
            float py = _gridOY + (v.Y - startY) * _cellH;
            float cx = _gridOX + (v.X - startX) * _cellW + _cellW / 2;

            string mname = $"{m.Name} [{m.Level}]";
            var mnameSize = fontSmall.MeasureString(mname);
            float mny = py - 26;
            sb.DrawString(fontSmall, mname, new Vector2(cx - mnameSize.X / 2 + 1, mny + 1), Color.Black);
            sb.DrawString(fontSmall, mname, new Vector2(cx - mnameSize.X / 2, mny), Color.White);

            if (m.MaxHealth > 0)
            {
                float barW = 34, barH = 3;
                float barX = cx - barW / 2, barY = py - 8;
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
            float cx = _gridOX + (v.X - startX) * _cellW + _cellW / 2;

            Color groupColor = new Color(110, 230, 130);
            Color pvpEnemyColor = new Color(220, 60, 60);
            Color nickColor = p.Name == _playerName
                ? Color.Goldenrod
                : (_partyMemberNames.Contains(p.Name) ? groupColor
                    : (map.PvPEnabled ? pvpEnemyColor : Color.LightGray));

            string nick = $"{p.Name} [{p.Level}]";
            var nickSize = fontSmall.MeasureString(nick);
            float ny = py - 26;
            sb.DrawString(fontSmall, nick, new Vector2(cx - nickSize.X / 2 + 1, ny + 1), Color.Black);
            sb.DrawString(fontSmall, nick, new Vector2(cx - nickSize.X / 2, ny), nickColor);

            if (p.MaxHealth > 0)
            {
                float barW = 34, barH = 3;
                float barX = cx - barW / 2, barY = py - 8;
                float hpPct = Math.Clamp((float)p.Health / p.MaxHealth, 0f, 1f);
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)barX, (int)barY, (int)barW, (int)barH), new Color(40, 10, 10));
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)barX, (int)barY, (int)(barW * hpPct), (int)barH), new Color(180, 40, 40));
            }
        }
    }

    private void DrawFloatingTexts(SpriteBatch sb, SpriteFont font, int startX, int startY)
    {
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
                var outline = new Color((byte)0, (byte)0, (byte)0, (byte)(alpha * 0.9f));
                float o = 1.2f * scale;
                sb.DrawString(font, ft.Text, new Vector2(fpx - o, fpy), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx + o, fpy), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy - o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy + o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx - o, fpy - o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx + o, fpy - o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx - o, fpy + o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx + o, fpy + o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy), c, 0f, origin, scale, SpriteEffects.None, 0f);
            }
        }
    }

    private void DrawHoverTile(SpriteBatch sb)
    {
        if (_hoverTileX < 0 || _hoverTileY < 0) return;
        if (_hoverTileX < _viewStartX || _hoverTileX > _viewEndX || _hoverTileY < _viewStartY || _hoverTileY > _viewEndY) return;
        float tx = _gridOX + (_hoverTileX - _viewStartX) * _cellW;
        float ty = _gridOY + (_hoverTileY - _viewStartY) * _cellH;
        (Color fill, Color border) = _hoverCursorType switch
        {
            "attack" => (new Color(60, 18, 18, 8), new Color(160, 50, 50, 40)),
            "player" => (new Color(18, 55, 25, 8), new Color(50, 200, 65, 40)),
            "talk" or "harvest" => (new Color(18, 55, 25, 8), new Color(50, 140, 65, 40)),
            "portal" => (new Color(25, 40, 70, 8), new Color(65, 100, 170, 40)),
            "loot" => (new Color(43, 43, 43, 8), new Color(110, 110, 110, 40)),
            _ => (new Color(65, 60, 25, 8), new Color(170, 155, 60, 40))
        };
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)tx, (int)ty, (int)_cellW, (int)_cellH), fill);
        DrawRect(sb, tx + 1, ty + 1, _cellW - 2, _cellH - 2, border, 1);
    }

    private void DrawSelectionHighlight(SpriteBatch sb, WorldMap map, int startX, int startY, int endX, int endY)
    {
        if (_selectedEntityType == null) return;
        int hx = _selectedEntityX, hy = _selectedEntityY;
        string? hkey = null;
        if (_selectedEntityType == "monster" && _selectedEntityId != null) hkey = $"monster:{_selectedEntityId}";
        else if (_selectedEntityType == "player" && _selectedEntityName != null) hkey = $"player:{_selectedEntityName}";
        if (_selectedEntityType == "merchant" && map.Merchant != null) { hx = map.Merchant.X; hy = map.Merchant.Y; }
        else if (_selectedEntityType == "board" && map.Board != null) { hx = map.Board.X; hy = map.Board.Y; }
        else if (_selectedEntityType == "storage_chest" && map.StorageChest != null) { hx = map.StorageChest.X; hy = map.StorageChest.Y; }
        if (hkey != null) { lock (_stateLock) { if (_visPos.TryGetValue(hkey, out var hv)) { hx = (int)Math.Round(hv.X); hy = (int)Math.Round(hv.Y); } } }
        if (hx >= startX && hx <= endX && hy >= startY && hy <= endY)
        {
            float tx = _gridOX + (hx - startX) * _cellW;
            float ty = _gridOY + (hy - startY) * _cellH;
            Color hc = _selectedEntityType switch
            {
                "monster" => Color.Red,
                "player" when _currentMap?.PvPEnabled == true => Color.Red,
                "move" => new Color(220, 200, 80),
                "corpse" => Color.Gray,
                _ => Color.LimeGreen
            };
            DrawRect(sb, tx + 1, ty + 1, _cellW - 2, _cellH - 2, hc, 2);
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
                else if (key.StartsWith("player:"))
                {
                    string pname = key.Substring(7);
                    _remoteMoving[pname] = dist > 0.05f;
                }

                // Монстры двигаются каждый со своей скоростью + свой случайный сдвиг фазы,
                // чтобы группы не шагали синхронно.
                float stepHere = step;
                if (key.StartsWith("monster:"))
                {
                    int moveMs = _visMoveMs.TryGetValue(key, out int mm) && mm > 0 ? mm : 500;
                    int h = StringComparer.Ordinal.GetHashCode(key) & 0x7fffffff;
                    moveMs = (int)(moveMs * (0.55 + 0.9 * (h % 100) / 100.0));
                    stepHere = Math.Max(0.0001f, 1000f / Math.Max(60, moveMs) * dt);
                }

                if (dist <= stepHere || dist < 0.001f) _visPos[key] = (tgt.X, tgt.Y);
                else { float inv = stepHere / dist; _visPos[key] = (v.X + dx * inv, v.Y + dy * inv); }
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
                float angle = (float)Math.Atan2(dy, dx) - MathHelper.PiOver2;
                var tex = SpriteCache.Get("projectile_arrow") ?? SpriteCache.Pixel;
                float scale = Math.Max(cellW, cellH) * 0.6f / 64f;
                int size = (int)(64 * scale);
                sb.Draw(tex, new Vector2(px, py), null, Color.White, angle,
                    new Vector2(32, 32), scale, SpriteEffects.None, 0f);
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
