using System.Text.Json;
using System.Text.Json.Serialization;

namespace LostAndDivine.Shared.Models;

[JsonConverter(typeof(SkillTypeJsonConverter))]
public enum SkillType
{
    Unknown = 0,
    Active = 1,
    Passive = 2
}

public static class SkillTypeExtensions
{
    public static SkillType Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return SkillType.Unknown;
        string t = s.Trim().ToLowerInvariant();
        // Поддержка русских значений из старых миграций
        if (t.Contains("актив") || t == "active") return SkillType.Active;
        if (t.Contains("пассив") || t == "passive") return SkillType.Passive;
        return SkillType.Unknown;
    }
    public static string ToDisplayString(this SkillType t) => t switch
    {
        SkillType.Active => "active",
        SkillType.Passive => "passive",
        _ => "unknown"
    };
}

public sealed class SkillTypeJsonConverter : JsonConverter<SkillType>
{
    public override SkillType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return SkillTypeExtensions.Parse(reader.GetString());
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int v))
            return Enum.IsDefined(typeof(SkillType), v) ? (SkillType)v : SkillType.Unknown;
        return SkillType.Unknown;
    }
    public override void Write(Utf8JsonWriter writer, SkillType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToDisplayString());
}
