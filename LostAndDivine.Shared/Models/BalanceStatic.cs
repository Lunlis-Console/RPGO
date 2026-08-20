namespace LostAndDivine.Shared.Models;

/// <summary>
/// Статические константы для формул атрибутов/характеристик.
/// Дублирует часть Balance.cs, но доступен из Shared (нужен Player/Monster).
/// </summary>
public static class BalanceStatic
{
    public const int AttackPerStrength = 2;
    public const int AttackPerAgility = 1;
    public const int AttackPerIntellect = 2;
    public const int DefensePerEndurance = 1;
    public const int ResistancePerWisdom = 1;

    // Процентная защита: DR% = Defense / (Defense + DefenseReductionK), кап MaxDefenseReduction.
    // Целочисленная защита — только «валюта» для набора процента; урон режет сам процент.
    public const int DefenseReductionK = 500;
    public const double MaxDefenseReduction = 0.90;

    // Капы боевых шансов (итоговый шанс с бонусами не выше капа)
    public const double MaxCritChance = 75.0;
    // Убывающая отдача крита: первые CritChanceLinearPoints очков хитча дают
    // CritChanceLinearRate за очко (до 25%), дальше — CritChanceDrRate за очко,
    // итог капится на MaxCritChance (75%). До 75% нужно ~250 очков.
    public const double CritChanceLinearPoints = 50;
    public const double CritChanceLinearRate = 0.5;
    public const double CritChanceDrRate = 0.25;
    public const double CritChancePerCunning = 1.0;

    // Кап уклонения: база 1 + очки (хитрость + шмот, очко = 1% хитча).
    // До EvadeLinearPoints очков — EvadeLinearRate за очко (до 30%), дальше — EvadeDrRate за очко,
    // итог капится на MaxEvadeChance (50%; ~227 очков хитча).
    public const double MaxEvadeChance = 50.0;
    public const double EvadeLinearPoints = 100;
    public const double EvadeLinearRate = 0.3;
    public const double EvadeDrRate = 0.15;

    // Кап парирования: база 1 + шмот (прямой процент, не более MaxParryGearBonus) + навык «Рефлексы»,
    // итог капится на MaxParryChance (25%). Атрибуты не влияют.
    public const double MaxParryChance = 25.0;
    public const double MaxParryGearBonus = 5.0;

    // Кап блока: база 1 (+2 с щитом) + шмот (прямой процент, не более MaxBlockGearBonus),
    // итог капится на MaxBlockChance (25%). Атрибуты не влияют.
    public const double MaxBlockChance = 25.0;
    public const double MaxBlockGearBonus = 5.0;

    // Точность: база 100% + очки (ловкость + бонус лука). До AccuracyLinearPoints очков —
    // AccuracyLinearRate за очко (до 130%), дальше — AccuracyDrRate за очко,
    // итог капится на AccuracyMax (150%, чтобы нельзя было обнулить уклонение цели).
    // 50 ловкости → ~115%, с луком 4 ранга → ~145%.
    public const double AccuracyBase = 100.0;
    public const double AccuracyMax = 150.0;
    public const double AccuracyLinearPoints = 100;
    public const double AccuracyLinearRate = 0.3;
    public const double AccuracyDrRate = 0.15;

    // Стойкость: снижает шанс крита противника по вам. Кап 50%, от выносливости.
    public const double MaxTenacity = 50.0;
    public const double TenacityLinearPoints = 100;
    public const double TenacityLinearRate = 0.3;
    public const double TenacityDrRate = 0.15;

    // Пробивание брони: игнорирует % защиты цели. Кап 25%, от силы.
    public const double MaxArmorPenetration = 25.0;
    public const double ArmorPenLinearPoints = 100;
    public const double ArmorPenLinearRate = 0.15;
    public const double ArmorPenDrRate = 0.075;

    // Сокращение перезарядки навыков (кулдауны быстрее на X%). Кап 50%, от мудрости.
    public const double MaxCooldownReduction = 50.0;
    public const double CdrLinearPoints = 100;
    public const double CdrLinearRate = 0.3;
    public const double CdrDrRate = 0.15;

