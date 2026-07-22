using RPGGame.Shared.Models;

namespace RPGGame.Server;

/// <summary>
/// Единая точка всех магических чисел и игрового баланса сервера.
/// </summary>
public static class Balance
{
    // ===== МИР / КОНФИГ =====
    public const int WorldWidth = 100;
    public const int WorldHeight = 100;
    public const int DefaultMerchantX = 50;
    public const int DefaultMerchantY = 50;
    public const int DefaultBoardX = 48;
    public const int DefaultBoardY = 48;
    public const int ViewRadius = 30;
    public const int ServerPort = 7777;

    public const int InteractRange = 1;
    public const int AttackRange = 1;

    public const int RespawnJitterMin = -3;
    public const int RespawnJitterMax = 4;

    // ===== МАНЕКЕН =====
    public const int MannequinHealth = 10000;
    public const int MannequinRegenDelayMs = 5000;
    public const int MannequinOffsetX = -4;
    public const int MannequinOffsetY = -2;

    // ===== ДВИЖЕНИЕ =====
    public const double BaseMoveMs = 500.0;
    public const double AttackBaseMs = 1000.0;

    // ===== БОЙ: АТРИБУТЫ (shared c BalanceStatic) =====
    public const int AttackPerStrength = BalanceStatic.AttackPerStrength;
    public const int AttackPerAgility = BalanceStatic.AttackPerAgility;
    public const int AttackPerIntellect = BalanceStatic.AttackPerIntellect;
    public const int DefensePerEndurance = BalanceStatic.DefensePerEndurance;
    public const int ResistancePerWisdom = BalanceStatic.ResistancePerWisdom;
    public const double CritChancePerCunning = BalanceStatic.CritChancePerCunning;
    public const double CritDamagePerStrength = BalanceStatic.CritDamagePerStrength;
    public const double EvadeChancePerCunning = BalanceStatic.EvadeChancePerCunning;

    // Базовые боевые множители
    public const double BaseCritChance = 1.0;
    public const double BaseCritDamage = 1.5;
    public const double BaseEvadeChance = 1.0;

    public const int BaseDamagePerLevel = 1;
    public const int BaseDefensePerLevel = 1;

    public const int AttackSpeedBase = 1;
    public const int AttackSpeedAgilityDivisor = 5;
    public const double AgilityDrK = 30.0;
    public const int MinAttackIntervalMs = 300;

    public const int MinDamage = 1;
    public const int ChanceRollMax = 100;

    // ===== ОПЫТ / УРОВНИ =====
    public const int XpPerLevel = 50;
    public const int MaxHealthPerLevel = 10;
    public const int MaxHealthPerEndurance = 5;
    public const int AttributePointsPerLevel = 3;

    // ===== СМЕРТЬ =====
    public const double DeathHealthFraction = 0.5;
    public const int DeathGoldLoss = 20;

    // ===== DUAL WIELD =====
    public const double DualWieldSpeedBonus = 1.15;
    public const double OffHandDamageFraction = 0.5;
    public const int OffHandDelayMs = 250;

    // ===== РЕГЕНЕРАЦИЯ (игрок) =====
    public const int PlayerRegenInCombatDelayMs = 4000;
    public const int PlayerRegenOutOfCombatHeal = 5;
    public const int PlayerRegenOutOfCombatTickMs = 5000;
    public const double PlayerRegenInCombatFraction = 0.015;
    public const int PlayerRegenInCombatTickMs = 4000;
    public const int PlayerRegenMinHeal = 1;

    // ===== МАНА (MP) =====
    public const int ManaPerWisdom = 5;
    public const int ManaBase = 20;
    public const int ManaRegenOutOfCombat = 2;
    public const int ManaRegenOutOfCombatTickMs = 2000;
    public const double ManaRegenInCombatFraction = 0.01;
    public const int ManaRegenInCombatTickMs = 3000;
    public const int ManaRegenMin = 1;

    // ===== РЕГЕНЕРАЦИЯ (монстр) =====
    public const int MonsterRegenFullHealDelayMs = 5000;
    public const int MonsterRegenInCombatDelayMs = 5000;
    public const int MonsterRegenOutOfCombatHeal = 5;
    public const int MonsterRegenOutOfCombatTickMs = 5000;
    public const double MonsterRegenInCombatFraction = 0.01;
    public const int MonsterRegenInCombatTickMs = 4000;
    public const int MonsterRegenMinHeal = 1;

