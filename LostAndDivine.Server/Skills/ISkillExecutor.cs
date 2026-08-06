using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Skills;

/// <summary>Простой интерфейс экзекутора: методы с сигнатурами как в CombatService.</summary>
public interface ISkillExecutor
{
    /// <summary>Первый удар навыка по монстру. true = метод сделал return (комбо-навыки).</summary>
    Task<bool> ExecutePvE(Player pl, Monster monster, Skill skill, ClientConnection client, CombatService svc, int weaponRange);

    /// <summary>Комбо-удар по монстру. null = не комбо-навык.</summary>
    Task<bool> ExecuteComboPvE(Player pl, Monster monster, ClientConnection client, CombatService svc);

    /// <summary>Наказание за смену таргета монстром (SK0009). true = наказание применено.</summary>
    Task<bool> CheckPunishPvE(Player pl, Monster monster, ClientConnection client, CombatService svc);

    /// <summary>Первый удар по игроку (PvP).</summary>
    Task<bool> ExecutePvP(Player pl, Player target, Skill skill, ClientConnection atkClient, CombatService svc, int weaponRange, int dist);

    /// <summary>Комбо-удар по игроку (PvP).</summary>
    Task<bool> ExecuteComboPvP(Player pl, Player target, ClientConnection atkClient, CombatService svc);

    /// <summary>Наказание за смену таргета PvP-игроком.</summary>
    Task<bool> CheckPunishPvP(Player pl, Player target, ClientConnection atkClient, CombatService svc);

    /// <summary>Интервал между комбо-ударами (мс). По умолчанию SlashHitIntervalMs.</summary>
    int ComboIntervalMs { get; }
}
