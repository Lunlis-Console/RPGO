using System.Text.Json;
using System.Text.Json.Serialization;

namespace LostAndDivine.Shared;

/// <summary>
/// Направление взгляда/движения сущности. Заменяет строковый псевдо-enum
/// ("up"/"down"/"left"/"right") на типобезопасный enum (P2-1): опечатка теперь
/// ловится компилятором, а не молча даёт неверный результат.
/// </summary>
[JsonConverter(typeof(FacingJsonConverter))]
public enum Facing
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Сохраняет проводной формат Facing как строчные строки ("up", "down", ...),
/// чтобы не ломать совместимость с уже развёрнутыми клиентами/серверами.
/// </summary>
public sealed class FacingJsonConverter : JsonConverter<Facing>
{
    public override Facing Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (!string.IsNullOrEmpty(s))
        {
            foreach (var f in Enum.GetValues<Facing>())
            {
                if (string.Equals(f.ToString(), s, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
        }
        return Facing.Down;
    }

    public override void Write(Utf8JsonWriter writer, Facing value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
    }
}
