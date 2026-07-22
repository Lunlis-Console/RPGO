namespace RPGGame.Shared.Models;

/// <summary>
/// Снаряжение персонажа. Хранит предметы по слотам (см. EquipmentSlots).
/// Бонусы суммируются по всем надетым предметам.
/// </summary>
public class Equipment
{
    public const double DualWieldSpeedBonus = 1.15;
    public const double TwoHandedSpeedPenalty = 0.85;
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
        else if (weapon != null && weapon.TwoHanded)
            mod *= TwoHandedSpeedPenalty;
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

    private static readonly Random _rng = new();

    public (int min, int max) GetWeaponDamageRange()
    {
        var weapon = _slots.TryGetValue(EquipmentSlots.RightHand, out var w) ? w : null;
        if (weapon == null || weapon.DamageMax <= 0) return (0, 0);
        return (weapon.DamageMin, weapon.DamageMax);
    }

    public int RollWeaponDamage()
    {
        var (min, max) = GetWeaponDamageRange();
        return min >= max ? min : _rng.Next(min, max + 1);
    }

    public int GetWeaponMaxDamage() => GetWeaponDamageRange().max;

    public (int min, int max) GetOffHandDamageRange()
    {
        var weapon = GetOffHandWeapon();
        if (weapon == null || weapon.DamageMax <= 0) return (0, 0);
        return (weapon.DamageMin, weapon.DamageMax);
    }

    public int RollOffHandDamage()
    {
        var (min, max) = GetOffHandDamageRange();
        return min >= max ? min : _rng.Next(min, max + 1);
    }

    public int GetOffHandMaxDamage() => GetOffHandDamageRange().max;

    public Equipment Clone()
    {
        var eq = new Equipment();
        foreach (var kv in _slots)
            eq._slots[kv.Key] = kv.Value == null ? null : CloneItem(kv.Value);
        return eq;
    }

    private static Item CloneItem(Item src) => src.Clone();
}
