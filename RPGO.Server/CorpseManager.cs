using RPGGame.Shared.Models;

namespace RPGGame.Server;

public class CorpseManager
{
    private readonly List<MonsterCorpse> _corpses = new();
    private readonly object _lock = new();
    private readonly TimeSpan CorpseLifetime = TimeSpan.FromMinutes(5);

    public void CreateCorpse(Monster monster, List<Item> loot, Dictionary<Guid, CorpsePlayerLoot>? playerLoot = null, Dictionary<Guid, int>? contributors = null)
    {
        lock (_lock)
        {
            _corpses.Add(new MonsterCorpse
            {
                Id = monster.Id,
                X = monster.X,
                Y = monster.Y,
                MonsterName = monster.Name,
                Symbol = monster.Symbol,
                Level = monster.Level,
                GoldReward = monster.GoldReward,
                Loot = loot,
                PlayerLoot = playerLoot ?? new(),
                Contributors = contributors ?? new(monster.DamageTracker),
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    public MonsterCorpse? FindCorpseAt(int x, int y)
    {
        lock (_lock)
        {
            return _corpses.FirstOrDefault(c => c.X == x && c.Y == y);
        }
    }

    public MonsterCorpse? FindCorpseById(Guid id)
    {
        lock (_lock)
        {
            return _corpses.FirstOrDefault(c => c.Id == id);
        }
    }

    public bool RemoveCorpse(Guid id)
    {
        lock (_lock)
        {
            return _corpses.RemoveAll(c => c.Id == id) > 0;
        }
    }

    public List<CorpsePosition> GetCorpsePositions()
    {
        lock (_lock)
        {
            return _corpses.Select(c => new CorpsePosition
            {
                Id = c.Id,
                X = c.X,
                Y = c.Y,
                MonsterName = c.MonsterName,
                Symbol = c.Symbol,
                Level = c.Level,
                ItemCount = c.PlayerLoot.Count > 0
                    ? c.PlayerLoot.Values.Sum(v => v.Items.Count)
                    : c.Loot.Count
            }).ToList();
        }
    }

    public void CleanupExpired()
    {
        lock (_lock)
        {
            _corpses.RemoveAll(c => (DateTime.UtcNow - c.CreatedAt) > CorpseLifetime);
        }
    }
}

public class MonsterCorpse
{
    public Guid Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string MonsterName { get; set; } = "";
    public char Symbol { get; set; }
    public int Level { get; set; }
    public DateTime CreatedAt { get; set; }

    // Общий лут (для обратной совместимости)
    public int GoldReward { get; set; }
    public List<Item> Loot { get; set; } = new();

    // Персональный лут по игрокам
    public Dictionary<Guid, CorpsePlayerLoot> PlayerLoot { get; set; } = new();

    // Кто наносил урон (playerId → урон)
    public Dictionary<Guid, int> Contributors { get; set; } = new();
}

public class CorpsePlayerLoot
{
    public string PlayerName { get; set; } = "";
    public int Gold { get; set; }
    public List<Item> Items { get; set; } = new();
    public int DamagePercent { get; set; }
}
