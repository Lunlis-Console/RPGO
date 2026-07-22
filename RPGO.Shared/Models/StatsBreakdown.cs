namespace RPGGame.Shared.Models;

public class StatsBreakdown
{
    public BreakdownPart PhysAttack { get; set; } = new();
    public BreakdownPart MagAttack { get; set; } = new();
    public BreakdownPart Defense { get; set; } = new();
    public BreakdownPart Resistance { get; set; } = new();
    public BreakdownPart Crit { get; set; } = new();
    public BreakdownPart CritDmg { get; set; } = new();
    public BreakdownPart Evade { get; set; } = new();
    public EffectiveAttrs Effective { get; set; } = new();
}

public class BreakdownPart
{
    public double Base { get; set; }
    public double AttrBonus { get; set; }
    public double EquipBonus { get; set; }
    public int WeaponDamageMin { get; set; }
    public int WeaponDamageMax { get; set; }
    public double Total { get; set; }
}

public class EffectiveAttrs
{
    public int Strength { get; set; }
    public int Endurance { get; set; }
    public int Agility { get; set; }
    public int Cunning { get; set; }
    public int Intellect { get; set; }
    public int Wisdom { get; set; }
}
