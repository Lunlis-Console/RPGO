using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server;

public class MerchantManager
{
    private readonly GameWorld _world;

    public int MerchantX
    {
        get => _world.Map.MerchantX;
        private set => _world.Map.MerchantX = value;
    }

    public int MerchantY
    {
        get => _world.Map.MerchantY;
        private set => _world.Map.MerchantY = value;
    }

    public List<Item> ShopItems { get; private set; } = new();

    private int? _tiledX;
    private int? _tiledY;

    public MerchantManager(GameWorld world)
    {
        _world = world;
    }

    /// <summary>Позиция торговца из Tiled-карты (приоритет над БД).</summary>
    public void SetTiledPosition(int x, int y)
    {
        _tiledX = x;
        _tiledY = y;
    }

    public void Initialize()
    {
        var npc = DatabaseManager.LoadNpcs().FirstOrDefault(n => n.Type == "merchant");
        if (_tiledX.HasValue && _tiledY.HasValue)
        {
            MerchantX = _tiledX.Value;
            MerchantY = _tiledY.Value;
        }
        else if (npc != null)
        {
            MerchantX = npc.X;
            MerchantY = npc.Y;
        }
        else
        {
            MerchantX = DatabaseManager.GetWorldConfigInt("merchant_x", 50);
            MerchantY = DatabaseManager.GetWorldConfigInt("merchant_y", 50);
        }
        var allItems = DatabaseManager.LoadItems();
        // Показываем все предметы кроме собираемых (collectible)
        ShopItems = allItems.Where(i => i.Type != "collectible").ToList();
        Log.Info($"Загружено предметов магазина: {ShopItems.Count}");
    }

    public Item? FindItem(string itemId)
    {
        return ShopItems.FirstOrDefault(i => i.Id == itemId);
    }

    public Item CreatePlayerCopy(Item template)
    {
        var copy = template.Clone();
        copy.Id = Guid.NewGuid().ToString();
        return copy;
    }
}
