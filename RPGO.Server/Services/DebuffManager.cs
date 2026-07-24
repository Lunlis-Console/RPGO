using RPGGame.Shared.Models;

namespace RPGGame.Server;

public class DebuffManager
{
    public bool ApplyDebuff(Player target, ActiveDebuff debuff)
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

    public bool ApplyDebuff(Monster target, ActiveDebuff debuff)
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

    public void TickDebuffs(Player target)
    {
        foreach (var d in target.ActiveDebuffs)
            d.RemainingMs -= Balance.DebuffTickMs;
        target.ActiveDebuffs.RemoveAll(d => d.RemainingMs <= 0);
    }

    public void TickDebuffs(Monster target)
    {
        foreach (var d in target.ActiveDebuffs)
            d.RemainingMs -= Balance.DebuffTickMs;
        target.ActiveDebuffs.RemoveAll(d => d.RemainingMs <= 0);
    }

    public double GetDebuffValue(ICombatant target, DebuffType type)
    {
        var list = target switch
        {
            Player p => p.ActiveDebuffs,
            Monster m => m.ActiveDebuffs,
            _ => new List<ActiveDebuff>()
        };
        return list.Where(d => d.Type == type).Sum(d => d.Value);
    }

    public bool HasDebuff(ICombatant target, DebuffType type)
    {
        return target switch
        {
            Player p => p.ActiveDebuffs.Any(d => d.Type == type),
            Monster m => m.ActiveDebuffs.Any(d => d.Type == type),
            _ => false
        };
    }

    public void ClearDebuffs(Player target) => target.ActiveDebuffs.Clear();
    public void ClearDebuffs(Monster target) => target.ActiveDebuffs.Clear();

    public (ActiveDebuff Debuff, bool IsNew) OnWeaponProc(ICombatant attacker, ICombatant defender, string weaponSubtype)
    {
        var rng = new Random();
        if (rng.Next(Balance.ChanceRollMax) >= Balance.WeaponProcChance) return default;

        ActiveDebuff debuff;
        ICombatant target;
        switch (weaponSubtype)
        {
            case "dagger" or "spear":
                debuff = ActiveDebuff.Create(DebuffType.ArmorPenetration, Balance.DaggerArmorPenValue,
                    Balance.DaggerArmorPenDurationMs, weaponSubtype, "Пронзание",
                    $"Снижает броню цели на {(int)(Balance.DaggerArmorPenValue * 100)}%");
                target = defender;
                break;

            case "sword" or "greatsword":
                debuff = ActiveDebuff.Create(DebuffType.CleaveReady, 0,
                    500, weaponSubtype, "Рассекающий удар",
                    "Следующая атака наносит урон по области");
                target = attacker;
                break;

            case "axe" or "greataxe" or "halberd":
                debuff = ActiveDebuff.Create(DebuffType.DamageBonus, Balance.AxeDamageBonusValue,
                    Balance.AxeDamageBonusDurationMs, weaponSubtype, "Свирепость",
                    $"Увеличивает ваш урон на {(int)(Balance.AxeDamageBonusValue * 100)}%");
                target = attacker;
                break;

            case "mace":
                debuff = ActiveDebuff.Create(DebuffType.DamageReduction, Balance.MaceDamageReductionValue,
                    Balance.MaceDisarmDurationMs, weaponSubtype, "Обезоруживание",
                    $"Снижает урон цели на {(int)(Balance.MaceDamageReductionValue * 100)}%");
                target = defender;
                break;

            case "hammer" or "greathammer":
                debuff = ActiveDebuff.Create(DebuffType.AccuracyReduction, Balance.HammerAccuracyReductionValue,
                    Balance.HammerStunDurationMs, weaponSubtype, "Контузия",
                    $"Снижает точность цели на {(int)(Balance.HammerAccuracyReductionValue * 100)}%");
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

    public (ActiveDebuff Debuff, bool IsNew) ForceWeaponProc(ICombatant attacker, ICombatant defender, string weaponSubtype)
    {
        ActiveDebuff debuff;
        ICombatant target;
        switch (weaponSubtype)
        {
            case "dagger" or "spear":
                debuff = ActiveDebuff.Create(DebuffType.ArmorPenetration, Balance.DaggerArmorPenValue,
                    Balance.DaggerArmorPenDurationMs, weaponSubtype, "Пронзание",
                    $"Снижает броню цели на {(int)(Balance.DaggerArmorPenValue * 100)}%");
                target = defender;
                break;

            case "sword" or "greatsword":
                debuff = ActiveDebuff.Create(DebuffType.CleaveReady, 0,
                    500, weaponSubtype, "Рассекающий удар",
                    "Следующая атака наносит урон по области");
                target = attacker;
                break;

            case "axe" or "greataxe" or "halberd":
                debuff = ActiveDebuff.Create(DebuffType.DamageBonus, Balance.AxeDamageBonusValue,
                    Balance.AxeDamageBonusDurationMs, weaponSubtype, "Свирепость",
                    $"Увеличивает ваш урон на {(int)(Balance.AxeDamageBonusValue * 100)}%");
                target = attacker;
                break;

            case "mace":
                debuff = ActiveDebuff.Create(DebuffType.DamageReduction, Balance.MaceDamageReductionValue,
                    Balance.MaceDisarmDurationMs, weaponSubtype, "Обезоруживание",
                    $"Снижает урон цели на {(int)(Balance.MaceDamageReductionValue * 100)}%");
                target = defender;
                break;

            case "hammer" or "greathammer":
                debuff = ActiveDebuff.Create(DebuffType.AccuracyReduction, Balance.HammerAccuracyReductionValue,
                    Balance.HammerStunDurationMs, weaponSubtype, "Контузия",
                    $"Снижает точность цели на {(int)(Balance.HammerAccuracyReductionValue * 100)}%");
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
