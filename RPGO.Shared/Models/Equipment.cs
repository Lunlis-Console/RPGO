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

    public static bool IsCasterOffhand(Item? item)
    {
        if (item == null) return false;
        string sub = (item.WeaponSubtype ?? "").ToLowerInvariant();
        return sub == "grimoire" || sub == "sphere";
    }

    public static bool IsCasterWeapon(Item? item)
    {
        if (item == null) return false;
        string sub = (item.WeaponSubtype ?? "").ToLowerInvariant();
        return sub == "staff" || sub == "grimoire" || sub == "sphere";
    }

    public Item? GetEffectiveMainHandWeapon()
    {
        var rh = _slots.TryGetValue(EquipmentSlots.RightHand, out var w) ? w : null;
        if (rh != null) return rh;
        var lh = _slots.TryGetValue(EquipmentSlots.LeftHand, out var l) ? l : null;
        if (lh != null && IsCasterOffhand(lh)) return lh;
        return null;
    }

    public string GetWeaponDamageType()
    {
        var weapon = GetEffectiveMainHandWeapon();
        return weapon?.DamageType ?? "";
    }

    public string GetWeaponSubtype()
    {
        var weapon = GetEffectiveMainHandWeapon();
        if (weapon != null) return weapon.WeaponSubtype ?? "";
        var lh = _slots.TryGetValue(EquipmentSlots.LeftHand, out var l) ? l : null;
        if (lh != null && !IsCasterOffhand(lh) && !lh.TwoHanded && lh.WeaponSubtype != null)
            return lh.WeaponSubtype;
        return "";
    }

    public int GetWeaponAttackRange()
    {
        var weapon = GetEffectiveMainHandWeapon();
        if (weapon != null) return weapon.AttackRange;
        var lh = _slots.TryGetValue(EquipmentSlots.LeftHand, out var l) ? l : null;
        if (lh != null && !IsCasterOffhand(lh) && !lh.TwoHanded && lh.AttackRange > 0)
            return lh.AttackRange;
        return 1;
    }

    public bool IsDualWielding()
    {
        var leftHand = _slots.TryGetValue(EquipmentSlots.LeftHand, out var lh) ? lh : null;
        var rightHand = _slots.TryGetValue(EquipmentSlots.RightHand, out var rh) ? rh : null;
        if (leftHand == null || rightHand == null) return false;
        string leftType = (leftHand.Type ?? "").ToLowerInvariant();
        string rightType = (rightHand.Type ?? "").ToLowerInvariant();
        return leftType == "weapon" && !leftHand.TwoHanded && rightType == "weapon" && !rightHand.TwoHanded;
    }

    public Item? GetOffHandWeapon()
        => IsDualWielding() ? _slots.TryGetValue(EquipmentSlots.LeftHand, out var lh) ? lh : null : null;

    private static readonly Random _rng = new();

    public (int min, int max) GetWeaponDamageRange()
    {
        var weapon = GetEffectiveMainHandWeapon();
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
