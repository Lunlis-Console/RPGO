using LostAndDivine.Shared;

namespace LostAndDivine.Shared.Models;

public class Player : ICombatant
{
    // Компоненты, вынесенные из God-Class (SRP)
    private readonly PlayerAttributes _attributes = new();
    private readonly PlayerProgression _progression = new();
    private readonly PlayerCombatStats _combatStats;

    public Player()
    {
        _combatStats = new PlayerCombatStats(this);
    }

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Безымянный";
    public CharacterClass Class { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public double AdminDamageMultiplier { get; set; } = 1.0;
    public int Mana { get; set; } = 100;
    public int MaxMana { get; set; } = 100;

    public System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> LastSkillUse { get; set; } = new();
    public List<string> QueuedSkillIds { get; set; } = new();
    public object QueuedSkillIdsLock { get; } = new();

    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();
    public List<string> CompletedQuestIds { get; set; } = new();
    public Dictionary<string, string> CompletedQuestTimes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Атрибуты — делегируем в PlayerAttributes (сохранён публичный API)
    public int Strength { get => _attributes.Strength; set => _attributes.Strength = value; }
    public int Endurance { get => _attributes.Endurance; set => _attributes.Endurance = value; }
    public int Agility { get => _attributes.Agility; set => _attributes.Agility = value; }
    public int Cunning { get => _attributes.Cunning; set => _attributes.Cunning = value; }
    public int Intellect { get => _attributes.Intellect; set => _attributes.Intellect = value; }
    public int Wisdom { get => _attributes.Wisdom; set => _attributes.Wisdom = value; }
    public int AttributePoints { get => _attributes.AttributePoints; set => _attributes.AttributePoints = value; }

    // Прогрессия — делегируем в PlayerProgression
    public int Level { get => _progression.Level; set => _progression.Level = value; }
    public int Experience { get => _progression.Experience; set => _progression.Experience = value; }
    public int Gold { get => _progression.Gold; set => _progression.Gold = value; }
    public int SkillPoints { get => _progression.SkillPoints; set => _progression.SkillPoints = value; }
    public List<string> LearnedSkills { get => _progression.LearnedSkills; set => _progression.LearnedSkills = value; }
    public Dictionary<string, int> SkillRanks { get => _progression.SkillRanks; set => _progression.SkillRanks = value; }

    // Доступ к компонентам для новой логики (JsonIgnore чтобы не ломать сериализацию)
    [System.Text.Json.Serialization.JsonIgnore]
    public PlayerAttributes Attributes => _attributes;
    [System.Text.Json.Serialization.JsonIgnore]
    public PlayerProgression Progression => _progression;
    [System.Text.Json.Serialization.JsonIgnore]
    public PlayerCombatStats CombatStats => _combatStats;

    public double BaseCritChance { get; set; } = 1.0;
    public double BaseCritDamage { get; set; } = 1.5;
    public double BaseEvadeChance { get; set; } = 1.0;
    public double BaseBlockChance { get; set; } = 0.0;
    public double BaseParryChance { get; set; } = 0.0;

    // Фасад боевым расчётам
    public double GetAttackSpeedPoints() => _combatStats.GetAttackSpeedPoints();
    public double GetAttackSpeedGearMultiplier() => _combatStats.GetAttackSpeedGearMultiplier();
    public int GetEffStrength() => _combatStats.GetEffStrength();
    public int GetEffEndurance() => _combatStats.GetEffEndurance();
    public int GetEffAgility() => _combatStats.GetEffAgility();
    public int GetEffCunning() => _combatStats.GetEffCunning();
    public int GetEffIntellect() => _combatStats.GetEffIntellect();
    public int GetEffWisdom() => _combatStats.GetEffWisdom();
    public int GetPhysAttack() => _combatStats.GetPhysAttack();
    public int GetMagAttack() => _combatStats.GetMagAttack();
    public int GetDefense() => _combatStats.GetDefense();
    public int GetResistance() => _combatStats.GetResistance();
    public double GetCritChance() => _combatStats.GetCritChance();
    public double GetCritDamage() => _combatStats.GetCritDamage();
    public double GetEvadeChance() => _combatStats.GetEvadeChance();
    public double GetBlockChance() => _combatStats.GetBlockChance();
    public double GetParryChance() => _combatStats.GetParryChance();
    public double GetReflexesParryBonus() => _combatStats.GetReflexesParryBonus();
    public double GetOffHandDamageFraction() => _combatStats.GetOffHandDamageFraction();
    public int GetOffHandTotalAttack(int dist) => _combatStats.GetOffHandTotalAttack(dist);
    public int GetBlockValue() => _combatStats.GetBlockValue();
    public bool IsMagicalDamage() => _combatStats.IsMagicalDamage();
    public bool IsOffHandMagical() => _combatStats.IsOffHandMagical();
    public int GetBaseDamage() => _combatStats.GetBaseDamage();
    public int GetBaseDefense() => _combatStats.GetBaseDefense();
    public int GetTotalAttack() => _combatStats.GetTotalAttack();
    public int GetTotalDefense() => _combatStats.GetTotalDefense();
    public int GetTotalResistance() => _combatStats.GetTotalResistance();
    public int RollAttackDamage() => _combatStats.RollAttackDamage();
    public int RollOffHandDamage() => _combatStats.RollOffHandDamage();
    public int GetMaxAttackDamage() => _combatStats.GetMaxAttackDamage();
    public int GetTotalAttack(int dist) => _combatStats.GetTotalAttack(dist);
    public double GetBerserkMultiplier() => _combatStats.GetBerserkMultiplier();
    public int GetSkillRank(string skillId) => _progression.GetSkillRank(skillId);
    public double GetSkillRankDmgMult(string skillId) => _progression.GetSkillRankDmgMult(skillId);
    public double GetSkillRankCdMult(string skillId) => _combatStats.GetSkillRankCdMult(skillId);
    public double GetTenacity() => _combatStats.GetTenacity();
    public double GetArmorPenetration() => _combatStats.GetArmorPenetration();
    public double GetCooldownReduction() => _combatStats.GetCooldownReduction();
    public double GetHealthRegenPercent() => _combatStats.GetHealthRegenPercent();
    public double GetManaRegenPercent() => _combatStats.GetManaRegenPercent();
    public double GetCastSpeedReduction() => _combatStats.GetCastSpeedReduction();
    public double GetCastTimeMultiplier() => _combatStats.GetCastTimeMultiplier();
    public double GetPassiveRankMult(string skillId) => _progression.GetPassiveRankMult(skillId);
    public bool IsWieldingBow() => _combatStats.IsWieldingBow();
    public int GetEffectiveAttackRange() => _combatStats.GetEffectiveAttackRange();
    public double GetExtraArrowChance() => _combatStats.GetExtraArrowChance();
    public double GetAccuracy() => _combatStats.GetAccuracy();
    public double GetBowAccuracyBonus() => _combatStats.GetBowAccuracyBonus();
    public double GetMeleeEvadeBonus() => _combatStats.GetMeleeEvadeBonus();
    public int GetBowRangeBonus() => _combatStats.GetBowRangeBonus();
    public double GetCloseRangeArmorPen(int dist) => _combatStats.GetCloseRangeArmorPen(dist);
    public double GetHunterInstinctCritBonus(ICombatant target) => _combatStats.GetHunterInstinctCritBonus(target);
    public int RollAttackDamage(int dist) => _combatStats.RollAttackDamage(dist);
    public int GetMaxAttackDamage(int dist) => _combatStats.GetMaxAttackDamage(dist);

    public int Speed { get; set; } = 1;
    public DateTime LastDamagedTime { get; set; } = DateTime.MinValue;
    public DateTime LastRegenTime { get; set; } = DateTime.MinValue;

    public MovementState Movement { get; set; } = new();
    public CombatState Combat { get; set; } = new();
    public InteractionState Interaction { get; set; } = new();
    public DialogueState Dialogue { get; set; } = new();
    public string Facing { get; set; } = "down";
    public List<ActiveDebuff> ActiveDebuffs { get; set; } = new();
    public object DebuffsLock { get; } = new();
    public List<ActiveDebuff> GetDebuffsSnapshot()
    {
        lock (DebuffsLock) return new List<ActiveDebuff>(ActiveDebuffs);
    }
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };
    public List<Item> BuybackItems { get; set; } = new();
    public Guid? PartyId { get; set; }
    public bool IsTrading { get; set; }
    public bool IsAdmin { get; set; }
    public string CurrentZoneId { get; set; } = BalanceStatic.MainZoneId;
    public bool IsDead { get; set; }
    public DateTime DeathTime { get; set; }

    public bool TryLevelUp() => _progression.TryLevelUp(this);
}
