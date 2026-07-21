namespace RPGGame.Shared.Models;

/// <summary>
/// Статические константы для формул атрибутов/характеристик.
/// Дублирует часть Balance.cs, но доступен из Shared (нужен Player/Monster).
/// </summary>
public static class BalanceStatic
{
    public const int AttackPerStrength = 2;
    public const int AttackPerAgility = 1;
    public const int AttackPerIntellect = 2;
    public const int DefensePerEndurance = 1;
    public const int ResistancePerWisdom = 1;
    public const double CritChancePerCunning = 1.0;
    public const double CritDamagePerStrength = 0.05;
    public const double EvadeChancePerCunning = 1.0;
}
