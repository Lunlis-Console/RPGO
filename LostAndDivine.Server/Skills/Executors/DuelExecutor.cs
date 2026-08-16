using System;
using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.Skills.Executors;

/// <summary>SK0009 «ЭТО ДУЭЛЬ!» — 6 ударов (180%+15% за удар), наказание + оглушение при смене таргета.</summary>
public sealed class DuelExecutor : SkillExecutorBase
{
    public override int ComboIntervalMs => Balance.DuelHitIntervalMs;
    public override Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, int weaponRange)
        => ExecuteFirstHitPvE(pl, monster, skill, client, svc,
            Balance.DuelFirstHitMult, comboHitsRemaining: Balance.DuelHitCount - 1);

    public override Task<bool> ExecuteComboPvE(Player pl, Monster monster,
        ClientConnection client, CombatService svc)
        => ExecuteComboHitPvE(pl, monster, client, svc, Balance.DuelHitCount,
            hitNum => Balance.DuelFirstHitMult + (hitNum - 1) * Balance.DuelPerHitBonus, useOffHand: false);

    public override async Task<bool> CheckPunishPvE(Player pl, Monster monster,
        ClientConnection client, CombatService svc)
    {
        if (monster.AggroTarget == pl || monster.AggroTarget == null) return false;
        return await ApplyPunishDmg(pl, monster, client, svc);
    }

    public override async Task<bool> ExecutePvP(Player pl, Player target, Skill skill,
        ClientConnection atkClient, CombatService svc, int weaponRange, int dist)
    {
        var ok = await ExecuteFirstHitPvP(pl, target, skill, atkClient, svc, dist,
            Balance.DuelFirstHitMult, comboHitsRemaining: Balance.DuelHitCount - 1);
        pl.Combat.DuelPunishArmed = target.Combat.TargetPlayerId == pl.Id;
        return ok;
    }

    public override Task<bool> ExecuteComboPvP(Player pl, Player target,
        ClientConnection atkClient, CombatService svc)
        => ExecuteComboHitPvP(pl, target, atkClient, svc, Balance.DuelHitCount,
            hitNum => Balance.DuelFirstHitMult + (hitNum - 1) * Balance.DuelPerHitBonus);

    public override async Task<bool> CheckPunishPvP(Player pl, Player target,
        ClientConnection atkClient, CombatService svc)
    {
        if (!pl.Combat.DuelPunishArmed || target.Combat.TargetPlayerId == pl.Id) return false;
        return await ApplyPunishDmgPvP(pl, target, atkClient, svc);
    }

    // ───── PvE punish ─────

    private async Task<bool> ApplyPunishDmg(Player pl, Monster monster, ClientConnection client, CombatService svc)
    {
        int remaining = pl.Combat.PendingSkillHitsRemaining;
        pl.Combat.PendingSkillHitsRemaining = 0;
        pl.Combat.PendingSkillId = null;
        pl.Combat.PendingSkillTargetId = null;
        if (monster.Health <= 0) return true;

        await svc.SendPlayerAttack(pl.Name, "main", targetX: monster.X, targetY: monster.Y);
        var rng = Random.Shared;
        double effDef = svc.Monsters.GetEffectiveDefense(monster);
        double effAtk = svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
        bool evaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
        bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
        bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
        int hitDmg = 0;

        if (!evaded && !parried)
        {
            int baseDmg = Math.Max(Balance.MinDamage, (int)(effAtk - effDef));
            double mult = Balance.DuelPunishBaseMult + remaining * Balance.DuelPunishPerMissMult;
            hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * mult);
            hitDmg = svc.Monsters.ApplyDmgReduction(pl, hitDmg);
            if (blocked) hitDmg = 0;
            monster.Health -= hitDmg;
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker.AddOrUpdate(pl.Id, hitDmg, (k, old) => old + hitDmg);
        }

        if (evaded)
            await svc.ChatToC(client, "Бой", $"{monster.Name} уклонился от наказания «{pl.Name}»!");
        else if (parried)
            await svc.ChatToC(client, "Бой", $"{monster.Name} парировал наказание «{pl.Name}»!");
        else
        {
            string bt = blocked ? " (блок)" : "";
            await svc.ChatToC(client, "Бой", $"«ЭТО ДУЭЛЬ!» — наказание за смену таргета: {hitDmg} урона{bt} {monster.Name}!");
            await svc.SendDmgToMonster(client, monster, hitDmg, false, "main", pl, isSkill: true);
        }

        if (!evaded && !parried)
        {
            var stun = ActiveDebuff.Create(DebuffType.Stun, 0, Balance.DuelStunMs, "skill", "Оглушение (Дуэль)",
                $"Оглушение на {Balance.DuelStunMs / 1000} сек. — наказание за смену таргета.");
            svc.Debuffs.ApplyDebuff(monster, stun);
        }

        if (monster.Health <= 0)
        {
            var killMsg = !evaded ? KillDamageMsg(monster, hitDmg, false, "main") : null;
            await svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killMsg);
            return true;
        }
        await svc.SendToC(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
        return true;
    }

    // ───── PvP punish ─────

    private async Task<bool> ApplyPunishDmgPvP(Player pl, Player target, ClientConnection atkClient, CombatService svc)
    {
        int remaining = pl.Combat.PendingSkillHitsRemaining;
        pl.Combat.PendingSkillHitsRemaining = 0;
        pl.Combat.PendingSkillId = null;
        pl.Combat.PendingSkillTargetId = null;
        if (target.Health <= 0) return true;

        int dist = Math.Abs(pl.X - target.X) + Math.Abs(pl.Y - target.Y);
        await svc.SendPlayerAttack(pl.Name, "main", targetX: target.X, targetY: target.Y);

        bool evaded = Random.Shared.NextDouble() * 100 < target.GetEvadeChance();
        bool parried = !evaded && dist <= 1 && Random.Shared.NextDouble() * 100 < target.GetParryChance();
        bool blocked = !evaded && !parried && Random.Shared.NextDouble() * 100 < target.GetBlockChance();
        int hitDmg = 0;

        if (!evaded && !parried)
        {
            int rawDmg = Math.Max(Balance.MinDamage, pl.GetTotalAttack(dist) - target.GetTotalDefense());
            double mult = Balance.DuelPunishBaseMult + remaining * Balance.DuelPunishPerMissMult;
            hitDmg = (int)Math.Max(Balance.MinDamage, rawDmg * mult);
            if (blocked) hitDmg = 0;
            target.Health -= hitDmg;
            target.LastDamagedTime = DateTime.UtcNow;
        }

        if (evaded)
            await svc.ChatToC(atkClient, "Бой", $"{target.Name} уклонился от наказания «ЭТО ДУЭЛЬ!»!");
        else if (parried)
            await svc.ChatToC(atkClient, "Бой", $"{target.Name} парировал наказание «ЭТО ДУЭЛЬ!»!");
        else
        {
            string bt = blocked ? " (блок)" : "";
            await svc.ChatToC(atkClient, "Бой", $"«ЭТО ДУЭЛЬ!» — наказание за смену таргета: {hitDmg} урона{bt} {target.Name}!");
            var dmgMsg = new GameMessage { Type = "damage", Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = false, IsSkill = true } };
            await svc.SendToC(atkClient, dmgMsg);
            var tgtConn = svc.World.FindClientByPlayer(target);
            if (tgtConn != null)
            {
                await svc.SendToC(tgtConn, dmgMsg);
                await svc.ChatToC(tgtConn, "Бой", $"{pl.Name} наказал вас сменой таргета «ЭТО ДУЭЛЬ!»: {hitDmg} урона.");
            }
        }

        var targetClient = svc.World.FindClientByPlayer(target);
        if (targetClient != null)

        if (!evaded && !parried)
        {
            var stun = ActiveDebuff.Create(DebuffType.Stun, 0, Balance.DuelStunMs, "skill", "Оглушение (Дуэль)",
                $"Оглушение на {Balance.DuelStunMs / 1000} сек.");
            svc.Debuffs.ApplyDebuff(target, stun);
        }

        if (target.Health <= 0) return true;
        await svc.SendToC(atkClient, new GameMessage
        {
            Type = "combat_state", Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true }
        });
        if (targetClient != null)
            await svc.SendToC(targetClient, new GameMessage { Type = "combat_state", Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true } });
        return true;
    }
}
