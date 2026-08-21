using LostAndDivine.Shared;

namespace LostAndDivine.Shared.Models;

public class Player : ICombatant
{
    private readonly PlayerAttributes _attributes = new();
    private readonly PlayerProgression _progression = new();
    private readonly PlayerCombatStats _combatStats;
    private readonly PlayerInventory _inventory = new();
    private readonly PlayerVitals _vitals = new();
    private readonly PlayerWorldState _state = new();

    public Player() => _combatStats = new PlayerCombatStats(this);

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Безымянный";
    public CharacterClass Class { get; set; }

    // Атрибуты
    public int Strength { get => _attributes.Strength; set => _attributes.Strength = value; }
    public int Endurance { get => _attributes.Endurance; set => _attributes.Endurance = value; }
    public int Agility { get => _attributes.Agility; set => _attributes.Agility = value; }
    public int Cunning { get => _attributes.Cunning; set => _attributes.Cunning = value; }
    public int Intellect { get => _attributes.Intellect; set => _attributes.Intellect = value; }
    public int Wisdom { get => _attributes.Wisdom; set => _attributes.Wisdom = value; }
    public int AttributePoints { get => _attributes.AttributePoints; set => _attributes.AttributePoints = value; }
    // Прогрессия
    public int Level { get => _progression.Level; set => _progression.Level = value; }
    public int Experience { get => _progression.Experience; set => _progression.Experience = value; }
    public int Gold { get => _progression.Gold; set => _progression.Gold = value; }
    public int SkillPoints { get => _progression.SkillPoints; set => _progression.SkillPoints = value; }
    public List<string> LearnedSkills { get => _progression.LearnedSkills; set => _progression.LearnedSkills = value; }
    public Dictionary<string,int> SkillRanks { get => _progression.SkillRanks; set => _progression.SkillRanks = value; }
    // Vitals
    public int Health { get => _vitals.Health; set => _vitals.Health = value; }
    public int MaxHealth { get => _vitals.MaxHealth; set => _vitals.MaxHealth = value; }
    public int Mana { get => _vitals.Mana; set => _vitals.Mana = value; }
    public int MaxMana { get => _vitals.MaxMana; set => _vitals.MaxMana = value; }
    public bool IsDead { get => _vitals.IsDead; set => _vitals.IsDead = value; }
    public DateTime DeathTime { get => _vitals.DeathTime; set => _vitals.DeathTime = value; }
    public DateTime LastDamagedTime { get => _vitals.LastDamagedTime; set => _vitals.LastDamagedTime = value; }
    public DateTime LastRegenTime { get => _vitals.LastRegenTime; set => _vitals.LastRegenTime = value; }
    public int Speed { get => _vitals.Speed; set => _vitals.Speed = value; }
    public double AdminDamageMultiplier { get => _vitals.AdminDamageMultiplier; set => _vitals.AdminDamageMultiplier = value; }
    // Inventory
    public List<Item> Inventory { get => _inventory.Inventory; set => _inventory.Inventory = value; }
    public Equipment Equipment { get => _inventory.Equipment; set => _inventory.Equipment = value; }
    public List<string?> HotbarSlots { get => _inventory.HotbarSlots; set => _inventory.HotbarSlots = value; }
    public List<Item> BuybackItems { get => _inventory.BuybackItems; set => _inventory.BuybackItems = value; }
    // State
    public int X { get => _state.X; set => _state.X = value; }
    public int Y { get => _state.Y; set => _state.Y = value; }
    public string Facing { get => _state.Facing; set => _state.Facing = value; }
    public string CurrentZoneId { get => _state.CurrentZoneId; set => _state.CurrentZoneId = value; }
    public Guid? PartyId { get => _state.PartyId; set => _state.PartyId = value; }
    public bool IsTrading { get => _state.IsTrading; set => _state.IsTrading = value; }
    public bool IsAdmin { get => _state.IsAdmin; set => _state.IsAdmin = value; }
    public MovementState Movement { get => _state.Movement; set => _state.Movement = value; }
    public CombatState Combat { get => _state.Combat; set => _state.Combat = value; }
    public InteractionState Interaction { get => _state.Interaction; set => _state.Interaction = value; }
    public DialogueState Dialogue { get => _state.Dialogue; set => _state.Dialogue = value; }
    public List<ActiveDebuff> ActiveDebuffs { get => _state.ActiveDebuffs; set => _state.ActiveDebuffs = value; }
    public object DebuffsLock => _state.DebuffsLock;
    public List<ActiveDebuff> GetDebuffsSnapshot() => _state.GetDebuffsSnapshot();
    public System.Collections.Concurrent.ConcurrentDictionary<string,DateTime> LastSkillUse { get => _state.LastSkillUse; set => _state.LastSkillUse = value; }
    public List<string> QueuedSkillIds { get => _state.QueuedSkillIds; set => _state.QueuedSkillIds = value; }
    public object QueuedSkillIdsLock => _state.QueuedSkillIdsLock;
    public List<QuestProgress> ActiveQuests { get => _state.ActiveQuests; set => _state.ActiveQuests = value; }
    public List<string> CompletedQuestIds { get => _state.CompletedQuestIds; set => _state.CompletedQuestIds = value; }
    public Dictionary<string,string> CompletedQuestTimes { get => _state.CompletedQuestTimes; set => _state.CompletedQuestTimes = value; }

