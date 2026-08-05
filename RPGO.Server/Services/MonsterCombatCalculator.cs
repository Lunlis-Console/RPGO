using RPGGame.Shared;
using RPGGame.Shared.Models;

namespace RPGGame.Server;

public class MonsterCombatCalculator
{
    private readonly Lazy<GameServices> _svcLazy;
    private GameServices _svc => _svcLazy.Value;

    public MonsterCombatCalculator(Lazy<GameServices> svc)
    {
        _svcLazy = svc;
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
        double effectiveAttackerAttack = GetEffectiveAttack(attacker, rolledAttack);
        double effectiveDefenderDefense = GetEffectiveDefense(defender);
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

        double evadeChance = defender.GetEvadeChance() + accuracyReduction * 100 - passiveAccuracyBonus;
        if (isMelee && defender is Player plDef)
            evadeChance += plDef.GetMeleeEvadeBonus();

        bool defenderEvaded = Balance.RollPercent(evadeChance);
        if (defenderEvaded)
            return (0, 0, false, false, true, false, false);

        bool defenderParried = isMelee && Balance.RollPercent(defender.GetParryChance());
        if (defenderParried)
            return (0, 0, false, false, false, true, false);

        bool defenderBlocked = Balance.RollPercent(defender.GetBlockChance());
        int attackerDamage = 0;
        bool isCrit = Balance.RollPercent(attacker.GetCritChance() + passiveCritBonus);
        double def = effectiveDefenderDefense * (1.0 - Math.Min(armorPenExtra, 1.0));
        int baseDamage = Math.Max(Balance.MinDamage, (int)(effectiveAttackerAttack - def));
        attackerDamage = isCrit ? (int)(baseDamage * attacker.GetCritDamage()) : baseDamage;
        attackerDamage = ApplyDmgReduction(attacker, attackerDamage);

        if (defenderBlocked)
        {
            int blockValue = defender.GetBlockValue();
            attackerDamage = Math.Max(Balance.MinDamage, attackerDamage - blockValue);
        }

        if (applyDefenderDamage && defender is Monster mon && !mon.ReturningToSpawn)
        {
            mon.Health -= attackerDamage;
            mon.LastDamagedTime = DateTime.UtcNow;
            if (attacker is Player pl)
                mon.DamageTracker.AddOrUpdate(pl.Id, attackerDamage, (k, old) => old + attackerDamage);
        }
        bool targetDead = defender.Health <= 0;

        return (attackerDamage, 0, targetDead, isCrit, false, false, defenderBlocked);
    }

    public (int damage, bool isCrit, bool isEvaded, bool isParried, bool isBlocked)
        CalculateOffHandAttack(Player attacker, Monster target)
    {
        if (!attacker.Equipment.IsDualWielding()) return (0, false, false, false, false);

        bool evaded = Balance.RollPercent(target.GetEvadeChance());
        if (evaded) return (0, false, true, false, false);

        bool parried = Balance.RollPercent(target.GetParryChance());
        if (parried) return (0, false, false, true, false);

        bool blocked = Balance.RollPercent(target.GetBlockChance());

        bool crit = Balance.RollPercent(attacker.GetCritChance());
        double effectiveAttack = GetEffectiveAttack(attacker, attacker.RollOffHandDamage());
        int baseDmg = Math.Max(Balance.MinDamage, (int)(effectiveAttack - GetEffectiveDefense(target)));
        int finalDmg = crit ? (int)(baseDmg * attacker.GetCritDamage()) : baseDmg;
        double offHandFraction = attacker.GetOffHandDamageFraction();
        finalDmg = Math.Max(Balance.MinDamage, (int)(finalDmg * offHandFraction));

        if (blocked)
        {
            int blockValue = target.GetBlockValue();
            finalDmg = Math.Max(Balance.MinDamage, finalDmg - blockValue);
        }

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
