using System;
using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Skills.Executors;

/// <summary>Общий расчёт урона навыка лука (PvE/PvP).</summary>
internal static class BowShotHelper
{
    public static bool RequireBow(Player pl, ClientConnection client, CombatService svc, string skillName)
    {
        if (pl.IsWieldingBow()) return true;
        _ = svc.ChatToC(client, "Бой", $"«{skillName}» доступен только с луком.");
        return false;
    }

    public static (int dmg, bool crit, bool evaded, bool parried, bool blocked) CalculatePvE(
        Player pl, Monster monster, CombatService svc,
        double dmgMult, bool vulnerable, string skillId)
    {
        int dist = Math.Abs(pl.X - monster.X) + Math.Abs(pl.Y - monster.Y);
        dmgMult *= pl.GetSkillRankDmgMult(skillId);

        var rng = Random.Shared;
        double armorPen = pl.GetCloseRangeArmorPen(dist);
        if (vulnerable) armorPen = Math.Min(1.0, armorPen + BalanceStatic.VulnerableArmorIgnore);

        double effDef = svc.Monsters.GetEffectiveDefense(monster, armorPen, magic: false);
        double effAtk = svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage(dist));

        double evadeChance = Math.Max(0, monster.GetEvadeChance() - pl.GetBowAccuracyBonus());
        bool evaded = rng.Next(Balance.ChanceRollMax) < evadeChance;
        bool parried = false;
        bool blocked = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
        int hitDmg = 0; bool hitCrit = false;

        if (!evaded)
        {
            double critChance = Math.Min(BalanceStatic.MaxCritChance, pl.GetCritChance() + pl.GetHunterInstinctCritBonus(monster));
            if (vulnerable) { hitCrit = true; }
            else hitCrit = rng.Next(Balance.ChanceRollMax) < critChance;

            int baseDmg = Math.Max(Balance.MinDamage, (int)(effAtk * (1.0 - effDef)));
            hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * dmgMult);
            if (hitCrit) hitDmg = (int)(hitDmg * pl.GetCritDamage());
            hitDmg = svc.Monsters.ApplyDmgReduction(pl, hitDmg);
            if (blocked) hitDmg = 0;
        }

        return (hitDmg, hitCrit, evaded, parried, blocked);
    }

    public static async Task<(int dmg, bool crit, bool evaded)> DealPvP(
        Player pl, Player target, ClientConnection atkClient, CombatService svc,
        double dmgMult, bool vulnerable, string skillName, string skillId, int dist)
    {
        dmgMult *= pl.GetSkillRankDmgMult(skillId);
        double armorPen = pl.GetCloseRangeArmorPen(dist);
        if (vulnerable) armorPen = Math.Min(1.0, armorPen + BalanceStatic.VulnerableArmorIgnore);

        double evadeChance = Math.Max(0, target.GetEvadeChance() - pl.GetBowAccuracyBonus());
        bool evaded = Random.Shared.NextDouble() * 100 < evadeChance;
        int hitDmg = 0; bool hitCrit = false;

        if (!evaded)
        {
            double reduction = CombatMath.CalcDefenseReduction(target.GetTotalDefense()) * (1.0 - armorPen);
            int raw = Math.Max(Balance.MinDamage, (int)(pl.GetTotalAttack(dist) * (1.0 - reduction)));
            hitDmg = (int)Math.Max(Balance.MinDamage, raw * dmgMult);
            double critChance = Math.Min(BalanceStatic.MaxCritChance, pl.GetCritChance() + pl.GetHunterInstinctCritBonus(target));
            hitCrit = vulnerable || Random.Shared.NextDouble() * 100 < critChance;
            if (hitCrit) hitDmg = (int)(hitDmg * pl.GetCritDamage());

            bool blocked = Random.Shared.NextDouble() * 100 < target.GetBlockChance();
            if (blocked) hitDmg = 0;

            target.Health -= hitDmg;
            target.LastDamagedTime = DateTime.UtcNow;

            string critT = hitCrit ? (vulnerable ? " (УЯЗВИМОЕ!)" : " (КРИТ!)") : "";
            await svc.ChatToC(atkClient, "Бой", $"«{skillName}»: {hitDmg} урона{critT} {target.Name}.");
            var targetClient = svc.World.FindClientByPlayer(target);
            if (targetClient != null)
            {
                await svc.SendToC(targetClient, new GameMessage
                {
                    Type = "damage",
                    Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit, IsSkill = true }
                });
                await svc.ChatToC(targetClient, "Бой", $"{pl.Name}: «{skillName}» — {hitDmg} урона{critT}.");
                await svc.Hub.SendStatusAsync(targetClient, target);
            }
        }
        else
            await svc.ChatToC(atkClient, "Бой", $"{target.Name} уклонился от «{skillName}».");

        return (hitDmg, hitCrit, evaded);
    }
}
