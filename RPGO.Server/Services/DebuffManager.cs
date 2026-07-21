using RPGGame.Shared.Models;

namespace RPGGame.Server;

public static class DebuffManager
{
    public static void ApplyDebuff(Player target, ActiveDebuff debuff)
    {
        var existing = target.ActiveDebuffs.FirstOrDefault(d => d.Type == debuff.Type && d.SourceSubtype == debuff.SourceSubtype);
        if (existing != null)
        {
            existing.RemainingMs = debuff.DurationMs;
            existing.Value = debuff.Value;
        }
        else
        {
            target.ActiveDebuffs.Add(debuff);
        }
    }

    public static void ApplyDebuff(Monster target, ActiveDebuff debuff)
    {
        var existing = target.ActiveDebuffs.FirstOrDefault(d => d.Type == debuff.Type && d.SourceSubtype == debuff.SourceSubtype);
        if (existing != null)
        {
            existing.RemainingMs = debuff.DurationMs;
            existing.Value = debuff.Value;
        }
        else
        {
            target.ActiveDebuffs.Add(debuff);
        }
    }

    public static void TickDebuffs(Player target)
    {
        foreach (var d in target.ActiveDebuffs)
            d.RemainingMs -= Balance.DebuffTickMs;
        target.ActiveDebuffs.RemoveAll(d => d.RemainingMs <= 0);
    }

    public static void TickDebuffs(Monster target)
    {
        foreach (var d in target.ActiveDebuffs)
            d.RemainingMs -= Balance.DebuffTickMs;
        target.ActiveDebuffs.RemoveAll(d => d.RemainingMs <= 0);
    }

    public static double GetDebuffValue(ICombatant target, DebuffType type)
    {
        var list = target switch
        {
            Player p => p.ActiveDebuffs,
            Monster m => m.ActiveDebuffs,
            _ => new List<ActiveDebuff>()
        };
        return list.Where(d => d.Type == type).Sum(d => d.Value);
    }

    public static bool HasDebuff(ICombatant target, DebuffType type)
    {
        return target switch
        {
            Player p => p.ActiveDebuffs.Any(d => d.Type == type),
            Monster m => m.ActiveDebuffs.Any(d => d.Type == type),
            _ => false
        };
    }

    public static void ClearDebuffs(Player target) => target.ActiveDebuffs.Clear();
    public static void ClearDebuffs(Monster target) => target.ActiveDebuffs.Clear();

    public static void OnWeaponProc(ICombatant attacker, ICombatant defender, string weaponSubtype)
    {
        var rng = new Random();
        if (rng.Next(Balance.ChanceRollMax) >= Balance.WeaponProcChance) return;

        switch (weaponSubtype)
        {
            case "dagger":
                ApplyDebuff(defender, ActiveDebuff.Create(
                    DebuffType.ArmorPenetration, Balance.DaggerArmorPenValue,
                    Balance.DaggerArmorPenDurationMs, "dagger", "Пронзание"));
                break;

            case "sword":
                ApplyDebuff(attacker, ActiveDebuff.Create(
                    DebuffType.CleaveReady, 0,
                    500, "sword", "Рассекающий удар"));
                break;

            case "axe":
                ApplyDebuff(attacker, ActiveDebuff.Create(
                    DebuffType.DamageBonus, Balance.AxeDamageBonusValue,
                    Balance.AxeDamageBonusDurationMs, "axe", "Свирепость"));
                break;

            case "mace":
                ApplyDebuff(defender, ActiveDebuff.Create(
                    DebuffType.DamageReduction, Balance.MaceDamageReductionValue,
                    Balance.MaceDisarmDurationMs, "mace", "Обезоруживание"));
                break;

            case "hammer":
                ApplyDebuff(defender, ActiveDebuff.Create(
                    DebuffType.AccuracyReduction, Balance.HammerAccuracyReductionValue,
                    Balance.HammerStunDurationMs, "hammer", "Контузия"));
                break;
        }
    }

    private static void ApplyDebuff(ICombatant target, ActiveDebuff debuff)
    {
        switch (target)
        {
            case Player p: ApplyDebuff(p, debuff); break;
            case Monster m: ApplyDebuff(m, debuff); break;
        }
    }
}
