using RPGGame.Server.Network;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Skills.Executors;

/// <summary>SK0012 «Прицельный выстрел» — 125% урон из лука.</summary>
public sealed class AimedShotExecutor : SkillExecutorBase
{
    public override async Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, int weaponRange)
    {
        if (!BowShotHelper.RequireBow(pl, client, svc, skill.Name)) return false;
        CommonPreHit(svc, pl, skill, client);
        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, monster.X, monster.Y);

        var (hitDmg, hitCrit, evaded, _, _) =
            BowShotHelper.CalculatePvE(pl, monster, svc, skill.DamageMultiplier, false, skill.Id);

        if (!evaded)
        {
            string visualType = pl.Equipment.GetWeaponCategory() == WeaponCategory.Bow ? "arrow" : "magic_bolt";
            var proj = svc.Projectiles.Spawn(pl, monster, visualType, hitDmg, hitCrit, "main", skill.Name);
            await svc.Projectiles.BroadcastSpawn(proj);
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
        await BowShotHelper.DealPvP(pl, target, atkClient, svc, skill.DamageMultiplier, false, skill.Name, skill.Id, dist);
        pl.Combat.LastAttackTime = DateTime.UtcNow;
        return true;
    }
}
