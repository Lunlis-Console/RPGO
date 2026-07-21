using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json.Serialization;

namespace RPGGame.ClientMonoGame.Networking;

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
    public int Speed { get; set; }
    public int MoveIntervalMs { get; set; }
    public int AttackSpeed { get; set; }
    public int AttackIntervalMs { get; set; }
    public string WeaponDamageType { get; set; } = "";
    public double WeaponSpeedModifier { get; set; } = 1.0;
    public bool IsDualWielding { get; set; }
    public BreakdownData? Breakdown { get; set; }
    public List<DebuffInfo>? ActiveDebuffs { get; set; }
}

public sealed class InventoryData
{
    public List<Item>? Items { get; set; }
    public int Gold { get; set; }
    public EquipmentData? Equipment { get; set; }
    public int BonusPhysAttack { get; set; }
    public int BonusMagAttack { get; set; }
    public int BonusDefense { get; set; }
    public int BonusResistance { get; set; }
    public int BonusMaxHealth { get; set; }
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
    public int Quantity { get; set; }
    public int Value { get; set; }
    public string? Description { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int MaxHealthBonus { get; set; }
    public int HealAmount { get; set; }
    public int MaxStack { get; set; }
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
    public int XpReward { get; set; }
    public int GoldReward { get; set; }
    public int Current { get; set; }
    public bool Completed { get; set; }
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
    // Поля для древа навыков (опциональны; сервер может не заполнять):
    public string? ParentId { get; set; }
    public int Tier { get; set; } = 1;
    public string? IconName { get; set; }
}

public sealed class LootItemInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Value { get; set; }
    public string Description { get; set; } = "";
}
