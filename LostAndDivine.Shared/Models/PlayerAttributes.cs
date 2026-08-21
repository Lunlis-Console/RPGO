namespace LostAndDivine.Shared.Models;

/// <summary>
/// Value Object для первичных атрибутов игрока.
/// Хранит базовые значения, эффективные считаются с учётом экипировки.
/// </summary>
public sealed class PlayerAttributes
{
    public int Strength { get; set; } = 1;
    public int Endurance { get; set; } = 1;
    public int Agility { get; set; } = 1;
    public int Cunning { get; set; } = 1;
    public int Intellect { get; set; } = 1;
    public int Wisdom { get; set; } = 1;
    public int AttributePoints { get; set; }

    public int GetEffectiveStrength(Equipment eq) => Strength + eq.GetBonusStrength();
    public int GetEffectiveEndurance(Equipment eq) => Endurance + eq.GetBonusEndurance();
    public int GetEffectiveAgility(Equipment eq) => Agility + eq.GetBonusAgility();
    public int GetEffectiveCunning(Equipment eq) => Cunning + eq.GetBonusCunning();
    public int GetEffectiveIntellect(Equipment eq) => Intellect + eq.GetBonusIntellect();
    public int GetEffectiveWisdom(Equipment eq) => Wisdom + eq.GetBonusWisdom();

    public void Reset() { Strength = Endurance = Agility = Cunning = Intellect = Wisdom = 1; AttributePoints = 0; }
}
