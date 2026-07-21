namespace RPGGame.Shared.Models;

/// <summary>
/// Снаряжение персонажа. Хранит предметы по слотам (см. EquipmentSlots).
/// Бонусы суммируются по всем надетым предметам.
/// </summary>
public class Equipment
{
    public const double DualWieldSpeedBonus = 1.15;
    public const double OffHandDamageFraction = 0.5;

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

    // Бонусы к атрибутам
    public int GetBonusStrength() => Sum(_slots.Values, i => i.BonusStrength);
    public int GetBonusEndurance() => Sum(_slots.Values, i => i.BonusEndurance);
    public int GetBonusAgility() => Sum(_slots.Values, i => i.BonusAgility);
    public int GetBonusCunning() => Sum(_slots.Values, i => i.BonusCunning);
    public int GetBonusIntellect() => Sum(_slots.Values, i => i.BonusIntellect);
    public int GetBonusWisdom() => Sum(_slots.Values, i => i.BonusWisdom);

    // Бонусы к вторичным характеристикам
    public int GetBonusPhysAttack() => Sum(_slots.Values, i => i.BonusPhysAttack);
    public int GetBonusMagAttack() => Sum(_slots.Values, i => i.BonusMagAttack);
    public int GetBonusDefense() => Sum(_slots.Values, i => i.BonusDefense);
    public int GetBonusResistance() => Sum(_slots.Values, i => i.BonusResistance);
    public int GetBonusMaxHealth() => Sum(_slots.Values, i => i.MaxHealthBonus);
    public double GetBonusCritChance() => SumD(_slots.Values, i => i.BonusCritChance);
    public double GetBonusCritDamage() => SumD(_slots.Values, i => i.BonusCritDamage);
    public double GetBonusEvadeChance() => SumD(_slots.Values, i => i.BonusEvadeChance);
    public double GetBonusAttackSpeed() => SumD(_slots.Values, i => i.BonusAttackSpeed);

    public double GetWeaponSpeedModifier()
    {
        var weapon = _slots.TryGetValue(EquipmentSlots.RightHand, out var w) ? w : null;
        double mod = weapon != null && weapon.AttackSpeedModifier > 0 ? weapon.AttackSpeedModifier : 1.0;
        if (IsDualWielding())
            mod *= DualWieldSpeedBonus;
        return mod;
    }

    public string GetWeaponDamageType()
    {
        var weapon = _slots.TryGetValue(EquipmentSlots.RightHand, out var w) ? w : null;
        return weapon?.DamageType ?? "";
    }

    public string GetWeaponSubtype()
    {
        var weapon = _slots.TryGetValue(EquipmentSlots.RightHand, out var w) ? w : null;
        return weapon?.WeaponSubtype ?? "";
    }

    public bool IsDualWielding()
    {
        var leftHand = _slots.TryGetValue(EquipmentSlots.LeftHand, out var lh) ? lh : null;
        if (leftHand == null) return false;
        string leftType = (leftHand.Type ?? "").ToLowerInvariant();
        if (leftType == "weapon" && !leftHand.TwoHanded)
            return true;
        return false;
    }

    public Item? GetOffHandWeapon()
        => IsDualWielding() ? _slots.TryGetValue(EquipmentSlots.LeftHand, out var lh) ? lh : null : null;

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
        MaxHealthBonus = src.MaxHealthBonus, HealAmount = src.HealAmount,
        Description = src.Description,
        BonusStrength = src.BonusStrength, BonusEndurance = src.BonusEndurance,
        BonusAgility = src.BonusAgility, BonusCunning = src.BonusCunning,
        BonusIntellect = src.BonusIntellect, BonusWisdom = src.BonusWisdom,
        BonusPhysAttack = src.BonusPhysAttack, BonusMagAttack = src.BonusMagAttack,
        BonusDefense = src.BonusDefense, BonusResistance = src.BonusResistance,
        BonusCritChance = src.BonusCritChance, BonusCritDamage = src.BonusCritDamage,
        BonusEvadeChance = src.BonusEvadeChance, BonusAttackSpeed = src.BonusAttackSpeed,
        TwoHanded = src.TwoHanded, DamageType = src.DamageType,
        AttackSpeedModifier = src.AttackSpeedModifier, WeaponSubtype = src.WeaponSubtype
    };
}
