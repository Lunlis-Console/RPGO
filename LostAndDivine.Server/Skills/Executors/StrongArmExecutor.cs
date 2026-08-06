using System;
using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Skills.Executors;

/// <summary>SK0001 «Крепкая рука» — усиленный удар + шанс оглушения. Только ближний бой.</summary>
public sealed class StrongArmExecutor : SkillExecutorBase
{
    public override async Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, int weaponRange)
    {
        if (weaponRange > 1)
        {
            await svc.ChatToC(client, "Бой", "«Крепкая рука» доступен только с оружием ближнего боя.");
            return false;
        }

        CommonPreHit(svc, pl, skill, client);
        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, monster.X, monster.Y);

        double dmgMult = skill.DamageMultiplier * pl.GetSkillRankDmgMult(skill.Id);
        var rng = Random.Shared;
        double effDef = svc.Monsters.GetEffectiveDefense(monster);
        double effAtk = svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
        bool evaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
        bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
        bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
        int hitDmg = 0; bool hitCrit = false;

        if (!evaded && !parried)
        {
            hitCrit = rng.Next(Balance.ChanceRollMax) < pl.GetCritChance();
            int baseDmg = Math.Max(Balance.MinDamage, (int)(effAtk - effDef));
            hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * dmgMult);
            if (hitCrit) hitDmg = (int)(hitDmg * pl.GetCritDamage());
            hitDmg = svc.Monsters.ApplyDmgReduction(pl, hitDmg);
            if (blocked) hitDmg = Math.Max(Balance.MinDamage, hitDmg - monster.GetBlockValue());
            monster.Health -= hitDmg;
            await svc.TryLifesteal(pl, hitDmg, true, client);
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker.AddOrUpdate(pl.Id, hitDmg, (k, old) => old + hitDmg);

            if (hitDmg > 0 && rng.Next(Balance.ChanceRollMax) < Balance.StunChanceOnHit)
            {
                var stun = ActiveDebuff.Create(DebuffType.Stun, 0,
                    Balance.StunDurationMs, "skill", "Оглушение",
                    $"Оглушение на {Balance.StunDurationMs / 1000} сек.");
                svc.Debuffs.ApplyDebuff(monster, stun);
                await svc.ChatToC(client, "Бой",
                    $"«Крепкая рука» оглушил {monster.Name} на {Balance.StunDurationMs / 1000} сек.!");
                await svc.SendTargetDebuffUpdateAsync(monster);
            }
        }

        pl.Combat.LastAttackTime = DateTime.UtcNow;

        if (evaded)
            await svc.ChatToC(client, "Бой", $"{monster.Name} уклонился от «{skill.Name}».");
        else if (parried)
            await svc.ChatToC(client, "Бой", $"{monster.Name} парировал «{skill.Name}»!");
        else
        {
            string critT = hitCrit ? " (КРИТ!)" : "", blockT = blocked ? " (блок)" : "";
            await svc.ChatToC(client, "Бой", $"«{skill.Name}»: {hitDmg} урона{critT}{blockT} {monster.Name}.");
            await svc.SendDmgToMonster(client, monster, hitDmg, hitCrit, "main", pl, isSkill: true);
        }

        if (monster.Health <= 0)
        {
            if (monster.IsMannequin)
            {
                monster.Health = monster.MaxHealth;
                await svc.ChatToC(client, "Бой", $"Манекен восстановил все HP!{(hitCrit ? " (КРИТ!)" : "")}");
                await svc.SendToC(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                return true;
            }
            var killMsg = !evaded ? KillDamageMsg(monster, hitDmg, hitCrit, "main") : null;
            await svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killMsg);
            return true;
        }

        await svc.SendToC(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
        return true;
    }

    public override async Task<bool> ExecutePvP(Player pl, Player target, Skill skill,
        ClientConnection atkClient, CombatService svc, int weaponRange, int dist)
    {
        if (weaponRange > 1 || dist > 1)
        {
            await svc.ChatToC(atkClient, "Бой", "«Крепкая рука» доступен только в ближнем бою.");
            return false;
        }

        return await ExecuteFirstHitPvP(pl, target, skill, atkClient, svc, dist,
            skill.DamageMultiplier, comboHitsRemaining: 0);
    }
}
