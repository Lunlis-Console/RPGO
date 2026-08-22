namespace LostAndDivine.Shared.Models.ValueObjects;

/// <summary>
/// Value-object для здоровья (P2-3). Инвариант: 0 ≤ Value ≤ Max (Max = MaxHealth + Bonus).
/// Breaking-change разрещён — старые сохранения конвертируются в миграции.
/// </summary>
public readonly record struct Health
{
    public int Value { get; }
    public int Max { get; }

    public Health(int value, int max)
    {
        if (max < 1) throw new ArgumentOutOfRangeException(nameof(max), "MaxHealth должен быть ≥1");
        if (value < 0 || value > max) throw new ArgumentOutOfRangeException(nameof(value), $"Health {value} вне 0..{max}");
        Value = value;
        Max = max;
    }

    public static Health Create(int value, int baseMax, int bonus) => new(value, baseMax + bonus);

    public Health WithValue(int newValue) => new(newValue, Max);
    public Health WithMax(int newMax) => new(Math.Min(Value, newMax), newMax);

    public static implicit operator int(Health h) => h.Value;
}
