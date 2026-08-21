using System;

namespace LostAndDivine.Shared.Models;

/// <summary>
/// Помогает применять уровень заточки/усиления предмета.
/// Бонус считается на лету поверх базовых характеристик предмета:
/// каждый уровень увеличивает базовую стату на EnhancementBonusPerLevel (сложный процент).
/// Хранится только EnhancementLevel — сами статьи предмета не мутируются,
/// что сохраняет корректность при перезагрузке из БД (SyncItemFromTemplate).
/// </summary>
public static class EnhancementHelper
{
    /// <summary>Базовый множитель роста статы за один уровень заточки (0.10 = +10%). Делегирует в EnhancementBalance для разрыва цикла BalanceStatic.</summary>
    public const double PerLevelRate = EnhancementBalance.PerLevelRate;

    /// <summary>Максимальный уровень заточки.</summary>
    public const int MaxLevel = EnhancementBalance.MaxLevel;

    /// <summary>Шанс успеха перехода НА указанный уровень (target = текущий+1). Индекс 0 = +1.</summary>
    public static readonly double[] SuccessChances = EnhancementBalance.SuccessChances;

    /// <summary>Шанс успеха при попытке поднять заточку до targetLevel (1..MaxLevel).</summary>
    public static double SuccessChance(int targetLevel) => EnhancementBalance.SuccessChance(targetLevel);

    /// <summary>True, если предмет можно ещё улучшать.</summary>
    public static bool CanEnhance(Item item) => item != null && item.EnhancementLevel < MaxLevel;

    /// <summary>Усиленное целочисленное значение базовой статы с учётом заточки.</summary>
    public static int Enhanced(this Item item, int baseValue)
    {
        if (item == null || item.EnhancementLevel <= 0 || baseValue == 0) return baseValue;
        double rate = EnhancementBalance.PerLevelRate;
        return (int)Math.Round(baseValue * Math.Pow(1 + rate, item.EnhancementLevel), MidpointRounding.AwayFromZero);
    }

    /// <summary>Усиленное значение базовой процентной статы с учётом заточки.</summary>
    public static double Enhanced(this Item item, double baseValue)
    {
        if (item == null || item.EnhancementLevel <= 0 || baseValue == 0) return baseValue;
        double rate = EnhancementBalance.PerLevelRate;
        return Math.Round(baseValue * Math.Pow(1 + rate, item.EnhancementLevel), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Чистый бонус (усиленное − базовое) для отображения в тултипе/окне.</summary>
    public static int EnhancementBonus(this Item item, int baseValue) => item.Enhanced(baseValue) - baseValue;

    /// <summary>Чистый бонус (процентный) для отображения.</summary>
    public static double EnhancementBonus(this Item item, double baseValue) => item.Enhanced(baseValue) - baseValue;
}
