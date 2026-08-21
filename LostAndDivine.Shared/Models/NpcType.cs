using System.Text.Json.Serialization;

namespace LostAndDivine.Shared.Models;

[JsonConverter(typeof(NpcTypeJsonConverter))]
public enum NpcType
{
    Unknown = 0,
    Merchant = 1,
    Board = 2,
    Npc = 3,
    InstancePortal = 4,
    Dummy = 5,
    Blacksmith = 6,
    Storage = 7,
    Collectible = 8
}

public static class NpcTypeExtensions
{
    public static NpcType Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "merchant" => NpcType.Merchant,
        "board" => NpcType.Board,
        "npc" => NpcType.Npc,
        "instance_portal" => NpcType.InstancePortal,
        "dummy" => NpcType.Dummy,
        "blacksmith" => NpcType.Blacksmith,
        "storage" => NpcType.Storage,
        "collectible" => NpcType.Collectible,
        _ => NpcType.Unknown
    };
    public static string ToDisplayString(this NpcType t) => t switch
    {
        NpcType.Merchant => "merchant",
        NpcType.Board => "board",
        NpcType.Npc => "npc",
        NpcType.InstancePortal => "instance_portal",
        NpcType.Dummy => "dummy",
        NpcType.Blacksmith => "blacksmith",
        NpcType.Storage => "storage",
        NpcType.Collectible => "collectible",
        _ => "unknown"
    };
}

public sealed class NpcTypeJsonConverter : JsonConverter<NpcType>
{
    public override NpcType Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => reader.TokenType == System.Text.Json.JsonTokenType.String ? NpcTypeExtensions.Parse(reader.GetString()) : NpcType.Unknown;
    public override void Write(System.Text.Json.Utf8JsonWriter writer, NpcType value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToDisplayString());
}
