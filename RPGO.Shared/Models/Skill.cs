namespace RPGGame.Shared.Models;

/// <summary>
/// Боевой навык (умение).
/// </summary>
public class Skill
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "active";     // active — активный навык боя
    public int MpCost { get; set; }                   // стоимость маны (в игре пока нет маны)
    public int CooldownMs { get; set; }               // откат
    public double DamageMultiplier { get; set; } = 1.0; // множитель урона
    public int MinLevel { get; set; } = 1;            // мин. уровень для использования
    public int SkillPointCost { get; set; } = 1;      // стоимость в очках навыков
}
