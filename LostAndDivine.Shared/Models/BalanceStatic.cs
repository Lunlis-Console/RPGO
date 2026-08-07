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
    public const double CritChancePerCunning = 1.0;
    public const double CritDamagePerStrength = 0.05;
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

    public const int MinDamage = 1;
    public const int ChanceRollMax = 100;

    // Level-up constants (duplicated from server Balance.cs, needed by Player.TryLevelUp)
    public const int MaxLevel = 50;
    public const int MaxHealthPerLevel = 10;
    public const int AttributePointsPerLevel = 3;
    public const int XpPerLevel = 50;

    public static int XpNeededForNextLevel(int level) => level * XpPerLevel;

    public static bool RollPercent(double percent)
        => System.Random.Shared.NextDouble() * 100 < percent;
}
