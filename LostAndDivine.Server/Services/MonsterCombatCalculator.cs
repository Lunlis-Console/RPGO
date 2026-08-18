using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server;

public class MonsterCombatCalculator
{
    private readonly IGameServices _svc;

    public MonsterCombatCalculator(IGameServices svc)
    {
        _svc = svc;
    }

    public double GetEffectiveAttack(ICombatant attacker, int baseAttack)
    {
        double dmgBonus = _svc.Debuffs.GetDebuffValue(attacker, DebuffType.DamageBonus);
        double mult = 1.0 + dmgBonus;
        if (attacker is Player p) mult *= p.GetBerserkMultiplier();
        return baseAttack * mult;
    }

    public double GetEffectiveAttack(ICombatant attacker)
        => GetEffectiveAttack(attacker, attacker.GetTotalAttack());

    /// <summary>
    /// Процент снижения урона защитой цели (0..кап 90%): физ. защита или маг. сопротивление,
    /// с учётом пробития брони (дебак + параметр). Возвращает именно долю 0..1, а не целую защиту.
    /// </summary>
    public double GetEffectiveDefense(ICombatant defender, double armorPen = 0, bool magic = false)
    {
        double debuffPen = _svc.Debuffs.GetDebuffValue(defender, DebuffType.ArmorPenetration);
        double totalPen = Math.Min(1.0, debuffPen + Math.Max(0, armorPen));
        int raw = magic ? defender.GetTotalResistance() : defender.GetTotalDefense();
        return CombatMath.CalcDefenseReduction(raw) * (1.0 - totalPen);
    }

    public int ApplyDmgReduction(ICombatant attacker, int baseDamage)
    {
        double dmgReduction = _svc.Debuffs.GetDebuffValue(attacker, DebuffType.DamageReduction);
        return Math.Max(Balance.MinDamage, (int)(baseDamage * (1.0 - Math.Min(dmgReduction, 1.0))));
    }

    public (int damageToTarget, int damageToAttacker, bool targetDead, bool isCrit, bool isEvaded, bool isParried, bool isBlocked)
        CalculateCombat(ICombatant attacker, ICombatant defender, bool applyDefenderDamage = true, bool isMelee = true)
    {
        var (damage, isCrit, isEvaded, isParried, isBlocked) =
            RollAttack(attacker, defender, attacker.RollAttackDamage(), 1.0, isMelee);

        if (isEvaded) return (0, 0, false, false, true, false, false);
        if (isParried) return (0, 0, false, false, false, true, false);

        if (applyDefenderDamage && defender is Monster mon && !mon.ReturningToSpawn)
        {
            mon.Health -= damage;
            mon.LastDamagedTime = DateTime.UtcNow;
            if (attacker is Player pl)
                mon.DamageTracker.AddOrUpdate(pl.Id, damage, (k, old) => old + damage);
        }
        bool targetDead = defender.Health <= 0;

        return (damage, 0, targetDead, isCrit, false, false, isBlocked);
    }

    /// <summary>
    /// Полная цепочка урона одного удара: атака→защита (с пробитием)→крит→редукция→блок.
    /// Используется PvP-боем, чтобы игроки били друг друга по тем же правилам, что и монстров.
    /// </summary>
    public (int damage, bool isCrit, bool isEvaded, bool isParried, bool isBlocked)
        RollAttack(ICombatant attacker, ICombatant defender, int baseAttack, double attackFraction, bool isMelee = true)
    {
        double effectiveAttack = GetEffectiveAttack(attacker, baseAttack);
        double accuracyReduction = _svc.Debuffs.GetDebuffValue(attacker, DebuffType.AccuracyReduction);

        double passiveAccuracyBonus = 0;
        double passiveCritBonus = 0;
        double armorPenExtra = 0;
        if (attacker is Player plPassive)
        {
            if (plPassive.LearnedSkills.Contains(SkillIds.WarriorsFocus)
                && _svc.Debuffs.HasDebuff(defender, DebuffType.Stun))
            {
                passiveAccuracyBonus = 10;
                passiveCritBonus = 10;
            }
            passiveAccuracyBonus += plPassive.GetBowAccuracyBonus();
            passiveCritBonus += plPassive.GetHunterInstinctCritBonus(defender);
            armorPenExtra = plPassive.GetCloseRangeArmorPen(isMelee ? 1 : 3);
        }

        double effectiveDefense = GetEffectiveDefense(defender, armorPenExtra, attacker.IsMagicalDamage());

        double defenderEvade = defender.GetEvadeChance() + accuracyReduction * 100 - passiveAccuracyBonus;
        if (isMelee && defender is Player plDef)
            defenderEvade += plDef.GetMeleeEvadeBonus();

        var (evaded, parried, blocked) = CombatMath.RollDefense(defenderEvade, defender.GetParryChance(), defender.GetBlockChance(), isMelee);

        if (evaded) return (0, false, true, false, false);
        if (parried) return (0, false, false, true, false);

        bool isCrit = Balance.RollPercent(Math.Min(BalanceStatic.MaxCritChance, attacker.GetCritChance() + passiveCritBonus));
        int damage = CombatMath.CalcFinalDamage(
            (int)effectiveAttack, effectiveDefense,
            isCrit, critMult: attacker.GetCritDamage(),
            block: 0);
        if (blocked) damage = 0;
        damage = ApplyDmgReduction(attacker, damage);
        if (attackFraction != 1.0)
            damage = Math.Max(Balance.MinDamage, (int)(damage * attackFraction));

        return (damage, isCrit, false, false, blocked);
    }

