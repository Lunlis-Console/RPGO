using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json.Serialization;

namespace LostAndDivine.ClientMonoGame.Networking;

public sealed class AuthResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Token { get; set; }
    public Guid PlayerId { get; set; }
}

public sealed class WelcomeData
{
    public string? Message { get; set; }
    public string? PlayerName { get; set; }
    public string? ClassName { get; set; }
}

public sealed class ChatData
{
    public string? Channel { get; set; }
    public string? Name { get; set; }
    public string? Text { get; set; }
    public string? To { get; set; }
    public bool IsAdmin { get; set; }
}

public sealed class StatusData
{
    public string? Name { get; set; }
    public string? ClassName { get; set; }
    public int Level { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int PhysAttack { get; set; }
    public int MagAttack { get; set; }
    public int Defense { get; set; }
    public int Resistance { get; set; }
    public double CritChance { get; set; }
    public double CritDamage { get; set; }
    public double EvadeChance { get; set; }
    public double BlockChance { get; set; }
    public double ParryChance { get; set; }
    public int Gold { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Experience { get; set; }
    public Dictionary<string, string> Equipped { get; set; } = new();
    public int Strength { get; set; }
    public int Endurance { get; set; }
    public int Agility { get; set; }
    public int Cunning { get; set; }
    public int Intellect { get; set; }
    public int Wisdom { get; set; }
    public int AttributePoints { get; set; }
    public int SkillPoints { get; set; }
    public int MoveIntervalMs { get; set; }
    public double AttackSpeed { get; set; }
    public int AttackIntervalMs { get; set; }
    public int AttackRange { get; set; }
    public string WeaponDamageType { get; set; } = "";
    public double WeaponSpeedModifier { get; set; } = 1.0;
    public BreakdownData? Breakdown { get; set; }
    public List<DebuffInfo>? ActiveDebuffs { get; set; }
}

public sealed class InventoryData
{
    public int PlayerLevel { get; set; }
    public List<Item>? Items { get; set; }
    public int Gold { get; set; }
    public EquipmentData? Equipment { get; set; }
    public int BonusPhysAttack { get; set; }
    public int BonusMagAttack { get; set; }
    public int BonusDefense { get; set; }
    public int BonusResistance { get; set; }
    public int BonusMaxHealth { get; set; }
    public bool FromUnequip { get; set; }
}

public sealed class EquipmentData
{
    // slot id (см. EquipmentSlots) -> предмет
    public Dictionary<string, Item> Slots { get; set; } = new();
}

public sealed class ShopData
{
    public int MerchantX { get; set; }
    public int MerchantY { get; set; }
    public string? MerchantName { get; set; }
    public List<Item>? Items { get; set; }
    public List<Item>? Buyback { get; set; }
    public int PlayerGold { get; set; }
    public int Discount { get; set; }
}

public sealed class QuestLogData
{
    public List<QuestInfo>? Available { get; set; }
    public List<QuestInfo>? Active { get; set; }
}

public sealed class StorageData
{
    public List<Item>? Items { get; set; }
    public int Slots { get; set; }
}

public sealed class TradeOpenData
{
    public string? SessionId { get; set; }
    public string? OtherName { get; set; }
    public int OtherLevel { get; set; }
    public int OtherHp { get; set; }
    public int OtherMaxHp { get; set; }
    public List<TradeItemData>? YourInventory { get; set; }
    public int YourGold { get; set; }
    public List<TradeItemData>? OtherInventory { get; set; }
    public int OtherGold { get; set; }
}

public sealed class TradeItemData
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? TemplateId { get; set; }
    public string? WeaponSubtype { get; set; }
    public int Quantity { get; set; }
    public int Value { get; set; }
    public string? Description { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int MaxHealthBonus { get; set; }
    public int HealAmount { get; set; }
    public int RestoreMana { get; set; }
    public int MaxStack { get; set; }
    public int BonusStrength { get; set; }
    public int BonusEndurance { get; set; }
    public int BonusAgility { get; set; }
    public int BonusCunning { get; set; }
    public int BonusIntellect { get; set; }
    public int BonusWisdom { get; set; }
    public int BonusPhysAttack { get; set; }
    public int BonusMagAttack { get; set; }
    public int BonusDefense { get; set; }
    public int BonusResistance { get; set; }
    public double BonusCritChance { get; set; }
    public double BonusCritDamage { get; set; }
    public double BonusEvadeChance { get; set; }
    public double BonusAttackSpeed { get; set; }
    public double BonusBlockChance { get; set; }
    public double BonusParryChance { get; set; }
    public string DamageType { get; set; } = "";
    public int RequiredLevel { get; set; }
    public int DamageMin { get; set; }
    public int DamageMax { get; set; }
    public double AttackSpeedModifier { get; set; } = 1.0;
    public bool TwoHanded { get; set; }
    public int AttackRange { get; set; } = 1;

    /// <summary>Создаёт копию предмета с указанным количеством.</summary>
    public TradeItemData WithQuantity(int qty) => new()
    {
        Id = Id, Name = Name, Type = Type, TemplateId = TemplateId,
        WeaponSubtype = WeaponSubtype,
        Value = Value, Description = Description, Attack = Attack,
        Defense = Defense, MaxHealthBonus = MaxHealthBonus, HealAmount = HealAmount,
        MaxStack = MaxStack, Quantity = qty, RestoreMana = RestoreMana,
        BonusStrength = BonusStrength, BonusEndurance = BonusEndurance,
        BonusAgility = BonusAgility, BonusCunning = BonusCunning,
        BonusIntellect = BonusIntellect, BonusWisdom = BonusWisdom,
        BonusPhysAttack = BonusPhysAttack, BonusMagAttack = BonusMagAttack,
        BonusDefense = BonusDefense, BonusResistance = BonusResistance,
        BonusCritChance = BonusCritChance, BonusCritDamage = BonusCritDamage,
        BonusEvadeChance = BonusEvadeChance, BonusAttackSpeed = BonusAttackSpeed,
        BonusBlockChance = BonusBlockChance, BonusParryChance = BonusParryChance,
        DamageType = DamageType, RequiredLevel = RequiredLevel,
        DamageMin = DamageMin, DamageMax = DamageMax,
        AttackSpeedModifier = AttackSpeedModifier, TwoHanded = TwoHanded,
        AttackRange = AttackRange
    };
}

public sealed class TradeOfferData
{
    public bool IsFromMe { get; set; }
    public TradeOfferSummary? Offer { get; set; }
}

public sealed class TradeOfferSummary
{
    public List<TradeItemData>? Items { get; set; }
    public int Gold { get; set; }
}

public sealed class TradeConfirmData
{
    public bool YouConfirmed { get; set; }
    public bool OtherConfirmed { get; set; }
}

public sealed class TradeCompleteData
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public sealed class QuestInfo
{
    public string? QuestId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public int Target { get; set; }
    public string? TargetZoneId { get; set; }
    public string? TargetNpcId { get; set; }
    public int XpReward { get; set; }
    public int GoldReward { get; set; }
    public int Current { get; set; }
    public bool Completed { get; set; }
    public string? ChainId { get; set; }
    public int Step { get; set; }
    public string? PrerequisiteQuestId { get; set; }
    public int MinLevel { get; set; }
}

public sealed class BreakdownData
{
    public BreakdownPart? PhysAttack { get; set; }
    public BreakdownPart? MagAttack { get; set; }
    public BreakdownPart? Defense { get; set; }
    public BreakdownPart? Resistance { get; set; }
    public BreakdownPart? Crit { get; set; }
    public BreakdownPart? CritDmg { get; set; }
    public BreakdownPart? Evade { get; set; }
    public BreakdownPart? Block { get; set; }
    public BreakdownPart? Parry { get; set; }
    public EffectiveData? Effective { get; set; }
}

public sealed class EffectiveData
{
    public int Strength { get; set; }
    public int Endurance { get; set; }
    public int Agility { get; set; }
    public int Cunning { get; set; }
    public int Intellect { get; set; }
    public int Wisdom { get; set; }
}

public sealed class DebuffInfo
{
    public string Type { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public double Value { get; set; }
    public int RemainingMs { get; set; }
    public int DurationMs { get; set; }
}

public sealed class ClientSkillInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public int MpCost { get; set; }
    public int CooldownMs { get; set; }
    public double DamageMultiplier { get; set; } = 1;
    public int MinLevel { get; set; } = 1;
    public int SkillPointCost { get; set; } = 1;
    // Поля для древа навыков (опциональны; сервер может не заполнять):
    public string? ParentId { get; set; }
    public int Tier { get; set; } = 1;
    public string? IconName { get; set; }
    public int MaxRank { get; set; } = 3;
    public int Rank { get; set; } = 1;
    public bool Learned { get; set; }
}

public sealed class LootItemInfo
{
    public string Id { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string WeaponSubtype { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int Value { get; set; }
    public string Description { get; set; } = "";
    public int MaxHealthBonus { get; set; }
    public int HealAmount { get; set; }
    public int RestoreMana { get; set; }
    public int MaxStack { get; set; } = 10;
    public int BonusStrength { get; set; }
    public int BonusEndurance { get; set; }
    public int BonusAgility { get; set; }
    public int BonusCunning { get; set; }
    public int BonusIntellect { get; set; }
    public int BonusWisdom { get; set; }
    public int BonusPhysAttack { get; set; }
    public int BonusMagAttack { get; set; }
    public int BonusDefense { get; set; }
    public int BonusResistance { get; set; }
    public double BonusCritChance { get; set; }
    public double BonusCritDamage { get; set; }
    public double BonusEvadeChance { get; set; }
    public double BonusAttackSpeed { get; set; }
    public double BonusBlockChance { get; set; }
    public double BonusParryChance { get; set; }
    public string DamageType { get; set; } = "";
    public int RequiredLevel { get; set; }
    public int DamageMin { get; set; }
    public int DamageMax { get; set; }
    public double AttackSpeedModifier { get; set; } = 1.0;
    public bool TwoHanded { get; set; }
    public int AttackRange { get; set; } = 1;
}
