using System;
using RPGGame.Server.Network;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Skills.Executors;

/// <summary>SK0014 «Отступление» — отскок назад + ловушка.</summary>
public sealed class RetreatExecutor : SkillExecutorBase
{
    public override async Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, int weaponRange)
    {
        if (!BowShotHelper.RequireBow(pl, client, svc, skill.Name)) return false;
        CommonPreHit(svc, pl, skill, client);
        await ApplyRetreat(pl, monster.X, monster.Y, skill, client, svc);
        return true;
    }

    public override async Task<bool> ExecutePvP(Player pl, Player target, Skill skill,
        ClientConnection atkClient, CombatService svc, int weaponRange, int dist)
    {
        if (!BowShotHelper.RequireBow(pl, atkClient, svc, skill.Name)) return false;
        CommonPreHit(svc, pl, skill, atkClient);
        await ApplyRetreat(pl, target.X, target.Y, skill, atkClient, svc);
        return true;
    }

    private static async Task ApplyRetreat(Player pl, int targetX, int targetY, Skill skill,
        ClientConnection client, CombatService svc)
    {
        int oldX = pl.X, oldY = pl.Y;
        int dx = Math.Sign(pl.X - targetX);
        int dy = Math.Sign(pl.Y - targetY);
        if (dx == 0 && dy == 0)
        {
            // Если цель на той же клетке — назад по facing
            (dx, dy) = pl.Facing switch
            {
                "left" => (-1, 0),
                "right" => (1, 0),
                "up" => (0, -1),
                _ => (0, 1)
            };
            // Отскок от facing: reverse
            dx = -dx; dy = -dy;
        }

        int moved = 0;
        for (int i = 0; i < Balance.RetreatTiles; i++)
        {
            int nx = pl.X + dx, ny = pl.Y + dy;
            if (!svc.World.Map.InBounds(nx, ny) || svc.World.Map.IsObstacle(nx, ny)) break;
            if (svc.World.FindMonsterAt(nx, ny) != null) break;
            if (svc.World.GetPlayersSnapshot().Any(p => p.Id != pl.Id && p.X == nx && p.Y == ny && !p.IsDead)) break;
            pl.X = nx; pl.Y = ny;
            moved++;
        }

        pl.Movement.Stop();
        await svc.Hub.BroadcastMapAsync();

        var kind = (HazardKind)Random.Shared.Next(0, 3);
        int duration = (int)(Balance.TrapDurationMs * (1.0 + (pl.GetSkillRank(skill.Id) - 1) * 0.15));
        int baseDmg = Math.Max(1, pl.GetMaxAttackDamage());
        var hazard = new GroundHazard
        {
            X = oldX,
            Y = oldY,
            ZoneId = pl.CurrentZoneId,
            Kind = kind,
            ExpiresAt = DateTime.UtcNow.AddMilliseconds(duration),
            OwnerId = pl.Id,
            DotDamagePerTick = kind == HazardKind.Acid
                ? Math.Max(1, (int)(baseDmg * Balance.AcidDotFractionPerSec))
                : 0
        };
        svc.World.AddHazard(hazard);

        string trapName = kind switch
        {
            HazardKind.Smoke => "дымовая шашка",
            HazardKind.Snare => "капкан",
            _ => "кислотная лужа"
        };
        await svc.ChatToC(client, "Бой",
            moved > 0
                ? $"«{skill.Name}»: отскок на {moved} кл., оставлена {trapName}!"
                : $"«{skill.Name}»: оставлена {trapName}!");
        await svc.SendPlayerAttack(pl.Name, "main", skill.Id, targetX, targetY);
        await svc.Hub.SendStatusAsync(client, pl);
    }
}
