using RPGGame.Shared.Models;

namespace RPGGame.Server;

public static class LootManager
{
    private static GameWorld World => Program.World;
    private static List<LootEntry> _lootTable = new();

    public static void LoadFromDatabase()
    {
        _lootTable = DatabaseManager.LoadLootTable();
        Log.Info($"Loot table loaded: {_lootTable.Count} entries");
    }

    public static List<Item> RollLoot(string monsterTemplateId)
    {
        var items = new List<Item>();
        var trophies = _lootTable.Where(t => t.MonsterId == monsterTemplateId).ToList();
        if (trophies.Count == 0) return items;

        foreach (var trophy in trophies)
        {
            int roll = World.NextRandom(0, 100);
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
