namespace LostAndDivine.Shared.Models;

/// <summary>
/// Баланс заточки: бонусы и шансы.
/// Выделено чтобы разорвать цикл BalanceStatic <-> EnhancementHelper.
/// </summary>
public static class EnhancementBalance
{
    public const double PerLevelRate = 0.10;
    public const int MaxLevel = 10;
    public static readonly double[] SuccessChances = { 100, 50, 25, 12.5, 6.25, 3.13, 1.5, 0.7, 0.4, 0.2 };

    public static double SuccessChance(int targetLevel)
    {
        int idx = targetLevel - 1;
        if (idx < 0 || idx >= SuccessChances.Length) return 0;
        return SuccessChances[idx];
    }
}
