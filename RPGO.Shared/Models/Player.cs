namespace RPGGame.Shared.Models;

public class Player : ICombatant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Незнакомец";
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

    // Кулдауны навыков: skillId -> время последнего применения (UTC)
    public Dictionary<string, DateTime> LastSkillUse { get; set; } = new();

    // Очередь прекаста/боя: skillId в порядке применения (без дублей)
    public List<string> QueuedSkillIds { get; set; } = new();

    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();

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

    public int GetDefense()
        => GetBaseDefense() + (GetEffEndurance() - 1) * BalanceStatic.DefensePerEndurance
           + Equipment.GetBonusDefense();

    public int GetResistance()
        => GetBaseDefense() + (GetEffWisdom() - 1) * BalanceStatic.ResistancePerWisdom
           + Equipment.GetBonusResistance();

    public double GetCritChance()
        => BaseCritChance + (GetEffCunning() - 1) * BalanceStatic.CritChancePerCunning
           + Equipment.GetBonusCritChance();

    public double GetCritDamage()
        => BaseCritDamage + (GetEffStrength() - 1) * BalanceStatic.CritDamagePerStrength
           + Equipment.GetBonusCritDamage();

    public double GetEvadeChance()
        => BaseEvadeChance + (GetEffCunning() - 1) * BalanceStatic.EvadeChancePerCunning
           + Equipment.GetBonusEvadeChance();

    public double GetBlockChance()
    {
        double shieldBase = Equipment.GetEquippedShield() != null ? 2.0 : 0.0;
        return BaseBlockChance + shieldBase
            + (GetEffEndurance() - 1) * BalanceStatic.BlockChancePerEndurance
            + Equipment.GetBonusBlockChance();
    }

    public double GetParryChance()
        => BaseParryChance + (GetEffAgility() - 1) * BalanceStatic.ParryChancePerAgility
           + Equipment.GetBonusParryChance();

    public int GetBlockValue()
        => (int)(Equipment.GetShieldBonusDefense() * BalanceStatic.ShieldBlockValueMultiplier);

    private bool IsUsingStaff() => Equipment.GetWeaponSubtype() == "staff";

    private bool UsesMagicAttack(int dist)
    {
        string sub = Equipment.GetWeaponSubtype();
        if (sub == "staff" || sub == "grimoire" || sub == "sphere") return true;
        return false;
    }

    // Совместимость с ICombatant (физ. атака/защита) — без расстояния (ближний бой по умолчанию)
    public int GetBaseDamage() => 1 + (Level - 1);
    public int GetBaseDefense() => 1 + (Level - 1);
    public int GetTotalAttack() => GetTotalAttack(1);
    public int GetTotalDefense() => GetDefense();
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
        => (UsesMagicAttack(dist) ? GetMagAttack() : GetPhysAttack()) + Equipment.GetWeaponMaxDamage();

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

    // Активные дебаффы
    public List<ActiveDebuff> ActiveDebuffs { get; set; } = new();

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
    public string CurrentZoneId { get; set; } = "main";

    // Смерть: флаг + время (для задержки 5с перед респауном)
    public bool IsDead { get; set; }
    public DateTime DeathTime { get; set; }

    /// <summary>
    /// Проверяет, достаточно ли опыта для повышения уровня.
    /// Если да — повышает уровень, возвращает true.
    /// </summary>
    public bool TryLevelUp()
    {
        if (Level >= BalanceStatic.MaxLevel) return false;
        int needed = BalanceStatic.XpNeededForNextLevel(Level);
        if (Experience < needed) return false;
        Level++;
        Experience -= needed;
        MaxHealth += BalanceStatic.MaxHealthPerLevel;
        Health = MaxHealth;
        AttributePoints += BalanceStatic.AttributePointsPerLevel;
        if (Level % 2 == 0)
            SkillPoints++;
        return true;
    }
}
