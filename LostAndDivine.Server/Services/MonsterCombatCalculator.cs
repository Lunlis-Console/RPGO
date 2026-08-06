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

    public double GetEffectiveDefense(ICombatant defender)
    {
        double armorPen = _svc.Debuffs.GetDebuffValue(defender, DebuffType.ArmorPenetration);
        return defender.GetTotalDefense() * (1.0 - Math.Min(armorPen, 1.0));
    }

    public int ApplyDmgReduction(ICombatant attacker, int baseDamage)
    {
        double dmgReduction = _svc.Debuffs.GetDebuffValue(attacker, DebuffType.DamageReduction);
        return Math.Max(Balance.MinDamage, (int)(baseDamage * (1.0 - Math.Min(dmgReduction, 1.0))));
    }

    public (int damageToTarget, int damageToAttacker, bool targetDead, bool isCrit, bool isEvaded, bool isParried, bool isBlocked)
        CalculateCombat(ICombatant attacker, ICombatant defender, bool applyDefenderDamage = true, bool isMelee = true)
    {
        int rolledAttack = attacker.RollAttackDamage();
        double effectiveAttack = GetEffectiveAttack(attacker, rolledAttack);
        double effectiveDefense = GetEffectiveDefense(defender);
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

        double defenderEvade = defender.GetEvadeChance() + accuracyReduction * 100 - passiveAccuracyBonus;
        if (isMelee && defender is Player plDef)
            defenderEvade += plDef.GetMeleeEvadeBonus();

        var (evaded, parried, blocked) = CombatMath.RollDefense(defenderEvade, defender.GetParryChance(), defender.GetBlockChance(), isMelee);

        if (evaded) return (0, 0, false, false, true, false, false);
        if (parried) return (0, 0, false, false, false, true, false);

        bool isCrit = Balance.RollPercent(attacker.GetCritChance() + passiveCritBonus);
        int damage = CombatMath.CalcFinalDamage(
            (int)effectiveAttack, (int)effectiveDefense,
            armorPen: armorPenExtra, isCrit, critMult: attacker.GetCritDamage(),
            block: blocked ? defender.GetBlockValue() : 0);
        damage = ApplyDmgReduction(attacker, damage);

        if (applyDefenderDamage && defender is Monster mon && !mon.ReturningToSpawn)
        {
            mon.Health -= damage;
            mon.LastDamagedTime = DateTime.UtcNow;
            if (attacker is Player pl)
                mon.DamageTracker.AddOrUpdate(pl.Id, damage, (k, old) => old + damage);
        }
        bool targetDead = defender.Health <= 0;

        return (damage, 0, targetDead, isCrit, false, false, blocked);
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
        int baseDmg = Math.Max(Balance.MinDamage, (int)(effectiveAttack - GetEffectiveDefense(target)));
        int finalDmg = CombatMath.ApplyCrit(baseDmg, crit, attacker.GetCritDamage());
        finalDmg = Math.Max(Balance.MinDamage, (int)(finalDmg * attacker.GetOffHandDamageFraction()));
        if (blocked) finalDmg = CombatMath.ApplyBlock(finalDmg, target.GetBlockValue());

        return (finalDmg, crit, false, false, blocked);
    }

    public void CalculateCleave(Player attacker, Monster primaryTarget, Func<int, int, Monster?> findMonster)
    {
        var positions = GetCleavePositions(attacker.X, attacker.Y, attacker.Facing);
        double effectiveAttack = GetEffectiveAttack(attacker, attacker.GetMaxAttackDamage());
        int cleaveDmg = Math.Max(Balance.MinDamage,
            (int)((effectiveAttack - GetEffectiveDefense(primaryTarget)) * Balance.CleaveDamageFraction));

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