    [System.Text.Json.Serialization.JsonIgnore] public PlayerAttributes Attributes => _attributes;
    [System.Text.Json.Serialization.JsonIgnore] public PlayerProgression Progression => _progression;
    [System.Text.Json.Serialization.JsonIgnore] public PlayerCombatStats CombatStats => _combatStats;
    [System.Text.Json.Serialization.JsonIgnore] public PlayerInventory InventoryComponent => _inventory;
    [System.Text.Json.Serialization.JsonIgnore] public PlayerVitals Vitals => _vitals;
    [System.Text.Json.Serialization.JsonIgnore] public PlayerWorldState State => _state;

    public double BaseCritChance { get; set; } = 1.0;
    public double BaseCritDamage { get; set; } = 1.5;
    public double BaseEvadeChance { get; set; } = 1.0;
    public double BaseBlockChance { get; set; } = 0.0;
    public double BaseParryChance { get; set; } = 0.0;

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
    public int GetSkillRank(string id) => _progression.GetSkillRank(id);
    public double GetSkillRankDmgMult(string id) => _progression.GetSkillRankDmgMult(id);
    public double GetSkillRankCdMult(string id) => _combatStats.GetSkillRankCdMult(id);
    public double GetTenacity() => _combatStats.GetTenacity();
    public double GetArmorPenetration() => _combatStats.GetArmorPenetration();
    public double GetCooldownReduction() => _combatStats.GetCooldownReduction();
    public double GetHealthRegenPercent() => _combatStats.GetHealthRegenPercent();
    public double GetManaRegenPercent() => _combatStats.GetManaRegenPercent();
    public double GetCastSpeedReduction() => _combatStats.GetCastSpeedReduction();
    public double GetCastTimeMultiplier() => _combatStats.GetCastTimeMultiplier();
    public double GetPassiveRankMult(string id) => _progression.GetPassiveRankMult(id);
    public bool IsWieldingBow() => _combatStats.IsWieldingBow();
    public int GetEffectiveAttackRange() => _combatStats.GetEffectiveAttackRange();
    public double GetExtraArrowChance() => _combatStats.GetExtraArrowChance();
    public double GetAccuracy() => _combatStats.GetAccuracy();
    public double GetBowAccuracyBonus() => _combatStats.GetBowAccuracyBonus();
    public double GetMeleeEvadeBonus() => _combatStats.GetMeleeEvadeBonus();
    public int GetBowRangeBonus() => _combatStats.GetBowRangeBonus();
    public double GetCloseRangeArmorPen(int dist) => _combatStats.GetCloseRangeArmorPen(dist);
    public double GetHunterInstinctCritBonus(ICombatant t) => _combatStats.GetHunterInstinctCritBonus(t);
    public int RollAttackDamage(int dist) => _combatStats.RollAttackDamage(dist);
    public int GetMaxAttackDamage(int dist) => _combatStats.GetMaxAttackDamage(dist);
    public bool TryLevelUp() => _progression.TryLevelUp(this);
}
