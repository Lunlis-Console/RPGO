using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Skills;

/// <summary>Базовый экзекутор: все методы no-op. Наследники переопределяют нужное.</summary>
public abstract class BaseSkillExecutor : ISkillExecutor
{
    public virtual Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill, ClientConnection client, CombatService svc, int weaponRange)
        => Task.FromResult(false);

    public virtual Task<bool> ExecuteComboPvE(Player pl, Monster monster, ClientConnection client, CombatService svc)
        => Task.FromResult(false);

    public virtual Task<bool> CheckPunishPvE(Player pl, Monster monster, ClientConnection client, CombatService svc)
        => Task.FromResult(false);

    public virtual Task<bool> ExecutePvP(Player pl, Player target, Skill skill, ClientConnection atkClient, CombatService svc, int weaponRange, int dist)
        => Task.FromResult(false);

    public virtual Task<bool> ExecuteComboPvP(Player pl, Player target, ClientConnection atkClient, CombatService svc)
        => Task.FromResult(false);

    public virtual Task<bool> CheckPunishPvP(Player pl, Player target, ClientConnection atkClient, CombatService svc)
        => Task.FromResult(false);

    public virtual int ComboIntervalMs => Balance.SlashHitIntervalMs;
}
