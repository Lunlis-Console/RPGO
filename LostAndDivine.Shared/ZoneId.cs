namespace LostAndDivine.Shared;

/// <summary>
/// Типобезопасный идентификатор зоны (P2-2). Заменяет строковые магические "main".
/// Неявно конвертируется в string для совместимости, но новые API принимают ZoneId.
/// </summary>
public readonly record struct ZoneId(string Value)
{
    public static readonly ZoneId Main = new(ZoneIds.Main);
    public static readonly ZoneId Start = new(ZoneIds.Start);

    public bool IsMain => Value == ZoneIds.Main;
    public bool IsStart => Value == ZoneIds.Start;
    public bool IsInstance => Value.StartsWith("instance:", StringComparison.OrdinalIgnoreCase);

    public static implicit operator string(ZoneId id) => id.Value;
    public static implicit operator ZoneId(string value) => new(value);

    public override string ToString() => Value;
}