    public (int damage, bool isCrit, bool isEvaded, bool isParried, bool isBlocked)
        CalculateOffHandAttack(Player attacker, Monster target)
    {
        if (!attacker.Equipment.IsDualWielding()) return (0, false, false, false, false);

        var (evaded, parried, blocked) = CombatMath.RollDefense(
            target.GetEvadeChance(), target.GetParryChance(), target.GetBlockChance());

        if (evaded) return (0, false, true, false, false);
        if (parried) return (0, false, false, true, false);

        bool crit = Balance.RollPercent(attacker.GetCritChance());
        double effectiveAttack = GetEffectiveAttack(attacker, attacker.RollOffHandDamage());
        double reduction = GetEffectiveDefense(target, 0, attacker.IsOffHandMagical());
        int baseDmg = Math.Max(Balance.MinDamage, (int)(effectiveAttack * (1.0 - reduction)));
        int finalDmg = CombatMath.ApplyCrit(baseDmg, crit, attacker.GetCritDamage());
        finalDmg = Math.Max(Balance.MinDamage, (int)(finalDmg * attacker.GetOffHandDamageFraction()));
        if (blocked) finalDmg = 0;

        return (finalDmg, crit, false, false, blocked);
    }

    public void CalculateCleave(Player attacker, Monster primaryTarget, Func<int, int, Monster?> findMonster)
    {
        var positions = GetCleavePositions(attacker.X, attacker.Y, attacker.Facing);
        double effectiveAttack = GetEffectiveAttack(attacker, attacker.GetMaxAttackDamage());
        double reduction = GetEffectiveDefense(primaryTarget, 0, attacker.IsMagicalDamage());
        int cleaveDmg = Math.Max(Balance.MinDamage,
            (int)(effectiveAttack * (1.0 - reduction) * Balance.CleaveDamageFraction));

        foreach (var (cx, cy) in positions)
        {
            var monster = findMonster(cx, cy);
            if (monster == null || monster.Id == primaryTarget.Id || monster.Health <= 0) continue;

            bool evaded = Balance.RollPercent(monster.GetEvadeChance());
            if (evaded) continue;

            bool crit = Balance.RollPercent(attacker.GetCritChance());
            int dmg = crit ? (int)(cleaveDmg * attacker.GetCritDamage()) : cleaveDmg;
            dmg = Math.Max(Balance.MinDamage, dmg);
            monster.Health -= dmg;
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker.AddOrUpdate(attacker.Id, dmg, (k, old) => old + dmg);
        }
    }

    private static List<(int x, int y)> GetCleavePositions(int px, int py, string facing)
    {
        return facing switch
        {
            "up"    => new List<(int, int)> { (px - 1, py - 1), (px, py - 1), (px + 1, py - 1) },
            "down"  => new List<(int, int)> { (px - 1, py + 1), (px, py + 1), (px + 1, py + 1) },
            "left"  => new List<(int, int)> { (px - 1, py - 1), (px - 1, py), (px - 1, py + 1) },
            "right" => new List<(int, int)> { (px + 1, py - 1), (px + 1, py), (px + 1, py + 1) },
            _       => new List<(int, int)>()
        };
    }
}
