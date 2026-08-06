namespace LostAndDivine.Shared.Models;

/// <summary>
/// Чистые функции боевой математики. Статические, без зависимостей, тестируемые.
/// </summary>
public static class CombatMath
{
    public static int MinDamage => BalanceStatic.MinDamage;
    public const int MinDamageConst = 1;

    /// <summary>Базовый урон: атака минус защита, не ниже MinDamage.</summary>
    public static int CalcBaseDamage(int attack, int defense)
        => Math.Max(MinDamage, attack - defense);

    /// <summary>Урон с учётом Armor Penetration.</summary>
    public static int CalcDamageWithPen(int attack, int defense, double armorPen)
        => Math.Max(MinDamage, (int)(attack - defense * (1.0 - Math.Min(armorPen, 1.0))));

    /// <summary>Применяет множитель урона (дебонус/бафф).</summary>
    public static int ApplyDamageMultiplier(int damage, double multiplier)
        => Math.Max(MinDamage, (int)(damage * multiplier));

    /// <summary>Применяет снижение урона (Damage Reduction).</summary>
    public static int ApplyDamageReduction(int damage, double reduction)
        => Math.Max(MinDamage, (int)(damage * (1.0 - Math.Min(reduction, 1.0))));

    /// <summary>Применяет критический урон.</summary>
    public static int ApplyCrit(int damage, bool isCrit, double critDamageMult)
        => isCrit ? ApplyDamageMultiplier(damage, critDamageMult) : damage;

    /// <summary>Применяет блокирование.</summary>
    public static int ApplyBlock(int damage, int blockValue)
        => Math.Max(MinDamage, damage - blockValue);

    /// <summary>Рассчитывает итоговый урон по цепочке: атака→защита→пенетрация→крит→блок→редукция.</summary>
    public static int CalcFinalDamage(int rawAttack, int rawDefense,
        double armorPen = 0, bool isCrit = false, double critMult = 1.5,
        int block = 0, double dmgReduction = 0)
    {
        int dmg = CalcDamageWithPen(rawAttack, rawDefense, armorPen);
        dmg = ApplyCrit(dmg, isCrit, critMult);
        dmg = ApplyDamageReduction(dmg, dmgReduction);
        if (block > 0) dmg = ApplyBlock(dmg, block);
        return dmg;
    }

    /// <summary>Выполняет бросок уклонения/парирования/блока.</summary>
    public static (bool evaded, bool parried, bool blocked)
        RollDefense(double evadeChance, double parryChance, double blockChance, bool isMelee = true)
    {
        bool evaded = BalanceStatic.RollPercent(evadeChance);
        bool parried = !evaded && isMelee && BalanceStatic.RollPercent(parryChance);
        bool blocked = !evaded && !parried && BalanceStatic.RollPercent(blockChance);
        return (evaded, parried, blocked);
    }
}
