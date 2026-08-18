namespace LostAndDivine.Shared.Models;

/// <summary>
/// Чистые функции боевой математики. Статические, без зависимостей, тестируемые.
/// </summary>
public static class CombatMath
{
    public static int MinDamage => BalanceStatic.MinDamage;
    public const int MinDamageConst = 1;

    /// <summary>
    /// Убывающая отдача крита: до порога очко даёт CritChanceLinearRate, дальше — CritChanceDrRate.
    /// Применяется к «очкам» хитча (хитрость + бонусы шмота).
    /// </summary>
    public static double ApplyCritDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.CritChanceLinearPoints,
            BalanceStatic.CritChanceLinearRate, BalanceStatic.CritChanceDrRate);

    /// <summary>
    /// Убывающая отдача крит-урона: до порога очко даёт полный темп (0.05x), дальше — CritDmgDrRate от темпа.
    /// Применяется к «очкам» (сила + бонусы шмота).
    /// </summary>
    public static double ApplyCritDamageDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.CritDmgLinearPoints,
            1.0, BalanceStatic.CritDmgDrRate);

    /// <summary>
    /// Убывающая отдача уклонения: до порога очко даёт EvadeLinearRate, дальше — EvadeDrRate.
    /// Применяется к «очкам» хитча (хитрость + бонусы шмота).
    /// </summary>
    public static double ApplyEvadeDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.EvadeLinearPoints,
            BalanceStatic.EvadeLinearRate, BalanceStatic.EvadeDrRate);

    /// <summary>
    /// Убывающая отдача парирования: до порога очко даёт ParryLinearRate, дальше — ParryDrRate.
    /// Применяется к «очкам» ловкости (ловкость + бонусы шмота).
    /// </summary>
    public static double ApplyParryDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.ParryLinearPoints,
            BalanceStatic.ParryLinearRate, BalanceStatic.ParryDrRate);

    /// <summary>
    /// Убывающая отдача блока: до порога очко даёт BlockLinearRate, дальше — BlockDrRate.
    /// Применяется к «очкам» выносливости (выносливость + бонусы шмота).
    /// </summary>
    public static double ApplyBlockDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.BlockLinearPoints,
            BalanceStatic.BlockLinearRate, BalanceStatic.BlockDrRate);

    /// <summary>
    /// Убывающая отдача точности: до порога очко даёт AccuracyLinearRate, дальше — AccuracyDrRate.
    /// Применяется к «очкам» ловкости (ловкость + бонусы шмота).
    /// </summary>
    public static double ApplyAccuracyDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.AccuracyLinearPoints,
            BalanceStatic.AccuracyLinearRate, BalanceStatic.AccuracyDrRate);

    /// <summary>Убывающая отдача стойкости (выносливость).</summary>
    public static double ApplyTenacityDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.TenacityLinearPoints,
            BalanceStatic.TenacityLinearRate, BalanceStatic.TenacityDrRate);

    /// <summary>Убывающая отдача пробивания брони (сила).</summary>
    public static double ApplyArmorPenDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.ArmorPenLinearPoints,
            BalanceStatic.ArmorPenLinearRate, BalanceStatic.ArmorPenDrRate);

    /// <summary>Убывающая отдача сокращения перезарядки (мудрость).</summary>
    public static double ApplyCdrDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.CdrLinearPoints,
            BalanceStatic.CdrLinearRate, BalanceStatic.CdrDrRate);

    /// <summary>Убывающая отдача регенерации здоровья (выносливость).</summary>
    public static double ApplyHealthRegenDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.HealthRegenLinearPoints,
            BalanceStatic.HealthRegenLinearRate, BalanceStatic.HealthRegenDrRate);

    /// <summary>Убывающая отдача регенерации маны (мудрость).</summary>
    public static double ApplyManaRegenDiminishingReturns(double points)
        => ApplyDiminishingReturns(points, BalanceStatic.ManaRegenLinearPoints,
            BalanceStatic.ManaRegenLinearRate, BalanceStatic.ManaRegenDrRate);

    /// <summary>Общая убывающая отдача: linearPoints очков по linearRate за очко, дальше по drRate за очко.</summary>
    public static double ApplyDiminishingReturns(double points, double linearPoints, double linearRate, double drRate)
    {
        if (points <= linearPoints) return points * linearRate;
        return linearPoints * linearRate
            + (points - linearPoints) * drRate;
    }

    /// <summary>Процентная защита: DR = Defense / (Defense + K), кап 90%.</summary>
    public static double CalcDefenseReduction(double defense)
    {
        if (defense <= 0) return 0;
        double dr = defense / (defense + BalanceStatic.DefenseReductionK);
        return Math.Min(BalanceStatic.MaxDefenseReduction, dr);
    }

    /// <summary>Базовый урон: атака минус защита, не ниже MinDamage (плоский вариант, не используется боем).</summary>
    public static int CalcBaseDamage(int attack, int defense)
        => Math.Max(MinDamage, attack - defense);

    /// <summary>Урон с учётом Armor Penetration: процентная защита режет атаку, пробитие режет процент.</summary>
    public static int CalcDamageWithPen(int attack, int defense, double armorPen)
        => Math.Max(MinDamage, (int)(attack * (1.0 - CalcDefenseReduction(defense) * (1.0 - Math.Min(armorPen, 1.0)))));

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

    /// <summary>Рассчитывает итоговый урон по цепочке: атака→%защита→крит→редукция→блок.</summary>
    /// <param name="reduction">Процент снижения урона (0..1, обычно из CalcDefenseReduction).</param>
    public static int CalcFinalDamage(int rawAttack, double reduction,
        bool isCrit = false, double critMult = 1.5,
        int block = 0, double dmgReduction = 0)
    {
        int dmg = Math.Max(MinDamage, (int)(rawAttack * (1.0 - Math.Clamp(reduction, 0.0, 1.0))));
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
