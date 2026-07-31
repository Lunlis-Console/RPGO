using System;
using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.Skills;

/// <summary>Хелперы для комбо-навыков: общая PvE-механика ударов.</summary>
public abstract class SkillExecutorBase : BaseSkillExecutor
{
    protected static async Task<bool> ExecuteFirstHitPvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, double dmgMult, int comboHitsRemaining,
        Action<Player, Monster, int, bool, Random>? sideEffect = null)
    {
        CommonPreHit(svc, pl, skill, client);
        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, monster.X, monster.Y);

        double rankMult = pl.GetSkillRankDmgMult(skill.Id);
        dmgMult *= rankMult;

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
            if (hitCrit) hitDmg = (int)(hitDmg * (pl.GetCritDamage() + 0.2));
            hitDmg = svc.Monsters.ApplyDmgReduction(pl, hitDmg);
            if (blocked) hitDmg = Math.Max(Balance.MinDamage, hitDmg - monster.GetBlockValue());
            monster.Health -= hitDmg;
            await svc.TryLifesteal(pl, hitDmg, true, client);
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker[pl.Id] = monster.DamageTracker.GetValueOrDefault(pl.Id) + hitDmg;
            sideEffect?.Invoke(pl, monster, hitDmg, hitCrit, rng);
        }

        if (evaded)
            await svc.ChatToC(client, "Бой", $"{monster.Name} уклонился от первого удара «{skill.Name}».");
        else if (parried)
            await svc.ChatToC(client, "Бой", $"{monster.Name} парировал первый удар «{skill.Name}»!");
        else
        {
            string critT = hitCrit ? " (КРИТ!)" : "", blockT = blocked ? " (блок)" : "";
            await svc.ChatToC(client, "Бой", $"«{skill.Name}» — первый удар: {hitDmg} урона{critT}{blockT} {monster.Name}.");
            await svc.SendDmgToMonster(client, monster, hitDmg, hitCrit, "main", pl, isSkill: true);
        }

        pl.Combat.LastAttackTime = DateTime.UtcNow;
        if (monster.Health <= 0)
        {
            var killMsg = !evaded ? KillDamageMsg(monster, hitDmg, hitCrit, "main") : null;
            await svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killMsg);
            return true;
        }

        await svc.SendToC(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));

        pl.Combat.PendingSkillHitsRemaining = comboHitsRemaining;
        pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
        pl.Combat.PendingSkillTargetId = monster.Id;
        pl.Combat.PendingSkillId = skill.Id;
        return true;
    }

    protected static async Task<bool> ExecuteComboHitPvE(Player pl, Monster monster,
        ClientConnection client, CombatService svc, int totalHits,
        Func<int, double> hitMultFn, bool useOffHand,
        Action<Player, Monster, int, bool, Random>? sideEffect = null)
    {
        pl.Combat.PendingSkillHitsRemaining--;
        bool more = pl.Combat.PendingSkillHitsRemaining > 0;
        if (!more) pl.Combat.PendingSkillTargetId = null;
        if (monster.Health <= 0) return false;

        int hitNumber = totalHits - pl.Combat.PendingSkillHitsRemaining;
        double dmgMult = hitMultFn(hitNumber);
        string skillId = pl.Combat.PendingSkillId ?? "";
        dmgMult *= pl.GetSkillRankDmgMult(skillId);
        string hitHand = useOffHand ? "off" : "main";

        await svc.SendPlayerAttack(pl.Name, hitHand, pl.Combat.PendingSkillId,
            targetX: monster.X, targetY: monster.Y);

        var rng = Random.Shared;
        double effDef = svc.Monsters.GetEffectiveDefense(monster);
        double effAtk = useOffHand
            ? svc.Monsters.GetEffectiveAttack(pl, pl.RollOffHandDamage())
            : svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
        bool evaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
        bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
        bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
        int hitDmg = 0; bool hitCrit = false;

        if (!evaded && !parried)
        {
            hitCrit = rng.Next(Balance.ChanceRollMax) < pl.GetCritChance();
            int baseDmg = Math.Max(Balance.MinDamage, (int)(effAtk - effDef));
            hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * dmgMult);
            if (hitCrit) hitDmg = (int)(hitDmg * (pl.GetCritDamage() + 0.2));
            if (useOffHand)
            {
                double offFrac = pl.LearnedSkills.Contains("SK0003") ? 0.75 : Equipment.OffHandDamageFraction;
                hitDmg = Math.Max(Balance.MinDamage, (int)(hitDmg * offFrac));
            }
            hitDmg = svc.Monsters.ApplyDmgReduction(pl, hitDmg);
            if (blocked) hitDmg = Math.Max(Balance.MinDamage, hitDmg - monster.GetBlockValue());
            monster.Health -= hitDmg;
            await svc.TryLifesteal(pl, hitDmg, true, client);
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker[pl.Id] = monster.DamageTracker.GetValueOrDefault(pl.Id) + hitDmg;
            sideEffect?.Invoke(pl, monster, hitDmg, hitCrit, rng);
        }

        string skillName = pl.Combat.PendingSkillId switch
        {
            "SK0004" => "Разрез",
            "SK0007" => "Святая троица",
            "SK0009" => "ЭТО ДУЭЛЬ!",
            _ => "???"
        };

        if (evaded)
            await svc.ChatToC(client, "Бой", $"{monster.Name} уклонился от удара «{skillName}».");
        else if (parried)
            await svc.ChatToC(client, "Бой", $"{monster.Name} парировал удар «{skillName}»!");
        else
        {
            string critT = hitCrit ? " (КРИТ!)" : "", blockT = blocked ? " (блок)" : "";
            string hLabel = useOffHand ? " — левая рука" : "";
            await svc.ChatToC(client, "Бой", $"«{skillName}»{hLabel}: {hitDmg} урона{critT}{blockT} {monster.Name}.");
            await svc.SendDmgToMonster(client, monster, hitDmg, hitCrit, hitHand, pl, isSkill: true);
        }

        pl.Combat.LastAttackTime = DateTime.UtcNow;

        if (monster.Health <= 0)
        {
            var killMsg = !evaded ? KillDamageMsg(monster, hitDmg, hitCrit, hitHand) : null;
            await svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killMsg);
            return true;
        }

        if (more) pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
        await svc.SendToC(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
        await svc.SendToC(client, new GameMessage
        {
            Type = "combat_state",
            Data = new { InCombat = true, TargetId = monster.Id.ToString(), TargetName = monster.Name, TargetHp = monster.Health, TargetMaxHp = monster.MaxHealth }
        });

        return true;
    }

    protected static void CommonPreHit(CombatService svc, Player pl, Skill skill, ClientConnection client)
    {
        pl.Mana = Math.Max(0, pl.Mana - skill.MpCost);
        pl.LastSkillUse[skill.Id] = DateTime.UtcNow;
        pl.QueuedSkillIds.RemoveAt(0);
        _ = Server.MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, svc.Hub);
        _ = svc.SendSkillCooldown(client, skill, pl.GetSkillRankCdMult(skill.Id));
    }

    protected static GameMessage KillDamageMsg(Monster monster, int hitDmg, bool hitCrit, string hand) => new()
    {
        Type = "damage",
        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + hitDmg), IsCrit = hitCrit, Hand = hand, IsSkill = true }
    };

    // ───── PvP: первый удар + настройка комбо ─────

    protected static async Task<bool> ExecuteFirstHitPvP(Player pl, Player target, Skill skill,
        ClientConnection atkClient, CombatService svc, int dist, double dmgMult, int comboHitsRemaining)
    {
        CommonPreHit(svc, pl, skill, atkClient);
        pl.Combat.LastAttackTime = DateTime.UtcNow;

        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, target.X, target.Y);

        double rankMult = pl.GetSkillRankDmgMult(skill.Id);
        dmgMult *= rankMult;

        bool evaded = Random.Shared.NextDouble() * 100 < target.GetEvadeChance();
        bool parried = !evaded && dist <= 1 && Random.Shared.NextDouble() * 100 < target.GetParryChance();
        bool blocked = !evaded && !parried && Random.Shared.NextDouble() * 100 < target.GetBlockChance();
        int hitDmg = 0; bool hitCrit = false;

        if (!evaded && !parried)
        {
            int rawDmg = Math.Max(Balance.MinDamage, pl.GetTotalAttack(dist) - target.GetTotalDefense());
            hitDmg = (int)Math.Max(Balance.MinDamage, rawDmg * dmgMult);
            hitCrit = Random.Shared.NextDouble() * 100 < pl.GetCritChance();
            if (hitCrit) hitDmg = (int)(hitDmg * pl.GetCritDamage());
            if (blocked) hitDmg = Math.Max(Balance.MinDamage, hitDmg - target.GetBlockValue());
            target.Health -= hitDmg;
            await svc.TryLifesteal(pl, hitDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;
        }

        if (evaded)
            await svc.ChatToC(atkClient, "Бой", $"{target.Name} уклонился от первого удара «{skill.Name}».");
        else if (parried)
            await svc.ChatToC(atkClient, "Бой", $"{target.Name} парировал первый удар «{skill.Name}»!");
        else
        {
            string critT = hitCrit ? " (КРИТ!)" : "", blockT = blocked ? " (блок)" : "";
            await svc.ChatToC(atkClient, "Бой", $"«{skill.Name}» — первый удар: {hitDmg} урона{critT}{blockT} {target.Name}.");
            var dmgMsg = new GameMessage { Type = "damage", Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit, IsSkill = true } };
            await svc.SendToC(atkClient, dmgMsg);
            await svc.SendDmgNearbyTo(dmgMsg, target);
        }

        var targetClient = svc.World.FindClientByPlayer(target);
        if (targetClient != null)
        {
            await svc.SendToC(targetClient, new GameMessage { Type = "damage", Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit, IsSkill = true } });
            await svc.ChatToC(targetClient, "Бой", $"{pl.Name} применил «{skill.Name}»: {hitDmg} урона вам.");
            await svc.SendMyStatus(targetClient, target);
        }

        if (target.Health <= 0)
        {
            await HandlePvPDeathStatic(pl, target, atkClient, svc);
            return true;
        }

        await svc.SendMyStatus(atkClient, pl);
        PvPCombatState(targetClient, svc, pl);

        pl.Combat.PendingSkillHitsRemaining = comboHitsRemaining;
        pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
        pl.Combat.PendingSkillTargetId = target.Id;
        pl.Combat.PendingSkillId = skill.Id;
        return true;
    }

    // ───── PvP: продолжение комбо-удара ─────

    protected static async Task<bool> ExecuteComboHitPvP(Player pl, Player target,
        ClientConnection atkClient, CombatService svc, int totalHits,
        Func<int, double> hitMultFn)
    {
        pl.Combat.PendingSkillHitsRemaining--;
        bool more = pl.Combat.PendingSkillHitsRemaining > 0;
        if (!more) pl.Combat.PendingSkillTargetId = null;
        if (target.Health <= 0) return false;

        int hitNumber = totalHits - pl.Combat.PendingSkillHitsRemaining;
        double dmgMult = hitMultFn(hitNumber);
        string skillId = pl.Combat.PendingSkillId ?? "";
        dmgMult *= pl.GetSkillRankDmgMult(skillId);
        int dist = Math.Abs(pl.X - target.X) + Math.Abs(pl.Y - target.Y);

        await svc.SendPlayerAttack(pl.Name, "main", pl.Combat.PendingSkillId,
            targetX: target.X, targetY: target.Y);

        bool evaded = Random.Shared.NextDouble() * 100 < target.GetEvadeChance();
        bool parried = !evaded && dist <= 1 && Random.Shared.NextDouble() * 100 < target.GetParryChance();
        bool blocked = !evaded && !parried && Random.Shared.NextDouble() * 100 < target.GetBlockChance();
        int hitDmg = 0; bool hitCrit = false;

        if (!evaded && !parried)
        {
            int rawDmg = Math.Max(Balance.MinDamage, pl.GetTotalAttack(dist) - target.GetTotalDefense());
            hitDmg = (int)Math.Max(Balance.MinDamage, rawDmg * dmgMult);
            hitCrit = Random.Shared.NextDouble() * 100 < pl.GetCritChance();
            if (hitCrit) hitDmg = (int)(hitDmg * pl.GetCritDamage());
            if (blocked) hitDmg = Math.Max(Balance.MinDamage, hitDmg - target.GetBlockValue());
            target.Health -= hitDmg;
            await svc.TryLifesteal(pl, hitDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;
        }

        string skillName = pl.Combat.PendingSkillId switch { "SK0004" => "Разрез", "SK0007" => "Святая троица", _ => "???" };
        if (evaded)
            await svc.ChatToC(atkClient, "Бой", $"{target.Name} уклонился от удара «{skillName}».");
        else if (parried)
            await svc.ChatToC(atkClient, "Бой", $"{target.Name} парировал удар «{skillName}»!");
        else
        {
            string critT = hitCrit ? " (КРИТ!)" : "", blockT = blocked ? " (блок)" : "";
            await svc.ChatToC(atkClient, "Бой", $"«{skillName}»: {hitDmg} урона{critT}{blockT} {target.Name}.");
            var dmgMsg = new GameMessage { Type = "damage", Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit, IsSkill = true } };
            await svc.SendToC(atkClient, dmgMsg);
            await svc.SendDmgNearbyTo(dmgMsg, target);
        }

        var targetClient = svc.World.FindClientByPlayer(target);
        if (targetClient != null)
        {
            await svc.SendToC(targetClient, new GameMessage { Type = "damage", Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit, IsSkill = true } });
            await svc.ChatToC(targetClient, "Бой", $"{pl.Name}: {hitDmg} урона «{skillName}».");
            await svc.SendMyStatus(targetClient, target);
        }

        pl.Combat.LastAttackTime = DateTime.UtcNow;
        if (target.Health <= 0) { await HandlePvPDeathStatic(pl, target, atkClient, svc); return true; }
        if (more) pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;

        await svc.SendMyStatus(atkClient, pl);
        PvPCombatState(targetClient, svc, pl);
        return true;
    }

    // ───── PvP хелперы ─────

    private static async Task HandlePvPDeathStatic(Player killer, Player victim, ClientConnection killerClient, CombatService svc)
    {
        victim.Combat.Cancel(); victim.Interaction.Clear(); victim.Movement.Stop();
        victim.IsDead = true; victim.DeathTime = DateTime.UtcNow;
        int lostGold = Balance.ComputeDeathGoldLoss(victim.Gold);
        victim.Gold -= lostGold;
        var vc = svc.World.FindClientByPlayer(victim);
        if (vc != null)
        {
            await svc.SendToC(vc, GameMessage.ResetCombat());
            await svc.SendToC(vc, GameMessage.PlayerDeath(lostGold));
            await svc.ChatToC(vc, "Система", $"Вы погибли в PvP от {killer.Name}! Потеряно {lostGold} золота.");
        }
        if (killerClient != null)
            await svc.ChatToC(killerClient, "Система", $"Вы победили {victim.Name} в PvP!");
    }

    private static void PvPCombatState(ClientConnection? targetClient, CombatService svc, Player pl)
    {
        if (targetClient != null)
            _ = svc.SendToC(targetClient, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true }
            });
    }
}
