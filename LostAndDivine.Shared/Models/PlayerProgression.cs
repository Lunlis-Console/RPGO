namespace LostAndDivine.Shared.Models;

/// <summary>
/// Компонент прогрессии игрока: уровень, опыт, золото, очки, изученные навыки.
/// Вынесен из Player для соблюдения SRP (было в God-Class Player.cs:384).
/// </summary>
public sealed class PlayerProgression
{
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Gold { get; set; }
    public int SkillPoints { get; set; }
    public List<string> LearnedSkills { get; set; } = new();
    public Dictionary<string, int> SkillRanks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int GetSkillRank(string skillId) => SkillRanks.TryGetValue(skillId, out int r) ? r : 1;

    public double GetSkillRankDmgMult(string skillId) => 1.0 + (GetSkillRank(skillId) - 1) * 0.12;

    public double GetPassiveRankMult(string skillId) => 1.0 + (GetSkillRank(skillId) - 1) * 0.33;

    /// <summary>
    /// Проверка и повышение уровня. Возвращает true если был ап.
    /// Логика перенесена из Player.TryLevelUp() без изменения поведения.
    /// Требует доступ к MaxHealth/Health/AttributePoints — передаются через Player.
    /// </summary>
    public bool TryLevelUp(Player player)
    {
        bool leveled = false;
        while (Level < BalanceStatic.MaxLevel)
        {
            int needed = BalanceStatic.XpNeededForNextLevel(Level);
            if (Experience < needed) break;
            Level++;
            Experience -= needed;
            player.MaxHealth += BalanceStatic.MaxHealthPerLevel;
            player.Health = player.MaxHealth;
            // AttributePoints хранится в PlayerAttributes, но доступ через Player wrapper
            player.AttributePoints += BalanceStatic.AttributePointsPerLevel;
            if (Level % 2 == 0)
                SkillPoints++;
            leveled = true;
        }
        return leveled;
    }
}
