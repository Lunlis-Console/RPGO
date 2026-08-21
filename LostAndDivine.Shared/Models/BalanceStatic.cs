namespace LostAndDivine.Shared.Models;

/// <summary>
/// Фасад для обратной совместимости. Константы переехали в CombatBalance/WorldBalance/ProgressionBalance/EnhancementBalance.
/// Новый код должен использовать специализированные классы напрямую.
/// </summary>
public static class BalanceStatic
{
    // Combat
    public const int AttackPerStrength = CombatBalance.AttackPerStrength;
    public const int AttackPerAgility = CombatBalance.AttackPerAgility;
    public const int AttackPerIntellect = CombatBalance.AttackPerIntellect;
    public const int DefensePerEndurance = CombatBalance.DefensePerEndurance;
    public const int ResistancePerWisdom = CombatBalance.ResistancePerWisdom;

    public const double EnhancementBonusPerLevel = EnhancementBalance.PerLevelRate;
    public const int EnhancementMaxLevel = EnhancementBalance.MaxLevel;

    public const int DefenseReductionK = CombatBalance.DefenseReductionK;
    public const double MaxDefenseReduction = CombatBalance.MaxDefenseReduction;

    public const double MaxCritChance = CombatBalance.MaxCritChance;
    public const double CritChanceLinearPoints = CombatBalance.CritChanceLinearPoints;
    public const double CritChanceLinearRate = CombatBalance.CritChanceLinearRate;
    public const double CritChanceDrRate = CombatBalance.CritChanceDrRate;
    public const double CritChancePerCunning = CombatBalance.CritChancePerCunning;

    public const double MaxEvadeChance = CombatBalance.MaxEvadeChance;
    public const double EvadeLinearPoints = CombatBalance.EvadeLinearPoints;
    public const double EvadeLinearRate = CombatBalance.EvadeLinearRate;
    public const double EvadeDrRate = CombatBalance.EvadeDrRate;

    public const double MaxParryChance = CombatBalance.MaxParryChance;
    public const double MaxParryGearBonus = CombatBalance.MaxParryGearBonus;

    public const double MaxBlockChance = CombatBalance.MaxBlockChance;
    public const double MaxBlockGearBonus = CombatBalance.MaxBlockGearBonus;

    public const double AccuracyBase = CombatBalance.AccuracyBase;
    public const double AccuracyMax = CombatBalance.AccuracyMax;
    public const double AccuracyLinearPoints = CombatBalance.AccuracyLinearPoints;
    public const double AccuracyLinearRate = CombatBalance.AccuracyLinearRate;
    public const double AccuracyDrRate = CombatBalance.AccuracyDrRate;

    public const double MaxCastSpeedReduction = CombatBalance.MaxCastSpeedReduction;
    public const double CastSpeedLinearPoints = CombatBalance.CastSpeedLinearPoints;
    public const double CastSpeedLinearRate = CombatBalance.CastSpeedLinearRate;
    public const double CastSpeedDrRate = CombatBalance.CastSpeedDrRate;

    public const double MaxTenacity = CombatBalance.MaxTenacity;
    public const double TenacityLinearPoints = CombatBalance.TenacityLinearPoints;
    public const double TenacityLinearRate = CombatBalance.TenacityLinearRate;
    public const double TenacityDrRate = CombatBalance.TenacityDrRate;

    public const double MaxArmorPenetration = CombatBalance.MaxArmorPenetration;
    public const double ArmorPenLinearPoints = CombatBalance.ArmorPenLinearPoints;
    public const double ArmorPenLinearRate = CombatBalance.ArmorPenLinearRate;
    public const double ArmorPenDrRate = CombatBalance.ArmorPenDrRate;

    public const double MaxCooldownReduction = CombatBalance.MaxCooldownReduction;
    public const double CdrLinearPoints = CombatBalance.CdrLinearPoints;
    public const double CdrLinearRate = CombatBalance.CdrLinearRate;
    public const double CdrDrRate = CombatBalance.CdrDrRate;

    public const double MaxHealthRegen = CombatBalance.MaxHealthRegen;
    public const double HealthRegenLinearPoints = CombatBalance.HealthRegenLinearPoints;
    public const double HealthRegenLinearRate = CombatBalance.HealthRegenLinearRate;
    public const double HealthRegenDrRate = CombatBalance.HealthRegenDrRate;

    public const double MaxManaRegen = CombatBalance.MaxManaRegen;

    public const double MaxCritDamage = CombatBalance.MaxCritDamage;
    public const double CritDmgLinearPoints = CombatBalance.CritDmgLinearPoints;
    public const double CritDmgDrRate = CombatBalance.CritDmgDrRate;
    public const double CritDamagePerStrength = CombatBalance.CritDamagePerStrength;
    public const double EvadeChancePerCunning = CombatBalance.EvadeChancePerCunning;
    public const double BlockChancePerEndurance = CombatBalance.BlockChancePerEndurance;
    public const double ParryChancePerAgility = CombatBalance.ParryChancePerAgility;
    public const double ShieldBlockValueMultiplier = CombatBalance.ShieldBlockValueMultiplier;

    public const double BerserkDamagePer5Percent = CombatBalance.BerserkDamagePer5Percent;

    public const double ExtraArrowChance = CombatBalance.ExtraArrowChance;
    public const double BowAccuracyBonus = CombatBalance.BowAccuracyBonus;
    public const double MeleeEvadeBonus = CombatBalance.MeleeEvadeBonus;
    public const int BowRangeBonus = CombatBalance.BowRangeBonus;
    public const double CloseRangeArmorPenMax = CombatBalance.CloseRangeArmorPenMax;
    public const int CloseRangeArmorPenDist = CombatBalance.CloseRangeArmorPenDist;
    public const double HunterInstinctCritBonus = CombatBalance.HunterInstinctCritBonus;
    public const double VulnerableArmorIgnore = CombatBalance.VulnerableArmorIgnore;

    public const string MainZoneId = WorldBalance.MainZoneId;
    public const string StartZoneId = WorldBalance.StartZoneId;

    public const int SectorSize = WorldBalance.SectorSize;
    public const int SectorCols = WorldBalance.SectorCols;
    public const int SectorRows = WorldBalance.SectorRows;
    public const int WorldWidth = WorldBalance.WorldWidth;
    public const int WorldHeight = WorldBalance.WorldHeight;

    public const int EntrySectorCol = WorldBalance.EntrySectorCol;
    public const int EntrySectorRow = WorldBalance.EntrySectorRow;
    public const int EntrySectorOffsetX = WorldBalance.EntrySectorOffsetX;
    public const int EntrySectorOffsetY = WorldBalance.EntrySectorOffsetY;

    public const int MinDamage = WorldBalance.MinDamage;
    public const int ChanceRollMax = WorldBalance.ChanceRollMax;

    public const int MaxLevel = ProgressionBalance.MaxLevel;
    public const int MaxHealthPerLevel = ProgressionBalance.MaxHealthPerLevel;
    public const int MaxHealthPerEndurance = ProgressionBalance.MaxHealthPerEndurance;
    public const int AttributePointsPerLevel = ProgressionBalance.AttributePointsPerLevel;
    public const int XpPerLevel = ProgressionBalance.XpPerLevel;

    public static int XpNeededForNextLevel(int level) => ProgressionBalance.XpNeededForNextLevel(level);
    public static bool RollPercent(double percent) => ProgressionBalance.RollPercent(percent);
}
