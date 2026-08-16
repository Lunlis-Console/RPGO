using LostAndDivine.Shared.Models;
using LostAndDivine.Server.Services;

namespace LostAndDivine.Server;

public class Collectible
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ItemName { get; set; } = "";
    public char Symbol { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string ZoneId { get; set; } = Balance.MainZoneId;
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

    public void Initialize(List<TiledSpawn>? spawns = null, string zoneId = "")
    {
        if (string.IsNullOrEmpty(zoneId))
            zoneId = Balance.MainZoneId;
        _world.ClearCollectiblesInZone(zoneId);

        // В списке точек из Tiled лежат и спавны монстров: оставляем только те,
        // чьи имена совпадают с шаблонами собираемых объектов.
        var collectibleSpawns = spawns?
            .Where(s => _templates.Any(t => string.Equals(t.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (collectibleSpawns != null && collectibleSpawns.Count > 0)
        {
            int spawned = 0;
            foreach (var s in collectibleSpawns)
            {
                var tpl = _templates.FirstOrDefault(t => string.Equals(t.Name, s.Name, StringComparison.OrdinalIgnoreCase));
                if (tpl.ItemName == null)
                    continue;

                if (_world.Map.IsObstacle(s.X, s.Y))
                {
                    Log.Warn($"Точка спавна '{s.Name}' на непроходимой клетке ({s.X},{s.Y}), пропускаю");
                    continue;
                }

                _world.AddCollectible(new Collectible
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = tpl.Name,
                    ItemName = tpl.ItemName,
                    Symbol = tpl.Symbol,
                    X = s.X,
                    Y = s.Y,
                    ZoneId = zoneId
                });
                spawned++;
            }
            Log.Info($"Спавн коллекционок из точек в зоне '{zoneId}': {spawned}");
        }
        else if (zoneId == Balance.MainZoneId)
        {
            // Случайный разброс собираемых предметов — только для основного мира.
            // В остальных зонах собираемые предметы появляются только из явных
            // точек спавна в Tiled-карте; иначе мусор из размеров главной карты
            // (SpawnOne использует _world.Map) протекает в чужие зоны.
            int spawned = 0;
            foreach (var template in _templates)
            {
                for (int i = 0; i < template.Count; i++)
                {
                    if (SpawnOne(template.Name, template.ItemName, template.Symbol, zoneId))
                        spawned++;
                }
            }
            Log.Info($"Случайный разброс собираемых предметов в зоне '{zoneId}': {spawned}");
        }
    }

    private bool SpawnOne(string name, string itemName, char symbol, string zoneId)
    {
        int mapW = _world.Map.Width;
        int mapH = _world.Map.Height;
        int x, y;
        int attempts = 0;
        do
        {
            x = _world.NextRandom(0, mapW);
            y = _world.NextRandom(0, mapH);
            attempts++;
        } while ((IsOccupied(x, y, zoneId) || _world.Map.IsObstacle(x, y)) && attempts < Balance.SpawnMaxAttempts);

        if (attempts >= Balance.SpawnMaxAttempts) return false;

        _world.AddCollectible(new Collectible
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            ItemName = itemName,
            Symbol = symbol,
            X = x,
            Y = y,
            ZoneId = zoneId
        });
        return true;
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
            Symbol = c.Symbol,
            ZoneId = c.ZoneId
        }).ToList();
    }

    public Item? TryCollect(int x, int y, string zoneId)
    {
        var collectible = _world.FindCollectibleAt(x, y);
        if (collectible == null || collectible.ZoneId != zoneId) return null;

        string itemName = collectible.ItemName;
        string collectibleName = collectible.Name;
        string itemId = _itemIdByCollectibleName.TryGetValue(itemName, out var id) ? id : Guid.NewGuid().ToString();
        _world.RemoveCollectible(collectible);
        SpawnOne(collectibleName, itemName, collectible.Symbol, collectible.ZoneId);

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

    public bool IsOccupied(int x, int y, string zoneId)
        => _world.GetCollectiblesSnapshot().Any(c => c.X == x && c.Y == y && c.ZoneId == zoneId);
}
