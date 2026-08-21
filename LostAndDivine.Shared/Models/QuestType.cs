using System.Text.Json.Serialization;

namespace LostAndDivine.Shared.Models;

[JsonConverter(typeof(QuestTypeJsonConverter))]
public enum QuestType
{
    Unknown = 0,
    Kill = 1,
    Collect = 2,
    Talk = 3,
    Travel = 4,
    Use = 5,
    Explore = 6
}

public static class QuestTypeExtensions
{
    public static QuestType Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "kill" => QuestType.Kill,
        "collect" => QuestType.Collect,
        "talk" => QuestType.Talk,
        "travel" => QuestType.Travel,
        "use" => QuestType.Use,
        "explore" => QuestType.Explore,
        _ => QuestType.Unknown
    };
    public static string ToDisplayString(this QuestType t) => t switch
    {
        QuestType.Kill => "kill",
        QuestType.Collect => "collect",
        QuestType.Talk => "talk",
        QuestType.Travel => "travel",
        QuestType.Use => "use",
        QuestType.Explore => "explore",
        _ => "unknown"
    };
}

public sealed class QuestTypeJsonConverter : JsonConverter<QuestType>
{
    public override QuestType Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => reader.TokenType == System.Text.Json.JsonTokenType.String ? QuestTypeExtensions.Parse(reader.GetString()) : QuestType.Unknown;
    public override void Write(System.Text.Json.Utf8JsonWriter writer, QuestType value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToDisplayString());
}

