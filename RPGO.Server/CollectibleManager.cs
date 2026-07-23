using RPGGame.Shared.Models;

namespace RPGGame.Server;

public class Collectible
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ItemName { get; set; } = "";
    public char Symbol { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// Тонкая обёртка над GameWorld для логики собираемых объектов.
/// Состояние (список коллекционов) хранится в GameWorld.
/// </summary>
public class CollectibleManager
{
    private readonly GameWorld _world;

    private readonly List<(string Name, string ItemName, char Symbol, int Count)> _templates = new()
    {
        ("Куст ягод",      "Ягоды",        '*', 15),
        ("Грибная поляна", "Грибы",        'g', 12),
        ("Травяной куст",  "Трава",        'h', 10),
        ("Пчелиный улей",  "Мёд",          'b', 6),
        ("Сундук с рудой", "Руда",         'c', 8),
    };

    // Сопоставление названия собираемого предмета с id из каталога items
    private readonly Dictionary<string, string> _itemIdByCollectibleName = new()
    {
        { "Ягоды", "I0015" },
        { "Грибы", "I0016" },
        { "Мёд", "I0017" },
        { "Трава", "I0018" },
        { "Руда", "I0019" },
    };

    public CollectibleManager(GameWorld world)
    {
        _world = world;
    }

    public void Initialize()
    {
        _world.ClearCollectibles();
        foreach (var template in _templates)
        {
            for (int i = 0; i < template.Count; i++)
                SpawnOne(template.Name, template.ItemName, template.Symbol);
        }
    }

    private void SpawnOne(string name, string itemName, char symbol)
    {
        int x, y;
        int attempts = 0;
        do
        {
            x = _world.NextRandom(0, _world.Map.Width);
            y = _world.NextRandom(0, _world.Map.Height);
            attempts++;
        } while (IsOccupied(x, y) && attempts < Balance.SpawnMaxAttempts);

        if (attempts >= Balance.SpawnMaxAttempts) return;

        _world.AddCollectible(new Collectible
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            ItemName = itemName,
            Symbol = symbol,
            X = x,
            Y = y
        });
    }

    public List<CollectiblePosition> GetPositions()
    {
        return _world.GetCollectiblesSnapshot().Select(c => new CollectiblePosition
        {
            Id = c.Id,
            X = c.X,
            Y = c.Y,
            Name = c.Name,
            ItemName = c.ItemName,
            Symbol = c.Symbol
        }).ToList();
    }

    public Item? TryCollect(int x, int y)
    {
        var collectible = _world.FindCollectibleAt(x, y);
        if (collectible == null) return null;

        string itemName = collectible.ItemName;
        string collectibleName = collectible.Name;
        string itemId = _itemIdByCollectibleName.TryGetValue(itemName, out var id) ? id : Guid.NewGuid().ToString();
        _world.RemoveCollectible(collectible);
        SpawnOne(collectibleName, itemName, collectible.Symbol);

        return new Item
        {
            Id = itemId,
            Name = itemName,
            Type = "collectible",
            Value = Balance.CollectibleValue,
            Description = collectibleName,
            MaxStack = Balance.MaxStackForType("collectible")
        };
    }

    public bool IsOccupied(int x, int y)
        => _world.GetCollectiblesSnapshot().Any(c => c.X == x && c.Y == y);
}
