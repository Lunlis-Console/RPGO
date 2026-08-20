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

    /// <summary>Все продаваемые предметы (не-коллекционки) без привязки к ассортименту мерчанта.
    /// Используется для наград инстансов (оружеённая), чтобы они не зависели от merchant_stock.</summary>
    public List<Item> AllItems { get; private set; } = new();

    private int? _tiledX;
    private int? _tiledY;
    private string _merchantNpcId = "";
    private Dictionary<string, int> _merchantStock = new(StringComparer.OrdinalIgnoreCase);

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
        _merchantNpcId = npc?.Id ?? "";
        _merchantStock.Clear();
        if (!string.IsNullOrEmpty(_merchantNpcId))
        {
            foreach (var (itemId, stock) in DatabaseManager.LoadMerchantStock(_merchantNpcId))
                _merchantStock[itemId] = stock;
        }
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
        var shopItems = allItems.Where(i => i.Type != "collectible");
        AllItems = shopItems.ToList();
        // Ассортимент торговца = только товары, добавленные в merchant_stock (запас торговца).
        // Если в БД нет NPC-торговца вообще — показываем все предметы (защитный fallback).
        ShopItems = string.IsNullOrEmpty(_merchantNpcId)
            ? AllItems
            : AllItems.Where(i => _merchantStock.ContainsKey(i.Id)).ToList();
        Log.Info($"Загружено предметов магазина: {ShopItems.Count}");
    }

    public Item? FindItem(string itemId)
    {
        return ShopItems.FirstOrDefault(i => i.Id == itemId);
    }

    /// <summary>
    /// Запас товара в лавке торговца (per-мерчант, из merchant_stock).
    /// Для товаров вне ассортимента — запас из шаблона предмета (legacy items.stock).
    /// </summary>
    public int GetStock(string itemId)
    {
        if (_merchantStock.TryGetValue(itemId, out int stock) && stock > 0) return stock;
        var item = FindItem(itemId);
        return Math.Max(1, item?.Stock ?? 1);
    }

    public Item CreatePlayerCopy(Item template)
    {
        var copy = template.Clone();
        copy.Id = Guid.NewGuid().ToString();
        return copy;
    }
}