    // Регенерация здоровья: +X% к количеству восстанавливаемого HP. Кап 25%, от выносливости.
    public const double MaxHealthRegen = 25.0;
    public const double HealthRegenLinearPoints = 100;
    public const double HealthRegenLinearRate = 0.15;
    public const double HealthRegenDrRate = 0.075;

    // Регенерация маны: +X% к количеству восстанавливаемой MP. Кап 20%, от мудрости.
    public const double MaxManaRegen = 20.0;
    public const double ManaRegenLinearPoints = 100;
    public const double ManaRegenLinearRate = 0.15;
    public const double ManaRegenDrRate = 0.075;

    // Кап крит-урона: база 1.5 + очки (сила + шмот, очко = CritDamagePerStrength = 0.02x).
    // До CritDmgLinearPoints очков — полная отдача (до 2.0x), дальше — CritDmgDrRate темпа (0.005x),
    // итог капится на MaxCritDamage (3.0x). Кап требует ~226 очков (сила + шмот): 25 линейных + 200 с половинным темпом.
    public const double MaxCritDamage = 3.0;
    public const double CritDmgLinearPoints = 25;
    public const double CritDmgDrRate = 0.25;
    public const double CritDamagePerStrength = 0.02;
    public const double EvadeChancePerCunning = 1.0;
    public const double BlockChancePerEndurance = 0.5;
    public const double ParryChancePerAgility = 0.5;
    public const double ShieldBlockValueMultiplier = 1.5;

    // «Берсерк» (SK0011): +2% урона за каждые 5% потерянного здоровья
    public const double BerserkDamagePer5Percent = 0.02;

    // Путь лука (пассивы, доступны из Shared)
    public const double ExtraArrowChance = 7.0;
    public const double BowAccuracyBonus = 15.0;
    public const double MeleeEvadeBonus = 15.0;
    public const int BowRangeBonus = 1;
    public const double CloseRangeArmorPenMax = 0.25;
    public const int CloseRangeArmorPenDist = 2;
    public const double HunterInstinctCritBonus = 20.0;
    public const double VulnerableArmorIgnore = 0.30;

    public const string MainZoneId = "main";
    public const string StartZoneId = "airship_basement";

    // ===== СЕКТОРНЫЙ МИР (main) =====
    // Открытый мир разбит на сетку секторов: SectorCols x SectorRows, каждый размером
    // SectorSize x SectorSize клеток. Сектор именуется "{col}_{row}" (например "3_7"),
    // глобальные координаты: col = X / SectorSize, row = Y / SectorSize.
    public const int SectorSize = 100;
    public const int SectorCols = 30;
    public const int SectorRows = 17;
    public const int WorldWidth = SectorSize * SectorCols;   // 3000
    public const int WorldHeight = SectorSize * SectorRows;  // 1700

    /// <summary>Сектор входа из zone_airship (содержит контент старой zone_main).</summary>
    public const int EntrySectorCol = 3;
    public const int EntrySectorRow = 7;

    /// <summary>Глобальное смещение сектора входа: EntrySectorCol * SectorSize, EntrySectorRow * SectorSize.</summary>
    public const int EntrySectorOffsetX = EntrySectorCol * SectorSize; // 300
    public const int EntrySectorOffsetY = EntrySectorRow * SectorSize; // 700

    public const int MinDamage = 1;
    public const int ChanceRollMax = 100;

    // Level-up constants (duplicated from server Balance.cs, needed by Player.TryLevelUp)
    public const int MaxLevel = 50;
    public const int MaxHealthPerLevel = 10;
    // Выносливость даёт +10 HP за очко: вложенные очки прибавляются к MaxHealth при вложении,
    // выносливость со шмота — через Equipment.GetBonusMaxHealth().
    public const int MaxHealthPerEndurance = 10;
    public const int AttributePointsPerLevel = 3;
    public const int XpPerLevel = 50;

    public static int XpNeededForNextLevel(int level) => level * XpPerLevel;

    public static bool RollPercent(double percent)
        => System.Random.Shared.NextDouble() * 100 < percent;
}
