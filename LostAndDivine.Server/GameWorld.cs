using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.Server.Network;

namespace LostAndDivine.Server;

/// <summary>
/// Лёгкая модель игрового мира: размеры, препятствия и стартовая точка мерчанта.
/// Координаты игроков/монстров плоские (X/Y), но привязаны к границам этого мира.
/// </summary>
public sealed class GameMap
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Координаты мерчанта (точка взаимодействия).</summary>
    public int MerchantX { get; set; }
    public int MerchantY { get; set; }

    /// <summary>Координаты доски заданий (NPC).</summary>
    public int BoardX { get; set; } = Balance.DefaultBoardX;
    public int BoardY { get; set; } = Balance.DefaultBoardY;

    /// <summary>Точки, недоступные для прохода (препятствия/здания).</summary>
    private readonly HashSet<(int X, int Y)> _obstacles = new();

    /// <summary>Тайл-карта: плоский массив tileType по [y * Width + x]. null — карта не загружена.</summary>
    private byte[]? _tiles;

    /// <summary>Слой объектов (деревья и т.п.), рисуется поверх сущностей. null — слоя нет.</summary>
    private byte[]? _objectTiles;

    public GameMap(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void SetTiles(byte[] tiles)
    {
        if (tiles.Length != Width * Height)
            throw new ArgumentException($"Tile array length {tiles.Length} != {Width * Height}");
        _tiles = tiles;
    }

    public byte[]? GetTiles() => _tiles;

    public byte GetTile(int x, int y)
    {
        if (_tiles == null || x < 0 || y < 0 || x >= Width || y >= Height) return (byte)255;
        return _tiles[y * Width + x];
    }

    public void SetTile(int x, int y, byte tileType)
    {
        if (_tiles == null || x < 0 || y < 0 || x >= Width || y >= Height) return;
        _tiles[y * Width + x] = tileType;
    }

    public void SetObjectTiles(byte[]? tiles)
    {
        if (tiles != null && tiles.Length != Width * Height)
            throw new ArgumentException($"Object tile array length {tiles.Length} != {Width * Height}");
        _objectTiles = tiles;
    }

    public byte[]? GetObjectTiles() => _objectTiles;

    public void AddObstacle(int x, int y) => _obstacles.Add((x, y));

    public bool IsObstacle(int x, int y) => _obstacles.Contains((x, y));

    /// <summary>
    /// Плоский массив Width*Height: 1 — клетка непроходима, 0 — свободна.
    /// Отправляется клиенту для отрисовки курсора «нельзя идти».
    /// </summary>
    public byte[] GetObstacleData()
    {
        var data = new byte[Width * Height];
        foreach (var (x, y) in _obstacles)
        {
            if (x >= 0 && y >= 0 && x < Width && y < Height)
                data[y * Width + x] = 1;
        }
        return data;
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public GameMap Clone()
    {
        var clone = new GameMap(Width, Height);
        if (_tiles != null) clone.SetTiles((byte[])_tiles.Clone());
        if (_objectTiles != null) clone.SetObjectTiles((byte[])_objectTiles.Clone());
        foreach (var (x, y) in _obstacles)
            clone.AddObstacle(x, y);
        return clone;
    }

    /// <summary>Радиус видимости сущностей вокруг игрока.</summary>
    public int ViewRadius { get; set; } = Balance.ViewRadius;
}

/// <summary>
/// Инстансный контейнер состояния сервера: игроки, клиенты, монстры, NPC, карта.
/// Все мутации списков защищены локами мира.
/// </summary>
public sealed class GameWorld
{
    public GameMap Map { get; }
    private INetworkHub? _hub;
    private Func<Player, bool>? _savePlayer;

    private readonly List<Player> _players = new();
    private readonly List<ClientConnection> _clients = new();
    private readonly object _lock = new();

    // --- Перезвон при обрыве соединения: игрок остаётся в мире до конца окна ---
    private static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(20);
    private readonly List<(Player Player, DateTime ExpiresUtc)> _pendingReconnects = new();
    private readonly object _pendingLock = new();

    // --- Монстры (отдельный лок, чтобы блуждание не блокировало игроков) ---
    private readonly List<Monster> _monsters = new();
    private readonly Dictionary<Guid, Monster> _monsterById = new();
    private readonly Dictionary<(int X, int Y), List<Monster>> _monsterByPos = new();
    private readonly object _monsterLock = new();
    private readonly List<(Monster Monster, Player Player, int Damage)> _pendingMonsterAttacks = new();
    private readonly object _monsterAttackLock = new();
    private List<MonsterTemplate> _monsterTemplates = new();

    private readonly List<GroundHazard> _hazards = new();
    private readonly object _hazardLock = new();

    public GameWorld(int width = 100, int height = 100)
    {
        Map = new GameMap(width, height);
    }

    /// <summary>
    /// Устанавливает зависимости, которые невозможно инжектировать через конструктор
    /// из-за циклических зависимостей (INetworkHub, сохранение игроков).
    /// </summary>
    public void SetDependencies(INetworkHub hub, Func<Player, bool> savePlayer)
    {
        _hub = hub;
        _savePlayer = savePlayer;
    }

    // --- Игроки ---
    public void AddPlayer(Player player)
    {
        lock (_lock)
        {
            _players.RemoveAll(p => p.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase));
            _players.Add(player);
        }
        // Друзьям игрока — обновить списки и сообщить о входе
        NotifyFriendsAsync(player.Name, online: true);
    }

    public void RemovePlayer(Player player)
    {
        lock (_lock) _players.Remove(player);
        // Друзьям игрока — обновить списки и сообщить о выходе
        NotifyFriendsAsync(player.Name, online: false);
    }

    private void NotifyFriendsAsync(string playerName, bool online)
    {
        if (_hub == null) return;
        try
        {
            var friendOwners = DatabaseManager.GetReverseFriendNames(playerName);
            foreach (var owner in friendOwners)
            {
                try
                {
                    if (!this.TryGetPlayerByName(owner, out var ownerPlayer) || ownerPlayer == null) continue;
                    var conn = FindClientByPlayer(ownerPlayer);
                    if (conn == null) continue;

                    _ = _hub.SendFriendListToAsync(conn, ownerPlayer);

                    string text = online
                        ? $"Друг {playerName} зашёл(а) в игру"
                        : $"Друг {playerName} вышел(а) из игры";
                    _ = _hub.SendChatToAsync(conn, ChatChannel.System, "Друзья", text);
                }
                catch
                {
                    // Не падаем из-за ошибки уведомления одного игрока
                }
            }
        }
        catch
        {
            // Не падаем сервер из-за уведомления друзей
        }
    }

    public List<Player> GetPlayersSnapshot()
    {
        lock (_lock) return new List<Player>(_players);
    }

    public bool TryGetPlayer(Guid id, out Player? player)
    {
        lock (_lock)
        {
            player = _players.FirstOrDefault(p => p.Id == id);
            return player != null;
        }
    }

    public bool TryGetPlayerByName(string name, out Player? player)
    {
        lock (_lock)
        {
            player = _players.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return player != null;
        }
    }

    // --- Клиенты ---
    public void AddClient(ClientConnection connection)
    {
        lock (_lock) _clients.Add(connection);
    }

    public void RemoveClient(ClientConnection connection)
    {
        if (connection.Player != null)
        {
            try { _savePlayer?.Invoke(connection.Player); }
            catch (Exception ex) { Log.Error($"[World] Save on disconnect failed for {connection.Player.Name}", ex); }
        }
        lock (_lock) _clients.Remove(connection);
    }

    public List<ClientConnection> GetClientsSnapshot()
    {
        lock (_lock) return new List<ClientConnection>(_clients);
    }

    public List<ClientConnection> GetAllConnectionsSnapshot()
    {
        lock (_lock) return new List<ClientConnection>(_clients);
    }

    public ClientConnection? GetConnectionByPlayerName(string playerName)
    {
        lock (_lock) return _clients.FirstOrDefault(c => c.Player?.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase) == true);
    }

    public ClientConnection? FindClientByPlayer(Player player)
    {
        lock (_lock) return _clients.FirstOrDefault(c => c.Player == player);
    }

    public void DisconnectPlayer(ClientConnection connection)
    {
        RemoveClient(connection);
        try { connection.Client.Close(); } catch { /* already closing */ }
    }

    // --- Отложенное удаление игрока при обрыве (окно переподключения) ---
    /// <summary>
    /// Помечает игрока как ожидающего переподключения: он остаётся в мире
    /// (позиция, партия, инстанс) до конца grace-периода, чтобы успешный
    /// реконнект прошёл без потери состояния.
    /// </summary>
    public void MarkPendingReconnect(Player player)
    {
        lock (_pendingLock)
        {
            _pendingReconnects.RemoveAll(p => ReferenceEquals(p.Player, player));
            _pendingReconnects.Add((player, DateTime.UtcNow + ReconnectGrace));
        }
    }

    /// <summary>
    /// Снимает метку pending-реконнекта. Возвращает true, если игрок всё ещё
    /// ожидал переподключения (значит, финализация дисконнекта — наша задача).
    /// </summary>
    public bool CancelPendingReconnect(Player player)
    {
        lock (_pendingLock) return _pendingReconnects.RemoveAll(p => ReferenceEquals(p.Player, player)) > 0;
    }

    public bool IsPlayerPendingReconnect(Player player)
    {
        lock (_pendingLock) return _pendingReconnects.Any(p => ReferenceEquals(p.Player, player));
    }

    /// <summary>
    /// Возвращает игроков, чьё окно переподключения истекло, и снимает их метки.
    /// </summary>
    public List<Player> TakeExpiredPendingReconnects()
    {
        var now = DateTime.UtcNow;
        lock (_pendingLock)
        {
            var expired = _pendingReconnects
                .Where(p => p.ExpiresUtc <= now)
                .Select(p => p.Player)
                .ToList();
            _pendingReconnects.RemoveAll(p => p.ExpiresUtc <= now);
            return expired;
        }
    }

    // --- Монстры ---
    public void SetMonsterTemplates(List<MonsterTemplate> templates)
    {
        lock (_monsterLock) _monsterTemplates = templates;
    }

    public List<MonsterTemplate> GetMonsterTemplates()
    {
        lock (_monsterLock) return new List<MonsterTemplate>(_monsterTemplates);
    }

    public void AddMonster(Monster monster)
    {
        lock (_monsterLock)
        {
            _monsters.Add(monster);
            _monsterById[monster.Id] = monster;
        }
    }

    public void RemoveMonster(Monster monster)
    {
        lock (_monsterLock)
        {
            _monsters.Remove(monster);
            _monsterById.Remove(monster.Id);
        }
    }

    public void ClearMonsters()
    {
        lock (_monsterLock)
        {
            _monsters.Clear();
            _monsterById.Clear();
        }
    }

    public List<Monster> GetMonstersSnapshot()
    {
        lock (_monsterLock) return new List<Monster>(_monsters);
    }

    public Monster? FindMonsterAt(int x, int y)
    {
        lock (_monsterLock) return _monsters.FirstOrDefault(m => m.X == x && m.Y == y);
    }

    public Monster? FindMonsterById(Guid id)
    {
        lock (_monsterLock) return _monsterById.GetValueOrDefault(id);
    }

    public int GetMonsterCount()
    {
        lock (_monsterLock) return _monsters.Count;
    }

    // --- Очередь атак монстров по игрокам ---
    public void QueueMonsterAttack(Monster monster, Player player, int damage)
    {
        lock (_monsterAttackLock) _pendingMonsterAttacks.Add((monster, player, damage));
    }

    public List<(Monster Monster, Player Player, int Damage)> DrainMonsterAttacks()
    {
        lock (_monsterAttackLock)
        {
            var result = new List<(Monster, Player, int)>(_pendingMonsterAttacks);
            _pendingMonsterAttacks.Clear();
            return result;
        }
    }

    // --- Случайности ---
    public int NextRandom(int min, int max) => Random.Shared.Next(min, max);

    // --- Собираемые объекты (коллекционы) ---
    private readonly List<Collectible> _collectibles = new();
    private readonly object _collectibleLock = new();

    public void ClearCollectibles()
    {
        lock (_collectibleLock) _collectibles.Clear();
    }

    public void ClearCollectiblesInZone(string zoneId)
    {
        lock (_collectibleLock) _collectibles.RemoveAll(c =>
            string.Equals(c.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase));
    }

    public void AddCollectible(Collectible collectible)
    {
        lock (_collectibleLock) _collectibles.Add(collectible);
    }

    public void RemoveCollectible(Collectible collectible)
    {
        lock (_collectibleLock) _collectibles.Remove(collectible);
    }

    public List<Collectible> GetCollectiblesSnapshot()
    {
        lock (_collectibleLock) return new List<Collectible>(_collectibles);
    }

    public Collectible? FindCollectibleAt(int x, int y)
    {
        lock (_collectibleLock) return _collectibles.FirstOrDefault(c => c.X == x && c.Y == y);
    }

    // --- Ловушки / зоны ──
    public void AddHazard(GroundHazard hazard)
    {
        lock (_hazardLock) _hazards.Add(hazard);
    }

    public List<GroundHazard> GetHazardsSnapshot()
    {
        lock (_hazardLock) return new List<GroundHazard>(_hazards);
    }

    public List<GroundHazard> GetHazardsAt(int x, int y, string zoneId)
    {
        lock (_hazardLock)
            return _hazards.Where(h => h.X == x && h.Y == y && h.ZoneId == zoneId && h.ExpiresAt > DateTime.UtcNow).ToList();
    }

    public void RemoveExpiredHazards()
    {
        var now = DateTime.UtcNow;
        lock (_hazardLock) _hazards.RemoveAll(h => h.ExpiresAt <= now);
    }

    public Player? FindPlayerAt(int x, int y)
    {
        lock (_lock) return _players.FirstOrDefault(p => p.X == x && p.Y == y);
    }
}
