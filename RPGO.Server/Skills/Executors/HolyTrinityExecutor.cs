using System;
using RPGGame.Server.Network;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Skills.Executors;

/// <summary>SK0007 «Святая троица» — 3 удара (200%), случайный дебафф на каждом.</summary>
public sealed class HolyTrinityExecutor : SkillExecutorBase
{
    public override Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, int weaponRange)
        => ExecuteFirstHitPvE(pl, monster, skill, client, svc, 2.0, comboHitsRemaining: 2, ApplyDebuff);

    public override Task<bool> ExecuteComboPvE(Player pl, Monster monster,
        ClientConnection client, CombatService svc)
        => ExecuteComboHitPvE(pl, monster, client, svc, totalHits: 3, _ => 2.0, useOffHand: false, ApplyDebuff);

    public override Task<bool> ExecutePvP(Player pl, Player target, Skill skill,
        ClientConnection atkClient, CombatService svc, int weaponRange, int dist)
        => ExecuteFirstHitPvP(pl, target, skill, atkClient, svc, dist, 2.0, comboHitsRemaining: 2);

    public override Task<bool> ExecuteComboPvP(Player pl, Player target,
        ClientConnection atkClient, CombatService svc)
        => ExecuteComboHitPvP(pl, target, atkClient, svc, totalHits: 3, _ => 2.0);

    private static void ApplyDebuff(Player pl, Monster monster, int dmg, bool crit, Random rng)
    {
        if (rng.Next(Balance.ChanceRollMax) < Balance.HolyTrinityDebuffChance)
        {
            ActiveDebuff debuff = rng.Next(3) switch
            {
                0 => ActiveDebuff.Create(DebuffType.Root, 0, Balance.RootDurationMs, "skill", "Обездвижен",
                    $"Обездвиживает на {Balance.RootDurationMs / 1000} сек."),
                1 => ActiveDebuff.Create(DebuffType.DamageReduction, Balance.MaceDamageReductionValue,
                    Balance.MaceDisarmDurationMs, "skill", "Обезоружен",
                    $"Снижает урон цели на {(int)(Balance.MaceDamageReductionValue * 100)}%"),
                _ => ActiveDebuff.Create(DebuffType.AccuracyReduction, Balance.HammerAccuracyReductionValue,
                    Balance.HammerStunDurationMs, "skill", "Контузия",
                    $"Снижает точность цели на {(int)(Balance.HammerAccuracyReductionValue * 100)}%")
            };
            Program.Services.Debuffs.ApplyDebuff(monster, debuff);
        }
    }
}
