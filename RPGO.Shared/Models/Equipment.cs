namespace RPGGame.Shared.Models;

public class Equipment
{
    public Item? Weapon { get; set; }
    public Item? Armor { get; set; }
    public Item? Accessory { get; set; }

    public int GetBonusAttack() =>
        (Weapon?.Attack ?? 0) + (Armor?.Attack ?? 0) + (Accessory?.Attack ?? 0);

    public int GetBonusDefense() =>
        (Weapon?.Defense ?? 0) + (Armor?.Defense ?? 0) + (Accessory?.Defense ?? 0);

    public int GetBonusMaxHealth() =>
        (Weapon?.MaxHealthBonus ?? 0) + (Armor?.MaxHealthBonus ?? 0) + (Accessory?.MaxHealthBonus ?? 0);

    public int GetBonusStrength() =>
        (Weapon?.BonusStrength ?? 0) + (Armor?.BonusStrength ?? 0) + (Accessory?.BonusStrength ?? 0);
    public int GetBonusStamina() =>
        (Weapon?.BonusStamina ?? 0) + (Armor?.BonusStamina ?? 0) + (Accessory?.BonusStamina ?? 0);
    public int GetBonusAgility() =>
        (Weapon?.BonusAgility ?? 0) + (Armor?.BonusAgility ?? 0) + (Accessory?.BonusAgility ?? 0);
    public int GetBonusCunning() =>
        (Weapon?.BonusCunning ?? 0) + (Armor?.BonusCunning ?? 0) + (Accessory?.BonusCunning ?? 0);
    public int GetBonusWisdom() =>
        (Weapon?.BonusWisdom ?? 0) + (Armor?.BonusWisdom ?? 0) + (Accessory?.BonusWisdom ?? 0);
    public int GetBonusWill() =>
        (Weapon?.BonusWill ?? 0) + (Armor?.BonusWill ?? 0) + (Accessory?.BonusWill ?? 0);

    public double GetBonusCritChance() =>
        (Weapon?.BonusCritChance ?? 0) + (Armor?.BonusCritChance ?? 0) + (Accessory?.BonusCritChance ?? 0);
    public double GetBonusCritDamage() =>
        (Weapon?.BonusCritDamage ?? 0) + (Armor?.BonusCritDamage ?? 0) + (Accessory?.BonusCritDamage ?? 0);
    public double GetBonusEvadeChance() =>
        (Weapon?.BonusEvadeChance ?? 0) + (Armor?.BonusEvadeChance ?? 0) + (Accessory?.BonusEvadeChance ?? 0);

    public Equipment Clone()
    {
        Item CloneItem(Item? src) => src == null ? null! : new Item
        {
            Id = src.Id, Name = src.Name, Type = src.Type, Value = src.Value,
            Attack = src.Attack, Defense = src.Defense, MaxHealthBonus = src.MaxHealthBonus,
            HealAmount = src.HealAmount, Description = src.Description,
            BonusStrength = src.BonusStrength, BonusStamina = src.BonusStamina,
            BonusAgility = src.BonusAgility, BonusCunning = src.BonusCunning,
            BonusWisdom = src.BonusWisdom, BonusWill = src.BonusWill,
            BonusCritChance = src.BonusCritChance, BonusCritDamage = src.BonusCritDamage,
            BonusEvadeChance = src.BonusEvadeChance
        };
        return new Equipment
        {
            Weapon = CloneItem(Weapon),
            Armor = CloneItem(Armor),
            Accessory = CloneItem(Accessory)
        };
    }
}
