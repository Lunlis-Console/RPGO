using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Skills.Executors;

/// <summary>SK0004 «Разрез» — 2 удара (150%), второй левой рукой при двойном оружии.</summary>
public sealed class SlashExecutor : SkillExecutorBase
{
    public override async Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill,
        ClientConnection client, CombatService svc, int weaponRange)
    {
        // Без двуоружия — один усиленный удар (без комбо левой рукой).
        if (!pl.Equipment.IsDualWielding())
        {
            await svc.ChatToC(client, "Бой", "«Разрез» без второго оружия — один удар.");
            return await ExecuteFirstHitPvE(pl, monster, skill, client, svc, 1.5, comboHitsRemaining: 0);
        }
        return await ExecuteFirstHitPvE(pl, monster, skill, client, svc, 1.5, comboHitsRemaining: 1);
    }

    public override Task<bool> ExecutePvP(Player pl, Player target, Skill skill,
        ClientConnection atkClient, CombatService svc, int weaponRange, int dist)
        => ExecuteFirstHitPvP(pl, target, skill, atkClient, svc, dist, 1.5,
            comboHitsRemaining: pl.Equipment.IsDualWielding() ? 1 : 0);

    public override Task<bool> ExecuteComboPvP(Player pl, Player target,
        ClientConnection atkClient, CombatService svc)
        => ExecuteComboHitPvP(pl, target, atkClient, svc, totalHits: 2, _ => 1.5);

    public override Task<bool> ExecuteComboPvE(Player pl, Monster monster,
        ClientConnection client, CombatService svc)
        => ExecuteComboHitPvE(pl, monster, client, svc, totalHits: 2, _ => 1.5, useOffHand: true);
}
