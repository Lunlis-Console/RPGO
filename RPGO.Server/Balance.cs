namespace RPGGame.Server;

/// <summary>
/// Единая точка всех магических чисел и игрового баланса сервера.
/// Логика формул остаётся в коде (типобезопасно, refactor-friendly),
/// здесь только значения, которые меняются при балансировке.
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

    // Радиус взаимодействия с сущностями (в клетках, Manhattan)
    public const int InteractRange = 1;
    // Дистанция атаки в бою (Manhattan)
    public const int AttackRange = 1;

    // Точка респавна игрока у торговца (джиттер ±)
    public const int RespawnJitterMin = -3;
    public const int RespawnJitterMax = 4;

    // ===== МАНЕКЕН =====
    public const int MannequinHealth = 10000;
    public const int MannequinRegenDelayMs = 5000;   // через 5 сек без ударов — полное восстановление
    public const int MannequinOffsetX = -4;           // смещение от торговца
    public const int MannequinOffsetY = -2;

    // ===== ДВИЖЕНИЕ =====
    // Базовый интервал движения: MoveIntervalMs = BaseMoveMs / max(1, Speed)
    public const double BaseMoveMs = 500.0;
    // Множитель интервала атаки: AttackIntervalMs = AttackBaseMs / max(1, AttackSpeed)
    public const double AttackBaseMs = 1000.0;

    // ===== БОЙ: ПРОИЗВОДНЫЕ ХАРАКТЕРИСТИКИ =====
    // Формулы в Player.cs / Monster.cs используют эти коэффициенты.
    public const int BaseDamagePerLevel = 1;        // 1 + (Level-1)
    public const int BaseDefensePerLevel = 1;       // 1 + (Level-1)
    public const int AttackPerStrength = 2;          // (Str-1) * 2
    public const int DefensePerStamina = 1;         // (Stam-1) * 1
    public const double CritChancePerAgility = 1.0; // (Agi-1) * 1.0 %
    public const double CritDamagePerStrength = 0.05;// (Str-1) * 0.05
    public const double EvadeChancePerAgility = 1.0; // (Agi-1) * 1.0 %

    // Базовые боевые множители (Player / Monster)
    public const double BaseCritChance = 1.0;    // %
    public const double BaseCritDamage = 1.5;    // множитель
    public const double BaseEvadeChance = 1.0;   // %

    // Скорость атаки: GetAttackSpeed = max(1, Base + Agility / AgilityDivisor)
    public const int AttackSpeedBase = 1;
    public const int AttackSpeedAgilityDivisor = 5;

    // Минимальный урон (после защиты)
    public const int MinDamage = 1;
    // Порог для rng.Next(100) в проверках шанса
    public const int ChanceRollMax = 100;

    // ===== ОПЫТ / УРОВНИ =====
    public const int XpPerLevel = 50;            // xpNeeded = Level * 50
    public const int MaxHealthPerLevel = 10;     // +10 HP за уровень
    public const int AttributePointsPerLevel = 3;// +3 очка за уровень
    public const int MaxHealthPerStamina = 5;    // +5 HP за очко Stamina

    // ===== СМЕРТЬ =====
    public const double DeathHealthFraction = 0.5; // HP после смерти = 50% MaxHP
    public const int DeathGoldLoss = 20;           // макс. потеря золота

    // ===== DUAL WIELD =====
    public const double DualWieldSpeedBonus = 1.15;   // +15% к скорости атаки при dual wield
    public const double OffHandDamageFraction = 0.5;   // off-hand наносит 50% урона
    public const int OffHandDelayMs = 250;             // задержка перед ударом второй руки (мс)

    // ===== РЕГЕНЕРАЦИЯ (игрок) =====
    public const int PlayerRegenInCombatDelayMs = 4000;
    public const int PlayerRegenOutOfCombatHeal = 5;
    public const int PlayerRegenOutOfCombatTickMs = 5000;
    public const double PlayerRegenInCombatFraction = 0.015;
    public const int PlayerRegenInCombatTickMs = 4000;
    public const int PlayerRegenMinHeal = 1;

    // ===== МАНА (MP) =====
    public const int ManaPerWill = 5;            // +5 MP за ед. Will
    public const int ManaBase = 20;              // базовый запас
    public const int ManaRegenOutOfCombat = 2;   // MP/тик вне боя
    public const int ManaRegenOutOfCombatTickMs = 2000;
    public const double ManaRegenInCombatFraction = 0.01; // доля MaxMana/тик в бою
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

    // ===== МОНСТРЫ: СПАВН / ПОВЕДЕНИЕ =====
    public const int MonsterSpawnCount = 120;
    public const int SpawnMaxAttempts = 200;
    public const double SpawnDifficultyPerDist = 0.015; // mult = 1 + dist*0.015
    public const int SpawnSafeRadiusFromMerchant = 6;   // не спавнить ближе
    public const int MonsterWanderRadius = 5;
    public const int MonsterAggroRange = 5;
    public const int MonsterWanderSkipChance = 40;      // % пропуска шага
    public const int MonsterMoveMinMs = 800;
    public const int MonsterMoveMaxMs = 2500;
    public const int MonsterSpawnJitterMaxMs = 2000;

    // Пересчёт атрибутов монстра из atk/def/tier (сида БД)
    public const int MonsterAttrBase = 1;
    public const int MonsterAttrDivisor = 2;            // (atk - baseDmg) / 2
    public const int MonsterAgilityPerTier = 1;

    // Таблица tier по дистанции от торговца
    public static int MonsterTierByDistance(int dist) => dist switch
    {
        <= 15 => 1,
        <= 25 => 2,
        <= 35 => 3,
        _     => 4
    };

    // ===== ЛУТ / ДРОП =====
    public const int LootDropChance = 40; // %
    public const int CollectibleValue = 2; // базовая ценность собранного ресурса

    // ===== ИНВЕНТАРЬ / HOTBAR =====
    public const int HotbarSlots = 10;
    public const int DefaultMaxStack = 10;   // для расходников/ресурсов
    public const int UniqueItemMaxStack = 1; // для оружия/брони/аксессуаров

    public static int MaxStackForType(string? type)
        => type == "consumable" || type == "collectible" || type == "trophy" || type == "material"
            ? DefaultMaxStack : UniqueItemMaxStack;

    // ===== ТОРГОВЛЯ =====
    public const int MaxDiscountPct = 30;
    public const int DiscountPerCunning = 2;   // +2% за Cunning
    public const double BuybackFraction = 0.5; // выкуп = 50% стоимости
    public const double SellFraction = 0.5;    // продажа = 50% стоимости

    // ===== ЦИКЛЫ СЕРВЕРА (интервалы tick) =====
    public const int LoopMonsterWanderMs = 1500;
    public const int LoopMovePathMs = 200;
    public const int LoopCombatMs = 200;
    public const int LoopMonsterAttackMs = 500;

    // ===== ФОРМУЛЫ (вызываются из логики сервера) =====

    public static int MoveIntervalMs(int speed)
        => (int)(BaseMoveMs / Math.Max(1, speed));

    public static int AttackIntervalMs(int attackSpeed)
        => (int)(AttackBaseMs / Math.Max(1, attackSpeed));

    /// <summary>
    /// Интервал атаки с точным учётом модификатора скорости оружия (без округления в int).
    /// baseAgilitySpeed: результат GetAttackSpeed(agility).
    /// weaponSpeedMod: модификатор оружия (0.5 = молот, 1.3 = кинжал).
    /// </summary>
    public static int AttackIntervalMs(int baseAgilitySpeed, double weaponSpeedMod)
        => (int)(AttackBaseMs / Math.Max(0.1, baseAgilitySpeed * Math.Max(0.1, weaponSpeedMod)));

    public static int GetAttackSpeed(int agility)
        => Math.Max(1, AttackSpeedBase + agility / AttackSpeedAgilityDivisor);

    /// <summary>
    /// Скорость атаки с учётом модификатора оружия.
    /// weaponSpeedMod: 1.0 = без оружия, 1.3 = кинжалы, 0.8 = топор, 0.6 = булава, 0.5 = молот.
    /// </summary>
    public static int GetAttackSpeedWithWeapon(int agility, double weaponSpeedMod)
    {
        int baseSpeed = GetAttackSpeed(agility);
        double effective = baseSpeed * Math.Max(0.1, weaponSpeedMod);
        return Math.Max(1, (int)Math.Round(effective));
    }

    public static int XpNeededForNextLevel(int level)
        => level * XpPerLevel;

    public static int ShopDiscountPct(int cunning)
        => Math.Min(MaxDiscountPct, cunning * DiscountPerCunning);

    public static int BuyPrice(int baseValue, int discountPct)
        => Math.Max(1, baseValue - baseValue * discountPct / ChanceRollMax);

    public static int SellPrice(int baseValue)
        => Math.Max(1, (int)(baseValue * SellFraction));

    public static int BuybackPrice(int baseValue)
        => Math.Max(1, (int)(baseValue * BuybackFraction));

    public static int ComputeDeathGoldLoss(int gold)
        => Math.Min(gold, DeathGoldLoss);

    public static int RespawnHealth(int maxHealth)
        => (int)(maxHealth * DeathHealthFraction);

    // ===== МАНА =====
    public static int MaxMana(int will)
        => ManaBase + Math.Max(0, will - 1) * ManaPerWill;
}
