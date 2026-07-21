using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server;

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

    public GameMap(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void AddObstacle(int x, int y) => _obstacles.Add((x, y));

    public bool IsObstacle(int x, int y) => _obstacles.Contains((x, y));

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

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

    private readonly List<Player> _players = new();
    private readonly List<ClientConnection> _clients = new();
    private readonly object _lock = new();
    private readonly Random _random = new();

    // --- Монстры (отдельный лок, чтобы блуждание не блокировало игроков) ---
    private readonly List<Monster> _monsters = new();
    private readonly object _monsterLock = new();
    private readonly List<(Monster Monster, Player Player, int Damage)> _pendingMonsterAttacks = new();
    private readonly object _monsterAttackLock = new();
    private List<DatabaseManager.MonsterTemplate> _monsterTemplates = new();

    public GameWorld(int width = 100, int height = 100)
    {
        Map = new GameMap(width, height);
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

    /// <summary>
    /// Рассылает обновлённый список друзей всем, у кого этот игрок в друзьях
    /// (чтобы онлайн-статус менялся в реальном времени, без перезахода в окно),
    /// и оповещает в системный чат о входе/выходе друга.
    /// </summary>
    private void NotifyFriendsAsync(string playerName, bool online)
    {
        try
        {
            var friendOwners = DatabaseManager.GetReverseFriendNames(playerName);
            foreach (var owner in friendOwners)
            {
                if (!this.TryGetPlayerByName(owner, out var ownerPlayer) || ownerPlayer == null) continue;
                var conn = FindClientByPlayer(ownerPlayer);
                if (conn == null) continue;

                _ = Program.Hub.SendFriendListToAsync(conn, ownerPlayer);

                string text = online
                    ? $"Друг {playerName} зашёл(а) в игру"
                    : $"Друг {playerName} вышел(а) из игры";
                _ = Program.Hub.SendChatToAsync(conn, ChatChannel.System, "Друзья", text);
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
            try { DatabaseManager.SavePlayerProgress(connection.Player); }
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
        lock (_lock) return _clients.FirstOrDefault(c => c.Player?.Name == playerName);
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

    // --- Монстры ---
    public void SetMonsterTemplates(List<DatabaseManager.MonsterTemplate> templates)
    {
        lock (_monsterLock) _monsterTemplates = templates;
    }

    public List<DatabaseManager.MonsterTemplate> GetMonsterTemplates()
    {
        lock (_monsterLock) return new List<DatabaseManager.MonsterTemplate>(_monsterTemplates);
    }

    public void AddMonster(Monster monster)
    {
        lock (_monsterLock) _monsters.Add(monster);
    }

    public void RemoveMonster(Monster monster)
    {
        lock (_monsterLock) _monsters.Remove(monster);
    }

    public void ClearMonsters()
    {
        lock (_monsterLock) _monsters.Clear();
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
        lock (_monsterLock) return _monsters.FirstOrDefault(m => m.Id == id);
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
    public int NextRandom(int min, int max) => _random.Next(min, max);

    // --- Собираемые объекты (коллекционы) ---
    private readonly List<Collectible> _collectibles = new();
    private readonly object _collectibleLock = new();

    public void ClearCollectibles()
    {
        lock (_collectibleLock) _collectibles.Clear();
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

    public Player? FindPlayerAt(int x, int y)
    {
        lock (_lock) return _players.FirstOrDefault(p => p.X == x && p.Y == y);
    }
}
