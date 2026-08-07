using LostAndDivine.Shared;

namespace LostAndDivine.Shared.Models;

public class Player : ICombatant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Безымянный";
    public CharacterClass Class { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Gold { get; set; }

    // ���� (MP)
    public int Mana { get; set; } = 100;
    public int MaxMana { get; set; } = 100;

    // �������� �������: skillId -> ����� ���������� ���������� (UTC) (����������������)
    public System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> LastSkillUse { get; set; } = new();

    // ������� ��������/���: skillId � ������� ���������� (��� ������, ����������������)
    public List<string> QueuedSkillIds { get; set; } = new();
    public object QueuedSkillIdsLock { get; } = new();

    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();

    // ������� ����������� ������� (��� ������� � ������� ��������)
    public List<string> CompletedQuestIds { get; set; } = new();

    // ��������� �������� (�������� � �������)
    public int Strength { get; set; } = 1;   // +���.�����, +���� ����
    public int Endurance { get; set; } = 1;  // +MaxHP, +������������� ���.��������
    public int Agility { get; set; } = 1;    // +���.�����, +�������� �����
    public int Cunning { get; set; } = 1;    // +���� �����, +���������
    public int Intellect { get; set; } = 1;  // +���.�����, +���� ���.�������
    public int Wisdom { get; set; } = 1;     // +MaxMP, +������������� ���.��������
    public int AttributePoints { get; set; }
    public int SkillPoints { get; set; }
    public List<string> LearnedSkills { get; set; } = new();
    public Dictionary<string, int> SkillRanks { get; set; } = new(); // SkillId > ���� ��������

    // ������� ������ ��������� (������������� ����� �������� ����������/������)
    public double BaseCritChance { get; set; } = 1.0;   // %
    public double BaseCritDamage { get; set; } = 1.5;   // ���������
    public double BaseEvadeChance { get; set; } = 1.0;  // %
    public double BaseBlockChance { get; set; } = 0.0;  // %
    public double BaseParryChance { get; set; } = 0.0;  // %

    // --- ����������� ������ �������������� ---

    // ����������� �������� (� ������ ������� ����������)
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
           + Equipment.GetBonusParryChance()
           + GetReflexesParryBonus();

    // ��������� ����� ���������� (SK0008): +10% ���� ����������� ��� ���� ���������� �������.
    public double GetReflexesParryBonus()
    {
        if (!LearnedSkills.Contains(SkillIds.Reflexes)) return 0;
        return Equipment.IsDualWielding() ? 10.0 * GetPassiveRankMult(SkillIds.Reflexes) : 0.0;
    }

    // ��������� ����� ����������� (SK0003): ���� ����� ����� ���� �� ������.
    // ���� 1: +25% (����� 75%), ���� 2: +40% (90%), ���� 3: +50% (100% � ��� ������ ����).
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

    // ������������ ����� ��� ����� ������ ����� (������ GetTotalAttack, �� �� �������-������).
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

    // ������������� � ICombatant (���. �����/������) � ��� ���������� (������� ��� �� ���������)
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
        => (int)(((UsesMagicAttack(dist) ? GetMagAttack() : GetPhysAttack()) + Equipment.GetWeaponMaxDamage()) * GetBerserkMultiplier());

    // �������� (SK0011): +2% ����� �� ������ 5% ����������� ��������.
    public double GetBerserkMultiplier()
    {
        if (!LearnedSkills.Contains(SkillIds.Berserk)) return 1.0;
        int maxHp = MaxHealth + Equipment.GetBonusMaxHealth();
        if (maxHp <= 0) return 1.0;
        double percentMissing = (maxHp - Health) / (double)maxHp * 100.0;
        return 1.0 + BalanceStatic.BerserkDamagePer5Percent * GetPassiveRankMult(SkillIds.Berserk) * (percentMissing / 5.0);
    }

    // ----- ����� ������� -----

    public int GetSkillRank(string skillId) => SkillRanks.TryGetValue(skillId, out int r) ? r : 1;

    /// <summary>��������� ����� ��������� ������ �� ����� (+12% �� ���� ���� 1-��).</summary>
    public double GetSkillRankDmgMult(string skillId) => 1.0 + (GetSkillRank(skillId) - 1) * 0.12;

    /// <summary>��������� �������� �� ����� (�8% �� ����).</summary>
    public double GetSkillRankCdMult(string skillId) => 1.0 - (GetSkillRank(skillId) - 1) * 0.08;

    /// <summary>��������� ���������� ������ �� ����� (+33% �� ����).</summary>
    public double GetPassiveRankMult(string skillId) => 1.0 + (GetSkillRank(skillId) - 1) * 0.33;

    public bool IsWieldingBow()
        => Equipment.IsBowEquipped();

    public int GetEffectiveAttackRange()
        => Equipment.GetWeaponAttackRange() + GetBowRangeBonus();

    /// <summary>���� ��������� (SK0017): ���� ���. ������.</summary>
    public double GetExtraArrowChance()
    {
        if (!LearnedSkills.Contains(SkillIds.ExtraArrow) || !IsWieldingBow()) return 0;
        return BalanceStatic.ExtraArrowChance * GetPassiveRankMult(SkillIds.ExtraArrow);
    }

    /// <summary>������ � ���� (SK0018): ����� �������� (���������� �� ������ ����).</summary>
    public double GetBowAccuracyBonus()
    {
        if (!LearnedSkills.Contains(SkillIds.BowAccuracy) || !IsWieldingBow()) return 0;
        return BalanceStatic.BowAccuracyBonus * GetPassiveRankMult(SkillIds.BowAccuracy);
    }

    /// <summary>������� �� �������� (SK0019): +����� ������ �������� ���.</summary>
    public double GetMeleeEvadeBonus()
    {
        if (!LearnedSkills.Contains(SkillIds.MeleeEvade)) return 0;
        return BalanceStatic.MeleeEvadeBonus * GetPassiveRankMult(SkillIds.MeleeEvade);
    }

    /// <summary>�������� ������ (SK0020): ����� ��������� ����.</summary>
    public int GetBowRangeBonus()
        => LearnedSkills.Contains(SkillIds.LongRangeSight) && IsWieldingBow() ? BalanceStatic.BowRangeBonus : 0;

    /// <summary>�������� ������: �������� ����� ��� ����� ���� (���� ? 2).</summary>
    public double GetCloseRangeArmorPen(int dist)
    {
        if (!LearnedSkills.Contains(SkillIds.LongRangeSight) || !IsWieldingBow()) return 0;
        if (dist > BalanceStatic.CloseRangeArmorPenDist) return 0;
        double t = 1.0 - (dist - 1) / (double)BalanceStatic.CloseRangeArmorPenDist;
        if (dist <= 1) t = 1.0;
        return BalanceStatic.CloseRangeArmorPenMax * GetPassiveRankMult(SkillIds.LongRangeSight) * Math.Clamp(t, 0, 1);
    }

    /// <summary>���������� �������� (SK0021): ����� ����� �� ����������� �����.</summary>
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

    public int Speed { get; set; } = 1;   // ���������� �������� �����������

    // �����������
    public DateTime LastDamagedTime { get; set; } = DateTime.MinValue;
    public DateTime LastRegenTime { get; set; } = DateTime.MinValue;

    // ���������� ���������
    public MovementState Movement { get; set; } = new();
    public CombatState Combat { get; set; } = new();
    public InteractionState Interaction { get; set; } = new();
    public DialogueState Dialogue { get; set; } = new();

    // ����������� ������� (��� cleave � �.�.)
    public string Facing { get; set; } = "down";

    // �������� ������� (���������������� ������ ����� DebuffsLock)
    public List<ActiveDebuff> ActiveDebuffs { get; set; } = new();
    public object DebuffsLock { get; } = new();

    /// <summary>���������� ������ ������ �������� (���������������).</summary>
    public List<ActiveDebuff> GetDebuffsSnapshot()
    {
        lock (DebuffsLock) return new List<ActiveDebuff>(ActiveDebuffs);
    }

    // ������ �������� ������� (10 ������, �������� ID ���������)
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };

    public List<Item> BuybackItems { get; set; } = new();

    // ����
    public Guid? PartyId { get; set; }

    // �����
    public bool IsTrading { get; set; }

    // �����������������
    public bool IsAdmin { get; set; }

    // ����
    public string CurrentZoneId { get; set; } = BalanceStatic.MainZoneId;

    // ������: ���� + ����� (��� �������� 5� ����� ���������)
    public bool IsDead { get; set; }
    public DateTime DeathTime { get; set; }

    /// <summary>
    /// ���������, ���������� �� ����� ��� ��������� ������.
    /// ���� �� � �������� �������, ���������� true.
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
