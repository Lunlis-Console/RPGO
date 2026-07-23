using RPGGame.Shared.Models;

namespace RPGGame.Server;

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

    public MerchantManager(GameWorld world)
    {
        _world = world;
    }

    public void Initialize()
    {
        var npc = DatabaseManager.LoadNpcs().FirstOrDefault(n => n.Type == "merchant");
        if (npc != null)
        {
            MerchantX = npc.X;
            MerchantY = npc.Y;
        }
        else
        {
            MerchantX = DatabaseManager.GetWorldConfigInt("merchant_x", 50);
            MerchantY = DatabaseManager.GetWorldConfigInt("merchant_y", 50);
        }
        var merchant = DatabaseManager.LoadNpcs().FirstOrDefault(n => n.Type == "merchant");
        var stockIds = merchant != null ? DatabaseManager.LoadMerchantStock(merchant.Id) : new List<string>();

        var allItems = DatabaseManager.LoadItems();
        if (stockIds.Count > 0)
        {
            var set = new HashSet<string>(stockIds);
            ShopItems = allItems.Where(i => set.Contains(i.Id)).ToList();
        }
        else
        {
            // Дефолтный ассортимент до первого редактирования в editor
            ShopItems = allItems.Where(i => i.Type != "collectible").ToList();
        }
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
