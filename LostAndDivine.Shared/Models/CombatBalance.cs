namespace LostAndDivine.Shared.Models;

/// <summary>
/// Боевой баланс: урон, защита, шансы, капы, diminishing returns.
/// Выделено из God-Class BalanceStatic.cs:160 (SRP).
/// </summary>
public static class CombatBalance
{
    public const int AttackPerStrength = 2;
    public const int AttackPerAgility = 1;
    public const int AttackPerIntellect = 2;
    public const int DefensePerEndurance = 1;
    public const int ResistancePerWisdom = 1;

    public const int DefenseReductionK = 500;
    public const double MaxDefenseReduction = 0.90;

    public const double MaxCritChance = 75.0;
    public const double CritChanceLinearPoints = 50;
    public const double CritChanceLinearRate = 0.5;
    public const double CritChanceDrRate = 0.25;
    public const double CritChancePerCunning = 1.0;

    public const double MaxEvadeChance = 50.0;
    public const double EvadeLinearPoints = 100;
    public const double EvadeLinearRate = 0.3;
    public const double EvadeDrRate = 0.15;

    public const double MaxParryChance = 25.0;
    public const double MaxParryGearBonus = 5.0;

    public const double MaxBlockChance = 25.0;
    public const double MaxBlockGearBonus = 5.0;

    public const double AccuracyBase = 100.0;
    public const double AccuracyMax = 150.0;
    public const double AccuracyLinearPoints = 100;
    public const double AccuracyLinearRate = 0.3;
    public const double AccuracyDrRate = 0.15;

    public const double MaxCastSpeedReduction = 50.0;
    public const double CastSpeedLinearPoints = 100;
    public const double CastSpeedLinearRate = 0.3;
    public const double CastSpeedDrRate = 0.15;

    public const double MaxTenacity = 50.0;
    public const double TenacityLinearPoints = 100;
    public const double TenacityLinearRate = 0.3;
    public const double TenacityDrRate = 0.15;

    public const double MaxArmorPenetration = 25.0;
    public const double ArmorPenLinearPoints = 100;
    public const double ArmorPenLinearRate = 0.15;
    public const double ArmorPenDrRate = 0.075;

    public const double MaxCooldownReduction = 50.0;
    public const double CdrLinearPoints = 100;
    public const double CdrLinearRate = 0.3;
    public const double CdrDrRate = 0.15;

    public const double MaxHealthRegen = 25.0;
    public const double HealthRegenLinearPoints = 100;
    public const double HealthRegenLinearRate = 0.15;
    public const double HealthRegenDrRate = 0.075;

    public const double MaxManaRegen = 20.0;

    public const double MaxCritDamage = 3.0;
    public const double CritDmgLinearPoints = 25;
    public const double CritDmgDrRate = 0.25;
    public const double CritDamagePerStrength = 0.02;
    public const double EvadeChancePerCunning = 1.0;
    public const double BlockChancePerEndurance = 0.5;
    public const double ParryChancePerAgility = 0.5;
    public const double ShieldBlockValueMultiplier = 1.5;

    public const double BerserkDamagePer5Percent = 0.02;

    public const double ExtraArrowChance = 7.0;
    public const double BowAccuracyBonus = 15.0;
    public const double MeleeEvadeBonus = 15.0;
    public const int BowRangeBonus = 1;
    public const double CloseRangeArmorPenMax = 0.25;
    public const int CloseRangeArmorPenDist = 2;
    public const double HunterInstinctCritBonus = 20.0;
    public const double VulnerableArmorIgnore = 0.30;
}
