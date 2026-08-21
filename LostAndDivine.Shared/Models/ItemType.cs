using System.Text.Json;
using System.Text.Json.Serialization;

namespace LostAndDivine.Shared.Models;

/// <summary>
/// Тип предмета — замена строковому псевдо-enum Item.Type.
/// Покрывает экипируемые и неэкипируемые типы, встречающиеся в БД и коде.
/// </summary>
[JsonConverter(typeof(ItemTypeJsonConverter))]
public enum ItemType
{
    Unknown = 0,
    Weapon = 1,
    TwoHand = 2,
    Shield = 3,
    Helmet = 4,
    Cloak = 5,
    Chest = 6,
    Legs = 7,
    Boots = 8,
    Glove = 9,
    Belt = 10,
    Necklace = 11,
    Ring = 12,
    Consumable = 13,
    Material = 14,
    Trophy = 15,
    Collectible = 16,
    QuestItem = 17,
    // Легаси
    Armor = 18,
    Accessory = 19
}

public static class ItemTypeExtensions
{
    public static ItemType Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return ItemType.Unknown;
        return s.Trim().ToLowerInvariant() switch
        {
            "weapon" => ItemType.Weapon,
            "twohand" => ItemType.TwoHand,
            "shield" => ItemType.Shield,
            "helmet" => ItemType.Helmet,
            "cloak" => ItemType.Cloak,
            "chest" => ItemType.Chest,
            "legs" => ItemType.Legs,
            "boots" => ItemType.Boots,
            "glove" => ItemType.Glove,
            "gloves" => ItemType.Glove,
            "belt" => ItemType.Belt,
            "necklace" => ItemType.Necklace,
            "neck" => ItemType.Necklace,
            "ring" => ItemType.Ring,
            "consumable" => ItemType.Consumable,
            "material" => ItemType.Material,
            "trophy" => ItemType.Trophy,
            "collectible" => ItemType.Collectible,
            "quest" => ItemType.QuestItem,
            "questitem" => ItemType.QuestItem,
            "armor" => ItemType.Armor,
            "accessory" => ItemType.Accessory,
            _ => ItemType.Unknown
        };
    }

    public static string ToDisplayString(this ItemType t) => t switch
    {
        ItemType.Weapon => "weapon",
        ItemType.TwoHand => "twohand",
        ItemType.Shield => "shield",
        ItemType.Helmet => "helmet",
        ItemType.Cloak => "cloak",
        ItemType.Chest => "chest",
        ItemType.Legs => "legs",
        ItemType.Boots => "boots",
        ItemType.Glove => "glove",
        ItemType.Belt => "belt",
        ItemType.Necklace => "necklace",
        ItemType.Ring => "ring",
        ItemType.Consumable => "consumable",
        ItemType.Material => "material",
        ItemType.Trophy => "trophy",
        ItemType.Collectible => "collectible",
        ItemType.QuestItem => "quest",
        ItemType.Armor => "armor",
        ItemType.Accessory => "accessory",
        _ => "unknown"
    };

    public static string ToDisplayNameRu(this ItemType t) => t switch
    {
        ItemType.Weapon => "Оружие",
        ItemType.TwoHand => "Двуручное",
        ItemType.Shield => "Щит",
        ItemType.Helmet => "Шлем",
        ItemType.Cloak => "Плащ",
        ItemType.Chest => "Нагрудник",
        ItemType.Legs => "Поножи",
        ItemType.Boots => "Обувь",
        ItemType.Glove => "Перчатки",
        ItemType.Belt => "Пояс",
        ItemType.Necklace => "Ожерелье",
        ItemType.Ring => "Кольцо",
        ItemType.Consumable => "Расходуемое",
        ItemType.Material => "Материал",
        ItemType.Trophy => "Трофей",
        ItemType.Collectible => "Собираемое",
        _ => "Предмет"
    };

    public static bool IsWeapon(this ItemType t) => t is ItemType.Weapon or ItemType.TwoHand;
    public static bool IsEquippable(this ItemType t) => t is ItemType.Weapon or ItemType.TwoHand or ItemType.Shield or ItemType.Helmet or ItemType.Cloak or ItemType.Chest or ItemType.Legs or ItemType.Boots or ItemType.Glove or ItemType.Belt or ItemType.Necklace or ItemType.Ring or ItemType.Armor;
}

public sealed class ItemTypeJsonConverter : JsonConverter<ItemType>
{
    public override ItemType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? s = reader.GetString();
            return ItemTypeExtensions.Parse(s);
        }
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int v))
            return Enum.IsDefined(typeof(ItemType), v) ? (ItemType)v : ItemType.Unknown;
        return ItemType.Unknown;
    }
    public override void Write(Utf8JsonWriter writer, ItemType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToDisplayString());
}
