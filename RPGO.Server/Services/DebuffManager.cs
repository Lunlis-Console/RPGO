using RPGGame.Shared.Models;

namespace RPGGame.Server;

public static class DebuffManager
{
    public static bool ApplyDebuff(Player target, ActiveDebuff debuff)
    {
        var existing = target.ActiveDebuffs.FirstOrDefault(d => d.Type == debuff.Type && d.SourceSubtype == debuff.SourceSubtype);
        if (existing != null)
        {
            existing.RemainingMs = debuff.DurationMs;
            existing.Value = debuff.Value;
            return false;
        }
        target.ActiveDebuffs.Add(debuff);
        return true;
    }

    public static bool ApplyDebuff(Monster target, ActiveDebuff debuff)
    {
        var existing = target.ActiveDebuffs.FirstOrDefault(d => d.Type == debuff.Type && d.SourceSubtype == debuff.SourceSubtype);
        if (existing != null)
        {
            existing.RemainingMs = debuff.DurationMs;
            existing.Value = debuff.Value;
            return false;
        }
        target.ActiveDebuffs.Add(debuff);
        return true;
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

    public static (ActiveDebuff Debuff, bool IsNew) OnWeaponProc(ICombatant attacker, ICombatant defender, string weaponSubtype)
    {
        var rng = new Random();
        if (rng.Next(Balance.ChanceRollMax) >= Balance.WeaponProcChance) return default;

        ActiveDebuff debuff;
        ICombatant target;
        switch (weaponSubtype)
        {
            case "dagger":
                debuff = ActiveDebuff.Create(DebuffType.ArmorPenetration, Balance.DaggerArmorPenValue,
                    Balance.DaggerArmorPenDurationMs, "dagger", "Пронзание");
                target = defender;
                break;

            case "sword":
                debuff = ActiveDebuff.Create(DebuffType.CleaveReady, 0,
                    500, "sword", "Рассекающий удар");
                target = attacker;
                break;

            case "axe":
                debuff = ActiveDebuff.Create(DebuffType.DamageBonus, Balance.AxeDamageBonusValue,
                    Balance.AxeDamageBonusDurationMs, "axe", "Свирепость");
                target = attacker;
                break;

            case "mace":
                debuff = ActiveDebuff.Create(DebuffType.DamageReduction, Balance.MaceDamageReductionValue,
                    Balance.MaceDisarmDurationMs, "mace", "Обезоруживание");
                target = defender;
                break;

            case "hammer":
                debuff = ActiveDebuff.Create(DebuffType.AccuracyReduction, Balance.HammerAccuracyReductionValue,
                    Balance.HammerStunDurationMs, "hammer", "Контузия");
                target = defender;
                break;

            default:
                return default;
        }

        bool isNew = target switch
        {
            Player p => ApplyDebuff(p, debuff),
            Monster m => ApplyDebuff(m, debuff),
            _ => false
        };
        return (debuff, isNew);
    }
}
