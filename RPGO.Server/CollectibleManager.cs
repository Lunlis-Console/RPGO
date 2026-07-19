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
public static class CollectibleManager
{
    private static GameWorld World => Program.World;

    private static readonly List<(string Name, string ItemName, char Symbol, int Count)> _templates = new()
    {
        ("Куст ягод",      "Ягоды",        '*', 15),
        ("Грибная поляна", "Грибы",        'g', 12),
        ("Травяной куст",  "Трава",        'h', 10),
        ("Пчелиный улей",  "Мёд",          'b', 6),
        ("Сундук с рудой", "Руда",         'c', 8),
    };

    // Сопоставление названия собираемого предмета с id из каталога items
    private static readonly Dictionary<string, string> _itemIdByCollectibleName = new()
    {
        { "Ягоды", "I0015" },
        { "Грибы", "I0016" },
        { "Мёд", "I0017" },
        { "Трава", "I0018" },
        { "Руда", "I0019" },
    };

    public static void Initialize()
    {
        World.ClearCollectibles();
        foreach (var template in _templates)
        {
            for (int i = 0; i < template.Count; i++)
                SpawnOne(template.Name, template.ItemName, template.Symbol);
        }
    }

    private static void SpawnOne(string name, string itemName, char symbol)
    {
        int x, y;
        int attempts = 0;
        do
        {
            x = World.NextRandom(0, World.Map.Width);
            y = World.NextRandom(0, World.Map.Height);
            attempts++;
        } while (IsOccupied(x, y) && attempts < Balance.SpawnMaxAttempts);

        if (attempts >= Balance.SpawnMaxAttempts) return;

        World.AddCollectible(new Collectible
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            ItemName = itemName,
            Symbol = symbol,
            X = x,
            Y = y
        });
    }

    public static List<CollectiblePosition> GetPositions()
    {
        return World.GetCollectiblesSnapshot().Select(c => new CollectiblePosition
        {
            Id = c.Id,
            X = c.X,
            Y = c.Y,
            Name = c.Name,
            ItemName = c.ItemName,
            Symbol = c.Symbol
        }).ToList();
    }

    public static Item? TryCollect(int x, int y)
    {
        var collectible = World.FindCollectibleAt(x, y);
        if (collectible == null) return null;

        string itemName = collectible.ItemName;
        string collectibleName = collectible.Name;
        string itemId = _itemIdByCollectibleName.TryGetValue(itemName, out var id) ? id : Guid.NewGuid().ToString();
        World.RemoveCollectible(collectible);
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

    public static bool IsOccupied(int x, int y)
        => World.GetCollectiblesSnapshot().Any(c => c.X == x && c.Y == y);
}
