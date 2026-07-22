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

    // Level-up constants (duplicated from server Balance.cs, needed by Player.TryLevelUp)
    public const int MaxHealthPerLevel = 10;
    public const int AttributePointsPerLevel = 3;
    public const int XpPerLevel = 50;

    public static int XpNeededForNextLevel(int level) => level * XpPerLevel;
}
