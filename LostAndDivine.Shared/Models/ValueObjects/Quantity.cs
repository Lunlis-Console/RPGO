using LostAndDivine.Shared.Models;

namespace LostAndDivine.Shared.Models.ValueObjects;

/// <summary>
/// Value-object для количества предметов (P2-3). Инвариант: 1 ≤ Value ≤ MaxStackForType.
/// </summary>
public readonly record struct Quantity
{
    public int Value { get; }
    public int MaxStack { get; }

    public Quantity(int value, int maxStack)
    {
        if (maxStack < 1) throw new ArgumentOutOfRangeException(nameof(maxStack));
        if (value < 1 || value > maxStack) throw new ArgumentOutOfRangeException(nameof(value), $"Quantity {value} вне 1..{maxStack}");
        Value = value;
        MaxStack = maxStack;
    }

    public static Quantity Create(int value, ItemType type) => new(value, type is ItemType.Consumable or ItemType.Collectible or ItemType.Trophy or ItemType.Material ? 10 : 1);
    public static Quantity Create(int value, string? type) => Create(value, ItemTypeExtensions.Parse(type));

    public Quantity WithValue(int newValue) => new(newValue, MaxStack);

    public static implicit operator int(Quantity q) => q.Value;
}
