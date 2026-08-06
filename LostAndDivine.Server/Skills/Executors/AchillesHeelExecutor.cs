using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Skills.Executors;

/// <summary>SK0013 «Ахиллесова пята» — 110% + Root.</summary>
public sealed class AchillesHeelExecutor : SkillExecutorBase
{
    public override async Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, int weaponRange)
    {
        if (!BowShotHelper.RequireBow(pl, client, svc, skill.Name)) return false;
        CommonPreHit(svc, pl, skill, client);
        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, monster.X, monster.Y);

        var (hitDmg, hitCrit, evaded, _, _) =
            BowShotHelper.CalculatePvE(pl, monster, svc, skill.DamageMultiplier, true, skill.Id);

        if (!evaded)
        {
            string visualType = pl.Equipment.GetWeaponCategory() == WeaponCategory.Bow ? "arrow" : "magic_bolt";
            var proj = svc.Projectiles.Spawn(pl, monster, visualType, hitDmg, hitCrit, "main", skill.Name);
            await svc.Projectiles.BroadcastSpawn(proj);
        }

        if (!evaded && hitDmg > 0)
        {
            int rootMs = (int)(Balance.AchillesRootMs * (1.0 + (pl.GetSkillRank(skill.Id) - 1) * 0.15));
            var root = ActiveDebuff.Create(DebuffType.Root, 0, rootMs, "skill", "Обездвижен",
                $"Обездвижен на {rootMs / 1000} сек.");
            svc.Debuffs.ApplyDebuff(monster, root);
            await svc.ChatToC(client, "Бой", $"«{skill.Name}» обездвижил {monster.Name}!");
            await svc.SendTargetDebuffUpdateAsync(monster);
        }

        pl.Combat.LastAttackTime = DateTime.UtcNow;
        return true;
    }

    public override async Task<bool> ExecutePvP(Player pl, Player target, Skill skill,
        ClientConnection atkClient, CombatService svc, int weaponRange, int dist)
    {
        if (!BowShotHelper.RequireBow(pl, atkClient, svc, skill.Name)) return false;
        CommonPreHit(svc, pl, skill, atkClient);
        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, target.X, target.Y);
        var (hitDmg, _, evaded) =
            await BowShotHelper.DealPvP(pl, target, atkClient, svc, skill.DamageMultiplier, false, skill.Name, skill.Id, dist);
        if (!evaded && hitDmg > 0)
        {
            int rootMs = (int)(Balance.AchillesRootMs * (1.0 + (pl.GetSkillRank(skill.Id) - 1) * 0.15));
            var root = ActiveDebuff.Create(DebuffType.Root, 0, rootMs, "skill", "Обездвижен",
                $"Обездвижен на {rootMs / 1000} сек.");
            svc.Debuffs.ApplyDebuff(target, root);
            await svc.ChatToC(atkClient, "Бой", $"«{skill.Name}» обездвижил {target.Name}!");
        }
        pl.Combat.LastAttackTime = DateTime.UtcNow;
        return true;
    }
}
