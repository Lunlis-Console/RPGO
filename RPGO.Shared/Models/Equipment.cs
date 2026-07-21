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

    /// <summary>
    /// Модификатор скорости атаки оружия в правой руке.
    /// 1.0 = базовая, >1 = быстрее (кинжалы), <1 = медленнее (булавы/молоты).
    /// Если оружие не надето — возвращает 1.0.
    /// При двойном оружии (dual wield) +15% к скорости атаки.
    /// </summary>
    public double GetWeaponSpeedModifier()
    {
        var weapon = _slots.TryGetValue(EquipmentSlots.RightHand, out var w) ? w : null;
        double mod = weapon != null && weapon.AttackSpeedModifier > 0 ? weapon.AttackSpeedModifier : 1.0;
        if (IsDualWielding())
            mod *= DualWieldSpeedBonus;
        return mod;
    }

    /// <summary>Тип урона оружия в правой руке (строка: "slashing", "piercing", "blunt").</summary>
    public string GetWeaponDamageType()
    {
        var weapon = _slots.TryGetValue(EquipmentSlots.RightHand, out var w) ? w : null;
        return weapon?.DamageType ?? "";
    }

    /// <summary>
    /// Проверяет,_dual wield ли игрок: в левой руке оружие (тип weapon), а не щит.
    /// Двуручное оружие в правой руке блокирует левую → dual wield невозможен.
    /// </summary>
    public bool IsDualWielding()
    {
        var leftHand = _slots.TryGetValue(EquipmentSlots.LeftHand, out var lh) ? lh : null;
        if (leftHand == null) return false;
        string leftType = (leftHand.Type ?? "").ToLowerInvariant();
        if (leftType == "weapon" && !leftHand.TwoHanded)
            return true;
        return false;
    }

    /// <summary>Оружие в левой руке (для dual wield). Если IsDualWielding() == false → null.</summary>
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
        Attack = src.Attack, Defense = src.Defense, MaxHealthBonus = src.MaxHealthBonus,
        HealAmount = src.HealAmount, Description = src.Description,
        BonusStrength = src.BonusStrength, BonusStamina = src.BonusStamina,
        BonusAgility = src.BonusAgility, BonusCunning = src.BonusCunning,
        BonusWisdom = src.BonusWisdom, BonusWill = src.BonusWill,
        BonusCritChance = src.BonusCritChance, BonusCritDamage = src.BonusCritDamage,
        BonusEvadeChance = src.BonusEvadeChance, TwoHanded = src.TwoHanded,
        DamageType = src.DamageType, AttackSpeedModifier = src.AttackSpeedModifier,
        WeaponSubtype = src.WeaponSubtype
    };
}
