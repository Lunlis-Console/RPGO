namespace LostAndDivine.Shared.Models.ValueObjects;

/// <summary>
/// Уровень заточки предмета (P2-3). Инвариант: 0 ≤ Level ≤ EnhancementBalance.MaxLevel.
/// </summary>
public readonly record struct EnhancementLevel
{
    public int Level { get; }

    public EnhancementLevel(int level)
    {
        if (level < 0 || level > EnhancementBalance.MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), $"Enhancement {level} вне 0..{EnhancementBalance.MaxLevel}");
        Level = level;
    }

    public static implicit operator int(EnhancementLevel e) => e.Level;
    public static implicit operator EnhancementLevel(int level) => new(level);
}
