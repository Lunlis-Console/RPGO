using RPGGame.Server.Network;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Skills.Executors;

/// <summary>SK0015 «Подавляющий огонь» — бафф конусных атак на 10 сек.</summary>
public sealed class SuppressingFireExecutor : SkillExecutorBase
{
    public override async Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, int weaponRange)
    {
        if (!BowShotHelper.RequireBow(pl, client, svc, skill.Name)) return false;
        CommonPreHit(svc, pl, skill, client);
        ApplyBuff(pl, skill);
        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, monster.X, monster.Y,
            buffDurationMs: Balance.SuppressingFireDurationMs);
        await svc.ChatToC(client, "Бой",
            $"«{skill.Name}»! Подавляющий огонь на {Balance.SuppressingFireDurationMs / 1000} сек.");
        await svc.Hub.SendStatusAsync(client, pl);
        return true;
    }

    public override async Task<bool> ExecutePvP(Player pl, Player target, Skill skill,
        ClientConnection atkClient, CombatService svc, int weaponRange, int dist)
    {
        if (!BowShotHelper.RequireBow(pl, atkClient, svc, skill.Name)) return false;
        CommonPreHit(svc, pl, skill, atkClient);
        ApplyBuff(pl, skill);
        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, target.X, target.Y,
            buffDurationMs: Balance.SuppressingFireDurationMs);
        await svc.ChatToC(atkClient, "Бой",
            $"«{skill.Name}»! Подавляющий огонь на {Balance.SuppressingFireDurationMs / 1000} сек.");
        await svc.Hub.SendStatusAsync(atkClient, pl);
        return true;
    }

    private static void ApplyBuff(Player pl, Skill skill)
    {
        int dur = (int)(Balance.SuppressingFireDurationMs * (1.0 + (pl.GetSkillRank(skill.Id) - 1) * 0.1));
        var fire = ActiveDebuff.Create(DebuffType.SuppressingFire, Balance.SuppressingFireDmgMult, dur, "skill",
            "Подавляющий огонь", "Автоатаки бьют конусом на 60% от базы.");
        Program.Services.Debuffs.ApplyDebuff(pl, fire);

            var slow = ActiveDebuff.Create(DebuffType.AttackSpeedBonus, Balance.SuppressingFireSpeedPenalty, dur, "skill_sf",
                "Подавление (скорость)", "+12% к скорости атаки.");
        Program.Services.Debuffs.ApplyDebuff(pl, slow);
    }
}
