using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Services;

/// <summary>
/// Сворачивает случайные бонусы к атрибутам для предметов Необычного/Редкого/Эпического
/// качества по конфигурации шаблона (items.roll_config).
/// Шаблон один — его статичные бонусы это база Обычного качества (их же продаёт торговец);
/// качество роллится по весам конфига, а бонусы — по тиру этого качества.
/// Ролл происходит один раз в момент дропа; дальше предмет живёт как обычный экземпляр
/// со свёрнутыми бонусами и качеством.
/// </summary>
public static class ItemRoller
{
    /// <summary>
    /// Полный ролл: качество (по весам конфига) + бонусы. Если у шаблона ролл не включён —
    /// возвращает клон без изменений (со статичными бонусами и качеством шаблона).
    /// </summary>
    public static Item Roll(Item template, Random rng, int? scaleLevel = null)
    {
        var cfg = template.RollConfig;
        if (cfg is not { Enabled: true }) return template.Clone();
        return ApplyRoll(template, RollQuality(cfg, rng), rng, scaleLevel);
    }

    /// <summary>
    /// Применить ролл бонусов для уже выбранного качества (например, сундук данжа
    /// сам роллит качество). Если у шаблона ролл не включён — возвращает клон без изменений.
    /// </summary>
    public static Item RollForQuality(Item template, ItemQuality quality, Random rng, int? scaleLevel = null)
    {
        if (template.RollConfig is not { Enabled: true }) return template.Clone();
        return ApplyRoll(template, quality, rng, scaleLevel);
    }

    /// <summary>Роллит качество по весам конфига. При нулевых весах — всегда Обычный.</summary>
    public static ItemQuality RollQuality(ItemRollConfig cfg, Random rng)
    {
        int u = Math.Max(0, cfg.WeightUncommon);
        int r = Math.Max(0, cfg.WeightRare);
        int e = Math.Max(0, cfg.WeightEpic);
        int total = u + r + e;
        if (total <= 0) return ItemQuality.Common;

        int roll = rng.Next(total);
        if (roll < u) return ItemQuality.Uncommon;
        roll -= u;
        if (roll < r) return ItemQuality.Rare;
        roll -= r;
        if (roll < e) return ItemQuality.Epic;
        return ItemQuality.Common;
    }

    /// <summary>Возвращает конфиг тира для указанного качества (у Обычного ролла нет).</summary>
    public static RollTierConfig? TierFor(ItemRollConfig cfg, ItemQuality quality) => quality switch
    {
        ItemQuality.Uncommon => cfg.Uncommon,
        ItemQuality.Rare => cfg.Rare,
        ItemQuality.Epic => cfg.Epic,
        _ => null
    };

    private static Item ApplyRoll(Item template, ItemQuality quality, Random rng, int? scaleLevel)
    {
        var item = template.Clone();
        item.Quality = quality;

        var tier = TierFor(template.RollConfig!, quality);
        if (quality == ItemQuality.Common || tier == null) return item;

        var pool = tier.Stats.Where(s => !string.IsNullOrWhiteSpace(s.Stat) && s.Min > 0).ToList();
        if (pool.Count == 0) return item;

        ClearBonuses(item);

        int countMin = Math.Max(0, tier.CountMin);
        int countMax = Math.Max(countMin, Math.Max(0, tier.CountMax));
        int count = countMin >= countMax ? countMin : rng.Next(countMin, countMax + 1);
        count = Math.Min(count, pool.Count);

        int level = Math.Max(1, scaleLevel ?? Math.Max(1, template.RequiredLevel));
        Shuffle(pool, rng);
        for (int i = 0; i < count; i++)
        {
            var stat = pool[i];
            double value = stat.Min + rng.NextDouble() * Math.Max(0, stat.Max - stat.Min);
            Apply(item, stat.Stat, value * level);
        }

        return item;
    }

    private static void ClearBonuses(Item item)
    {
        item.MaxHealthBonus = 0;
        item.MaxManaBonus = 0;
        item.BonusStrength = 0;
        item.BonusEndurance = 0;
        item.BonusAgility = 0;
        item.BonusCunning = 0;
        item.BonusIntellect = 0;
        item.BonusWisdom = 0;
        item.BonusPhysAttack = 0;
        item.BonusMagAttack = 0;
        item.BonusDefense = 0;
        item.BonusResistance = 0;
        item.BonusCritChance = 0;
        item.BonusCritDamage = 0;
        item.BonusEvadeChance = 0;
        item.BonusAttackSpeed = 0;
        item.BonusBlockChance = 0;
        item.BonusParryChance = 0;
        item.BonusAccuracy = 0;
        item.BonusTenacity = 0;
        item.BonusArmorPenetration = 0;
        item.BonusCooldownReduction = 0;
        item.BonusHpRegen = 0;
        item.BonusMpRegen = 0;
    }

    private static void Apply(Item item, string stat, double value)
    {
        // Итоговые значения в игре: обычные статы — целые числа (округление от нуля),
        // процентные — до одного знака после запятой.
        int intVal = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        double pctVal = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        switch (stat)
        {
            case "Strength": item.BonusStrength = intVal; break;
            case "Endurance": item.BonusEndurance = intVal; break;
            case "Agility": item.BonusAgility = intVal; break;
            case "Cunning": item.BonusCunning = intVal; break;
            case "Intellect": item.BonusIntellect = intVal; break;
            case "Wisdom": item.BonusWisdom = intVal; break;
            case "MaxHealth": item.MaxHealthBonus = intVal; break;
            case "MaxMana": item.MaxManaBonus = intVal; break;
            case "PhysAttack": item.BonusPhysAttack = intVal; break;
            case "MagAttack": item.BonusMagAttack = intVal; break;
            case "Defense": item.BonusDefense = intVal; break;
            case "Resistance": item.BonusResistance = intVal; break;
            case "CritChance": item.BonusCritChance = pctVal; break;
            case "CritDamage": item.BonusCritDamage = pctVal; break;
            case "EvadeChance": item.BonusEvadeChance = pctVal; break;
            case "AttackSpeed": item.BonusAttackSpeed = pctVal; break;
            case "BlockChance": item.BonusBlockChance = pctVal; break;
            case "ParryChance": item.BonusParryChance = pctVal; break;
            case "Accuracy": item.BonusAccuracy = pctVal; break;
            case "Tenacity": item.BonusTenacity = pctVal; break;
            case "ArmorPenetration": item.BonusArmorPenetration = pctVal; break;
            case "CooldownReduction": item.BonusCooldownReduction = pctVal; break;
            case "HpRegen": item.BonusHpRegen = pctVal; break;
            case "MpRegen": item.BonusMpRegen = pctVal; break;
        }
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}