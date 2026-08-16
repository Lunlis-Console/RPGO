using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server;

public class LootManager
{
    private readonly GameWorld _world;
    private List<MonsterDrop> _drops = new();

    public LootManager(GameWorld world)
    {
        _world = world;
    }

    public void LoadFromDatabase()
    {
        _drops = DatabaseManager.LoadMonsterDrops();
        Log.Info($"Monster drops loaded: {_drops.Count} entries");
    }

    public List<Item> RollLoot(string monsterTemplateId)
    {
        var items = new List<Item>();
        var drops = _drops.Where(d => d.MonsterId == monsterTemplateId).ToList();
        if (drops.Count == 0) return items;

        foreach (var drop in drops)
        {
            int roll = _world.NextRandom(0, 100);
            if (roll >= drop.DropChance) continue;

            var template = DatabaseManager.GetItemTemplate(drop.ItemId);
            if (template == null) continue;

            items.Add(new Item
            {
                Id = Guid.NewGuid().ToString(),
                TemplateId = drop.ItemId,
                Name = template.Name,
                Type = template.Type,
                Value = template.Value,
                Description = template.Description,
                QuestItem = template.QuestItem,
                MaxStack = template.MaxStack,
            });
        }

        return items;
    }
}