using LostAndDivine.Shared;

namespace LostAndDivine.Shared.Models;

public class Player : ICombatant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Безымянный";
    public CharacterClass Class { get; set; }

    // Базовые атрибуты класса: производные характеристики считаются от вложенных очков (выше базиса),
    // поэтому у свежего персонажа без вложений бонусы к стойкости/пробиву/точности и т.п. равны нулю.
    public int ClassBaseStrength => Class.BaseStats().Str;
    public int ClassBaseEndurance => Class.BaseStats().End;
    public int ClassBaseAgility => Class.BaseStats().Agi;
    public int ClassBaseCunning => Class.BaseStats().Cun;
    public int ClassBaseIntellect => Class.BaseStats().Int;
    public int ClassBaseWisdom => Class.BaseStats().Wis;

    /// <summary>Вложенная ловкость (выше базиса класса) — «очки» скорости атаки (шмот даёт плоский % через GetAttackSpeedGearMultiplier).</summary>
    public double GetAttackSpeedPoints()
        => Math.Max(0, Agility - ClassBaseAgility);

    /// <summary>Множитель скорости атаки от шмота: бонус «+Скор. атк %» прибавляется плоским процентом.</summary>
    public double GetAttackSpeedGearMultiplier()
        => 1.0 + Equipment.GetBonusAttackSpeed() / 100.0;
    public int X { get; set; }
    public int Y { get; set; }
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Gold { get; set; }

    // Мана (MP)
    public int Mana { get; set; } = 100;
    public int MaxMana { get; set; } = 100;

    // Кулдауны навыков: skillId -> время последнего применения (UTC) (потокобезопасная)
    public System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> LastSkillUse { get; set; } = new();

    // Очередь прекаста/боя: skillId в порядке применения (без дублей, потокобезопасная)
    public List<string> QueuedSkillIds { get; set; } = new();
    public object QueuedSkillIdsLock { get; } = new();

    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();

    // История выполненных квестов (для цепочек и условий диалогов)
    public List<string> CompletedQuestIds { get; set; } = new();

    // Время первого выполнения квеста (UTC ISO) по id — для истории в журнале
    public Dictionary<string, string> CompletedQuestTimes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Первичные атрибуты (качаются с уровнем)
    public int Strength { get; set; } = 1;   // +физ.атака, +крит урон
    public int Endurance { get; set; } = 1;  // +MaxHP, +сопротивление физ.эффектам
    public int Agility { get; set; } = 1;    // +физ.атака, +скорость атаки
    public int Cunning { get; set; } = 1;    // +шанс крита, +уклонение
    public int Intellect { get; set; } = 1;  // +маг.атака, +шанс маг.эффекта
    public int Wisdom { get; set; } = 1;     // +MaxMP, +сопротивление маг.эффектам
    public int AttributePoints { get; set; }
    public int SkillPoints { get; set; }
    public List<string> LearnedSkills { get; set; } = new();
    public Dictionary<string, int> SkillRanks { get; set; } = new(); // SkillId > ранг прокачки

    // Базовые боевые параметры (редактируются позже бонусами экипировки/умений)
    public double BaseCritChance { get; set; } = 1.0;   // %
    public double BaseCritDamage { get; set; } = 1.5;   // множитель
    public double BaseEvadeChance { get; set; } = 1.0;  // %
    public double BaseBlockChance { get; set; } = 0.0;  // %
    public double BaseParryChance { get; set; } = 0.0;  // %

    // --- Производные боевые характеристики ---

    // Эффективные атрибуты (с учётом бонусов экипировки)
    public int GetEffStrength() => Strength + Equipment.GetBonusStrength();
    public int GetEffEndurance() => Endurance + Equipment.GetBonusEndurance();
    public int GetEffAgility() => Agility + Equipment.GetBonusAgility();
    public int GetEffCunning() => Cunning + Equipment.GetBonusCunning();
    public int GetEffIntellect() => Intellect + Equipment.GetBonusIntellect();
    public int GetEffWisdom() => Wisdom + Equipment.GetBonusWisdom();

    public int GetPhysAttack()
        => GetBaseDamage() + (GetEffStrength() - 1) * BalanceStatic.AttackPerStrength
           + (GetEffAgility() - 1) * BalanceStatic.AttackPerAgility
           + Equipment.GetBonusPhysAttack();

    public int GetMagAttack()
        => GetBaseDamage() + (GetEffIntellect() - 1) * BalanceStatic.AttackPerIntellect
           + Equipment.GetBonusMagAttack();

    // Защита (физ.) и сопротивление (маг.): база от уровня (1 ед/уровень) + базовая защита брони + бонусы шмота.
    // Целочисленное значение — «валюта» для процентной защиты DR = Def/(Def+K).
    public int GetDefense()
        => GetBaseDefense() + Equipment.GetDefense() + Equipment.GetBonusDefense();

    public int GetResistance()
        => GetBaseDefense() + Equipment.GetMagicDefense() + Equipment.GetBonusResistance();

    public double GetCritChance()
        => Math.Min(BalanceStatic.MaxCritChance, BaseCritChance
            + CombatMath.ApplyCritDiminishingReturns(GetEffCunning() - 1)
            + Equipment.GetBonusCritChance());

    public double GetCritDamage()
        => Math.Min(BalanceStatic.MaxCritDamage, BaseCritDamage
            + CombatMath.ApplyCritDamageDiminishingReturns(Math.Max(0, GetEffStrength() - ClassBaseStrength))
            * BalanceStatic.CritDamagePerStrength
            + Equipment.GetBonusCritDamage() / 100.0);

    public double GetEvadeChance()
        => Math.Min(BalanceStatic.MaxEvadeChance, BaseEvadeChance
            + CombatMath.ApplyEvadeDiminishingReturns(GetEffCunning() - 1)
            + Equipment.GetBonusEvadeChance());

    /// <summary>Блок: базовый + щит (+2%) + бонус шмота (прямой процент, не более 5%), кап 25%. Атрибуты не влияют.</summary>
    public double GetBlockChance()
    {
        double shieldBase = Equipment.GetEquippedShield() != null ? 2.0 : 0.0;
        return Math.Min(BalanceStatic.MaxBlockChance, BaseBlockChance + shieldBase
            + Math.Min(BalanceStatic.MaxBlockGearBonus, Equipment.GetBonusBlockChance()));
    }

    /// <summary>Парирование: базовый + бонус шмота (прямой процент, не более 5%) + навык «Рефлексы», кап 25%. Атрибуты не влияют.</summary>
    public double GetParryChance()
        => Math.Min(BalanceStatic.MaxParryChance, BaseParryChance
            + Math.Min(BalanceStatic.MaxParryGearBonus, Equipment.GetBonusParryChance())
            + GetReflexesParryBonus());

    // Пассивный навык «Рефлексы» (SK0008): +10% шанс парирования при двух одноручных оружиях.
    public double GetReflexesParryBonus()
    {
        if (!LearnedSkills.Contains(SkillIds.Reflexes)) return 0;
        return Equipment.IsDualWielding() ? 10.0 * GetPassiveRankMult(SkillIds.Reflexes) : 0.0;
    }

    // Пассивный навык «Амбидекстр» (SK0003): доля урона левой руки от правой.
    // Ранг 1: +25% (итого 75%), ранг 2: +40% (90%), ранг 3: +50% (100% — как правая рука).
    public double GetOffHandDamageFraction()
    {
        if (!LearnedSkills.Contains(SkillIds.Ambidextrous)) return Equipment.OffHandDamageFraction;
        double bonus = GetSkillRank(SkillIds.Ambidextrous) switch
        {
            3 => 0.50,
            2 => 0.40,
            _ => 0.25
        };
        return Math.Min(1.0, Equipment.OffHandDamageFraction + bonus);
    }

    // Максимальная атака для удара второй рукой (аналог GetTotalAttack, но по оффхенд-оружию).
    public int GetOffHandTotalAttack(int dist)
    {
        var offHand = Equipment.GetOffHandWeapon();
        bool useMag = offHand != null && Equipment.IsCasterWeapon(offHand);
        return (int)(((useMag ? GetMagAttack() : GetPhysAttack()) + Equipment.GetOffHandMaxDamage())
            * GetBerserkMultiplier());
    }

    public int GetBlockValue()
        => (int)(Equipment.GetShieldBonusDefense() * BalanceStatic.ShieldBlockValueMultiplier);

    private bool IsUsingStaff() => Equipment.GetWeaponCategory() == WeaponCategory.Staff;

    private bool UsesMagicAttack(int dist)
        => Equipment.GetWeaponCategory() is WeaponCategory.Staff or WeaponCategory.Grimoire or WeaponCategory.Sphere;

    /// <summary>Магическая ли текущая атака (по категории оружия).</summary>
    public bool IsMagicalDamage() => UsesMagicAttack(1);

    /// <summary>Магическая ли атака оффхенд-оружия.</summary>
    public bool IsOffHandMagical() => Equipment.GetOffHandWeapon() is { } oh && Equipment.IsCasterWeapon(oh);

    // Совместимость с ICombatant (физ. атака/защита) — без расстояния (ближний бой по умолчанию)
    public int GetBaseDamage() => 1 + (Level - 1);
    public int GetBaseDefense() => 1 + (Level - 1);
    public int GetTotalAttack() => GetTotalAttack(1);
    public int GetTotalDefense() => GetDefense();
    public int GetTotalResistance() => GetResistance();
    public int RollAttackDamage() => RollAttackDamage(1);
    public int RollOffHandDamage()
    {
        var offHand = Equipment.GetOffHandWeapon();
        if (offHand != null && Equipment.IsCasterWeapon(offHand))
            return GetMagAttack() + Equipment.RollOffHandDamage();
        return GetPhysAttack() + Equipment.RollOffHandDamage();
    }
    public int GetMaxAttackDamage() => GetMaxAttackDamage(1);

    public int GetTotalAttack(int dist)
        => (int)(((UsesMagicAttack(dist) ? GetMagAttack() : GetPhysAttack()) + Equipment.GetWeaponMaxDamage()) * GetBerserkMultiplier());

    // «Берсерк» (SK0011): +2% урона за каждые 5% потерянного здоровья.
    public double GetBerserkMultiplier()
    {
        if (!LearnedSkills.Contains(SkillIds.Berserk)) return 1.0;
        int maxHp = MaxHealth + Equipment.GetBonusMaxHealth();
        if (maxHp <= 0) return 1.0;
        double percentMissing = (maxHp - Health) / (double)maxHp * 100.0;
        return 1.0 + BalanceStatic.BerserkDamagePer5Percent * GetPassiveRankMult(SkillIds.Berserk) * (percentMissing / 5.0);
    }

    // ----- Ранги навыков -----

    public int GetSkillRank(string skillId) => SkillRanks.TryGetValue(skillId, out int r) ? r : 1;

    /// <summary>Множитель урона активного навыка от ранга (+12% за ранг выше 1-го).</summary>
    public double GetSkillRankDmgMult(string skillId) => 1.0 + (GetSkillRank(skillId) - 1) * 0.12;

    /// <summary>Множитель кулдауна от ранга (–8% за ранг) и от мудрости (сокращение отката, кап 50%).</summary>
    public double GetSkillRankCdMult(string skillId)
        => Math.Max(0.5, (1.0 - (GetSkillRank(skillId) - 1) * 0.08) * (1.0 - GetCooldownReduction() / 100.0));

    /// <summary>Стойкость: снижает шанс крита противника по вам. Кап 50%, от вложенной выносливости (выше базиса класса) + шмот.</summary>
    public double GetTenacity()
        => Math.Min(BalanceStatic.MaxTenacity,
            CombatMath.ApplyTenacityDiminishingReturns(Math.Max(0, GetEffEndurance() - ClassBaseEndurance))
            + Equipment.GetBonusTenacity());

    /// <summary>Пробивание брони: игнорирует % защиты цели. Кап 25%, от вложенной силы (выше базиса класса) + плоский % шмота.</summary>
    public double GetArmorPenetration()
        => Math.Min(BalanceStatic.MaxArmorPenetration,
            CombatMath.ApplyArmorPenDiminishingReturns(Math.Max(0, GetEffStrength() - ClassBaseStrength))
            + Equipment.GetBonusArmorPenetration());

    /// <summary>Сокращение перезарядки навыков. Кап 50%, от вложенной мудрости (выше базиса класса) + плоский % шмота.</summary>
    public double GetCooldownReduction()
        => Math.Min(BalanceStatic.MaxCooldownReduction,
            CombatMath.ApplyCdrDiminishingReturns(Math.Max(0, GetEffWisdom() - ClassBaseWisdom))
            + Equipment.GetBonusCooldownReduction());

    /// <summary>Регенерация здоровья: +X% к количеству восстанавливаемого HP. Кап 25%, от вложенной выносливости (выше базиса класса) + плоский % шмота.</summary>
    public double GetHealthRegenPercent()
        => Math.Min(BalanceStatic.MaxHealthRegen,
            CombatMath.ApplyHealthRegenDiminishingReturns(Math.Max(0, GetEffEndurance() - ClassBaseEndurance))
            + Equipment.GetBonusHpRegen());

    /// <summary>Регенерация маны: +X% к количеству восстанавливаемой MP. Кап 20%, от вложенной мудрости (выше базиса класса) + плоский % шмота.</summary>
    public double GetManaRegenPercent()
        => Math.Min(BalanceStatic.MaxManaRegen,
            CombatMath.ApplyManaRegenDiminishingReturns(Math.Max(0, GetEffWisdom() - ClassBaseWisdom))
            + Equipment.GetBonusMpRegen());

    /// <summary>Множитель пассивного навыка от ранга (+33% за ранг).</summary>
    public double GetPassiveRankMult(string skillId) => 1.0 + (GetSkillRank(skillId) - 1) * 0.33;

    public bool IsWieldingBow()
        => Equipment.IsBowEquipped();

    public int GetEffectiveAttackRange()
        => Equipment.GetWeaponAttackRange() + GetBowRangeBonus();

    /// <summary>«Вам подарочек» (SK0017): шанс доп. стрелы.</summary>
    public double GetExtraArrowChance()
    {
        if (!LearnedSkills.Contains(SkillIds.ExtraArrow) || !IsWieldingBow()) return 0;
        return BalanceStatic.ExtraArrowChance * GetPassiveRankMult(SkillIds.ExtraArrow);
    }

    /// <summary>Точность: база 100% + убывающая отдача от вложенной ловкости (выше базиса класса) + шмот + бонус «Белке в глаз» (лук), кап 150%.</summary>
    public double GetAccuracy()
        => Math.Min(BalanceStatic.AccuracyMax, BalanceStatic.AccuracyBase
            + CombatMath.ApplyAccuracyDiminishingReturns(Math.Max(0, GetEffAgility() - ClassBaseAgility))
            + Equipment.GetBonusAccuracy()
            + GetBowAccuracyBonus());

    /// <summary>«Белке в глаз» (SK0018): бонус точности (вычитается из уклона цели).</summary>
    public double GetBowAccuracyBonus()
    {
        if (!LearnedSkills.Contains(SkillIds.BowAccuracy) || !IsWieldingBow()) return 0;
        return BalanceStatic.BowAccuracyBonus * GetPassiveRankMult(SkillIds.BowAccuracy);
    }

    /// <summary>«Руками не трогать» (SK0019): +уклон против ближнего боя.</summary>
    public double GetMeleeEvadeBonus()
    {
        if (!LearnedSkills.Contains(SkillIds.MeleeEvade)) return 0;
        return BalanceStatic.MeleeEvadeBonus * GetPassiveRankMult(SkillIds.MeleeEvade);
    }

    /// <summary>«Дальний прицел» (SK0020): бонус дальности лука.</summary>
    public int GetBowRangeBonus()
        => LearnedSkills.Contains(SkillIds.LongRangeSight) && IsWieldingBow() ? BalanceStatic.BowRangeBonus : 0;

    /// <summary>«Дальний прицел»: пробитие брони чем ближе цель (дист ? 2).</summary>
    public double GetCloseRangeArmorPen(int dist)
    {
        if (!LearnedSkills.Contains(SkillIds.LongRangeSight) || !IsWieldingBow()) return 0;
        if (dist > BalanceStatic.CloseRangeArmorPenDist) return 0;
        double t = 1.0 - (dist - 1) / (double)BalanceStatic.CloseRangeArmorPenDist;
        if (dist <= 1) t = 1.0;
        return BalanceStatic.CloseRangeArmorPenMax * GetPassiveRankMult(SkillIds.LongRangeSight) * Math.Clamp(t, 0, 1);
    }

    /// <summary>«Охотничий инстинкт» (SK0021): бонус крита по ослабленным целям.</summary>
    public double GetHunterInstinctCritBonus(ICombatant target)
    {
        if (!LearnedSkills.Contains(SkillIds.HuntingInstinct) || !IsWieldingBow()) return 0;
        bool marked = target switch
        {
            Player p => p.GetDebuffsSnapshot().Any(d => d.Type is DebuffType.Root or DebuffType.Slow or DebuffType.AccuracyReduction),
            Monster m => m.GetDebuffsSnapshot().Any(d => d.Type is DebuffType.Root or DebuffType.Slow or DebuffType.AccuracyReduction),
            _ => false
        };
        return marked ? BalanceStatic.HunterInstinctCritBonus * GetPassiveRankMult(SkillIds.HuntingInstinct) : 0;
    }

    public int RollAttackDamage(int dist)
        => (UsesMagicAttack(dist) ? GetMagAttack() : GetPhysAttack()) + Equipment.RollWeaponDamage();

    public int GetMaxAttackDamage(int dist)
        => (UsesMagicAttack(dist) ? GetMagAttack() : GetPhysAttack()) + Equipment.GetWeaponMaxDamage();

    public int Speed { get; set; } = 1;   // определяет интервал перемещения

    // Регенерация
    public DateTime LastDamagedTime { get; set; } = DateTime.MinValue;
    public DateTime LastRegenTime { get; set; } = DateTime.MinValue;

    // Компоненты состояний
    public MovementState Movement { get; set; } = new();
    public CombatState Combat { get; set; } = new();
    public InteractionState Interaction { get; set; } = new();
    public DialogueState Dialogue { get; set; } = new();

    // Направление взгляда (для cleave и т.д.)
    public string Facing { get; set; } = "down";

    // Активные дебаффы (потокобезопасный доступ через DebuffsLock)
    public List<ActiveDebuff> ActiveDebuffs { get; set; } = new();
    public object DebuffsLock { get; } = new();

    /// <summary>Возвращает снимок списка дебаффов (потокобезопасно).</summary>
    public List<ActiveDebuff> GetDebuffsSnapshot()
    {
        lock (DebuffsLock) return new List<ActiveDebuff>(ActiveDebuffs);
    }

    // Панель быстрого доступа (10 слотов, хранятся ID предметов)
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };

    public List<Item> BuybackItems { get; set; } = new();

    // Пати
    public Guid? PartyId { get; set; }

    // Обмен
    public bool IsTrading { get; set; }

    // Администрирование
    public bool IsAdmin { get; set; }

    // Зона
    public string CurrentZoneId { get; set; } = BalanceStatic.MainZoneId;

    // Смерть: флаг + время (для задержки 5с перед респауном)
    public bool IsDead { get; set; }
    public DateTime DeathTime { get; set; }

    /// <summary>
    /// Проверяет, достаточно ли опыта для повышения уровня.
    /// Если да — повышает уровень, возвращает true.
    /// </summary>
    public bool TryLevelUp()
    {
        bool leveled = false;
        while (Level < BalanceStatic.MaxLevel)
        {
            int needed = BalanceStatic.XpNeededForNextLevel(Level);
            if (Experience < needed) break;
            Level++;
            Experience -= needed;
            MaxHealth += BalanceStatic.MaxHealthPerLevel;
            Health = MaxHealth;
            AttributePoints += BalanceStatic.AttributePointsPerLevel;
            if (Level % 2 == 0)
                SkillPoints++;
            leveled = true;
        }
        return leveled;
    }
}
