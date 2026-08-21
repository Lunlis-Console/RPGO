namespace LostAndDivine.Shared.Models;

/// <summary>
/// Прогрессия игрока: уровни, опыт, здоровье, очки.
/// Выделено из BalanceStatic (дублировалось с server/Balance.cs).
/// Единственный источник для Shared и Server.
/// </summary>
public static class ProgressionBalance
{
    public const int MaxLevel = 50;
    public const int MaxHealthPerLevel = 10;
    public const int MaxHealthPerEndurance = 10;
    public const int AttributePointsPerLevel = 3;
    public const int XpPerLevel = 50;

    public static int XpNeededForNextLevel(int level) => level * XpPerLevel;

    public static bool RollPercent(double percent)
        => Random.Shared.NextDouble() * 100 < percent;
}
