using RPGGame.Shared.Models;

namespace RPGGame.Server;

public class LootManager
{
    private readonly GameWorld _world;
    private List<LootEntry> _lootTable = new();

    public LootManager(GameWorld world)
    {
        _world = world;
    }

    public void LoadFromDatabase()
    {
        _lootTable = DatabaseManager.LoadLootTable();
        Log.Info($"Loot table loaded: {_lootTable.Count} entries");
    }

    public List<Item> RollLoot(string monsterTemplateId)
    {
        var items = new List<Item>();
        var trophies = _lootTable.Where(t => t.MonsterId == monsterTemplateId).ToList();
        if (trophies.Count == 0) return items;

        foreach (var trophy in trophies)
        {
            int roll = _world.NextRandom(0, 100);
            if (roll < trophy.DropChance)
            {
                items.Add(new Item
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = trophy.Name,
                    Type = "trophy",
                    Value = trophy.Value,
                    Description = trophy.Description,
                    MaxStack = Balance.MaxStackForType("trophy")
                });
            }
        }

        return items;
    }
}
