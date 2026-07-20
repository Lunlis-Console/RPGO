namespace RPGGame.Shared.Models;

/// <summary>
/// Снаряжение персонажа. Хранит предметы по слотам (см. EquipmentSlots).
/// Бонусы суммируются по всем надетым предметам.
/// </summary>
public class Equipment
{
    private readonly Dictionary<string, Item?> _slots = new();

    public Item? this[string slot]
    {
        get => _slots.TryGetValue(slot, out var i) ? i : null;
        set => _slots[slot] = value;
    }

    public IReadOnlyDictionary<string, Item?> Slots => _slots;

    private static int Sum(IEnumerable<Item?> items, Func<Item, int> sel) =>
        items.Sum(i => i == null ? 0 : sel(i));

    private static double SumD(IEnumerable<Item?> items, Func<Item, double> sel) =>
        items.Sum(i => i == null ? 0 : sel(i));

    public int GetBonusAttack() => Sum(_slots.Values, i => i.Attack);
    public int GetBonusDefense() => Sum(_slots.Values, i => i.Defense);
    public int GetBonusMaxHealth() => Sum(_slots.Values, i => i.MaxHealthBonus);
    public int GetBonusStrength() => Sum(_slots.Values, i => i.BonusStrength);
    public int GetBonusStamina() => Sum(_slots.Values, i => i.BonusStamina);
    public int GetBonusAgility() => Sum(_slots.Values, i => i.BonusAgility);
    public int GetBonusCunning() => Sum(_slots.Values, i => i.BonusCunning);
    public int GetBonusWisdom() => Sum(_slots.Values, i => i.BonusWisdom);
    public int GetBonusWill() => Sum(_slots.Values, i => i.BonusWill);
    public double GetBonusCritChance() => SumD(_slots.Values, i => i.BonusCritChance);
    public double GetBonusCritDamage() => SumD(_slots.Values, i => i.BonusCritDamage);
    public double GetBonusEvadeChance() => SumD(_slots.Values, i => i.BonusEvadeChance);

    public Equipment Clone()
    {
        var eq = new Equipment();
        foreach (var kv in _slots)
            eq._slots[kv.Key] = kv.Value == null ? null : CloneItem(kv.Value);
        return eq;
    }

    private static Item CloneItem(Item src) => new Item
    {
        Id = src.Id, Name = src.Name, Type = src.Type, Value = src.Value,
        Attack = src.Attack, Defense = src.Defense, MaxHealthBonus = src.MaxHealthBonus,
        HealAmount = src.HealAmount, Description = src.Description,
        BonusStrength = src.BonusStrength, BonusStamina = src.BonusStamina,
        BonusAgility = src.BonusAgility, BonusCunning = src.BonusCunning,
        BonusWisdom = src.BonusWisdom, BonusWill = src.BonusWill,
        BonusCritChance = src.BonusCritChance, BonusCritDamage = src.BonusCritDamage,
        BonusEvadeChance = src.BonusEvadeChance, TwoHanded = src.TwoHanded
    };
}
