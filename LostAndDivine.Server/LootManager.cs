using LostAndDivine.Server.Services;
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

            // Копия шаблона целиком (базовые статы, качество, иконка) +
            // случайные бонусы по roll_config, если он включён у шаблона.
            var item = ItemRoller.Roll(template, Random.Shared);
            item.Id = Guid.NewGuid().ToString();
            item.Quantity = 1;
            item.Stock = 1;
            item.IsBuyback = false;
            items.Add(item);
        }

        return items;
    }
}