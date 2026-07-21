using RPGGame.Shared.Models;

namespace RPGGame.Server;

public static class MerchantManager
{
    public static int MerchantX
    {
        get => Program.World.Map.MerchantX;
        private set => Program.World.Map.MerchantX = value;
    }

    public static int MerchantY
    {
        get => Program.World.Map.MerchantY;
        private set => Program.World.Map.MerchantY = value;
    }

    public static List<Item> ShopItems { get; private set; } = new();

    public static void Initialize()
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

    public static Item? FindItem(string itemId)
    {
        return ShopItems.FirstOrDefault(i => i.Id == itemId);
    }

    public static Item CreatePlayerCopy(Item template)
    {
        return new Item
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = template.TemplateId,
            Name = template.Name,
            Type = template.Type,
            Value = template.Value,
            MaxHealthBonus = template.MaxHealthBonus,
            HealAmount = template.HealAmount,
            Description = template.Description,
            MaxStack = template.MaxStack,
            BonusStrength = template.BonusStrength,
            BonusEndurance = template.BonusEndurance,
            BonusAgility = template.BonusAgility,
            BonusCunning = template.BonusCunning,
            BonusIntellect = template.BonusIntellect,
            BonusWisdom = template.BonusWisdom,
            BonusCritChance = template.BonusCritChance,
            BonusCritDamage = template.BonusCritDamage,
            BonusEvadeChance = template.BonusEvadeChance,
            TwoHanded = template.TwoHanded,
            DamageType = template.DamageType,
            AttackSpeedModifier = template.AttackSpeedModifier
        };
    }
}