    // ===== МОНСТРЫ =====
    public const int MonsterSpawnCount = 120;
    public const int SpawnMaxAttempts = 200;
    public const double SpawnDifficultyPerDist = 0.015;
    public const int SpawnSafeRadiusFromMerchant = 6;
    public const int MonsterWanderRadius = 5;
    public const int MonsterAggroRange = 5;
    public const int MonsterWanderSkipChance = 40;
    public const int MonsterMoveMinMs = 800;
    public const int MonsterMoveMaxMs = 2500;
    public const int MonsterSpawnJitterMaxMs = 2000;

    public const int MonsterAttrBase = 1;
    public const int MonsterAttrDivisor = 2;
    public const int MonsterAgilityPerTier = 1;

    public static int MonsterTierByDistance(int dist) => dist switch
    {
        <= 15 => 1,
        <= 25 => 2,
        <= 35 => 3,
        _     => 4
    };

    // ===== ЛУТ / ДРОП =====
    public const int LootDropChance = 40;
    public const int CollectibleValue = 2;

    // ===== ИНВЕНТАРЬ =====
    public const int HotbarSlots = 10;
    public const int DefaultMaxStack = 10;
    public const int UniqueItemMaxStack = 1;

    public static int MaxStackForType(string? type)
        => type == "consumable" || type == "collectible" || type == "trophy" || type == "material"
            ? DefaultMaxStack : UniqueItemMaxStack;

    // ===== ТОРГОВЛЯ =====
    public const double BuybackFraction = 0.5;
    public const double SellFraction = 0.5;

    // ===== ЦИКЛЫ СЕРВЕРА =====
    public const int LoopMonsterWanderMs = 1500;
    public const int LoopMovePathMs = 200;
    public const int LoopCombatMs = 200;
    public const int LoopMonsterAttackMs = 500;

    // ===== ФОРМУЛЫ =====

    public static int MoveIntervalMs(int speed)
        => (int)(BaseMoveMs / Math.Max(1, speed));

    public static int AttackIntervalMs(int attackSpeed)
        => Math.Max(MinAttackIntervalMs, (int)(AttackBaseMs / Math.Max(1, attackSpeed)));

    public static int AttackIntervalMs(int baseAgilitySpeed, double weaponSpeedMod)
        => Math.Max(MinAttackIntervalMs, (int)(AttackBaseMs / Math.Max(0.1, baseAgilitySpeed * Math.Max(0.1, weaponSpeedMod))));

    public static int GetAttackSpeed(int agility)
    {
        double effective = AgilityDrK * agility / (AgilityDrK + agility);
        return Math.Max(1, AttackSpeedBase + (int)(effective / AttackSpeedAgilityDivisor));
    }

    public static int GetAttackSpeedWithWeapon(int agility, double weaponSpeedMod)
    {
        int baseSpeed = GetAttackSpeed(agility);
        double effective = baseSpeed * Math.Max(0.1, weaponSpeedMod);
        return Math.Max(1, (int)Math.Round(effective));
    }

    public static int XpNeededForNextLevel(int level)
        => level * XpPerLevel;

    public static int BuyPrice(int baseValue)
        => Math.Max(1, baseValue);

    public static int SellPrice(int baseValue)
        => Math.Max(1, (int)(baseValue * SellFraction));

    public static int BuybackPrice(int baseValue)
        => Math.Max(1, (int)(baseValue * BuybackFraction));

    public static int ComputeDeathGoldLoss(int gold)
        => Math.Min(gold, DeathGoldLoss);

    public static int RespawnHealth(int maxHealth)
        => (int)(maxHealth * DeathHealthFraction);

    // ===== МАНА =====
    public static int MaxMana(int wisdom)
        => ManaBase + Math.Max(0, wisdom - 1) * ManaPerWisdom;

    // ===== ОРУЖЕЙНЫЕ ПРОКИ =====
    public const int WeaponProcChance = 5;       // 5% шанс прока при автоатаке
    public const int DebuffTickMs = 1000;         // тик дебаффов каждую секунду

    // Длительности дебаффов (мс)
    public const int DaggerArmorPenDurationMs = 5000;
    public const int AxeDamageBonusDurationMs = 5000;
    public const int MaceDisarmDurationMs = 3000;
    public const int HammerStunDurationMs = 3000;

    // Значения дебаффов
    public const double DaggerArmorPenValue = 0.25;
    public const double AxeDamageBonusValue = 0.15;
    public const double MaceDamageReductionValue = 0.15;
    public const double HammerAccuracyReductionValue = 0.15;

    // Урон по area-of-effect (меч)
    public const double CleaveDamageFraction = 0.5;
}
