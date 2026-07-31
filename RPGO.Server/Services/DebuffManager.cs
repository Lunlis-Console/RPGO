using RPGGame.Shared.Models;

namespace RPGGame.Server;

public class DebuffManager
{
    private CombatService _combat = null!;

    public void SetCombatService(CombatService combat) => _combat = combat;

    public bool ApplyDebuff(Player target, ActiveDebuff debuff)
    {
        lock (target.DebuffsLock)
        {
            var existing = target.ActiveDebuffs.FirstOrDefault(d => d.Type == debuff.Type && d.SourceSubtype == debuff.SourceSubtype);
            if (existing != null)
            {
                existing.RemainingMs = debuff.DurationMs;
                existing.Value = debuff.Value;
                return false;
            }
            target.ActiveDebuffs.Add(debuff);
        }
        Log.Debug($"ApplyDebuff player={target.Name} type={debuff.Type}");

        _ = _combat?.SendTargetPlayerDebuffUpdateAsync(target);
        return true;
    }

    public bool ApplyDebuff(Monster target, ActiveDebuff debuff)
    {
        lock (target.DebuffsLock)
        {
            var existing = target.ActiveDebuffs.FirstOrDefault(d => d.Type == debuff.Type && d.SourceSubtype == debuff.SourceSubtype);
            if (existing != null)
            {
                existing.RemainingMs = debuff.DurationMs;
                existing.Value = debuff.Value;
                return false;
            }
            target.ActiveDebuffs.Add(debuff);
        }
        return true;
    }

    public async void TickDebuffs(Player target)
    {
        lock (target.DebuffsLock)
        {
            foreach (var d in target.ActiveDebuffs)
                d.RemainingMs -= Balance.DebuffTickMs;
            target.ActiveDebuffs.RemoveAll(d => d.RemainingMs <= 0);
        }
        if (_combat != null)
            await _combat.SendTargetPlayerDebuffUpdateAsync(target);
    }

    public void TickDebuffs(Monster target)
    {
        lock (target.DebuffsLock)
        {
            foreach (var d in target.ActiveDebuffs)
                d.RemainingMs -= Balance.DebuffTickMs;
            target.ActiveDebuffs.RemoveAll(d => d.RemainingMs <= 0);
        }
    }

    public double GetDebuffValue(ICombatant target, DebuffType type)
    {
        return target switch
        {
            Player p => GetDebuffValueSafe(p.DebuffsLock, p.ActiveDebuffs, type),
            Monster m => GetDebuffValueSafe(m.DebuffsLock, m.ActiveDebuffs, type),
            _ => 0
        };
    }

    private static double GetDebuffValueSafe(object syncRoot, List<ActiveDebuff> debuffs, DebuffType type)
    {
        lock (syncRoot)
        {
            return debuffs.Where(d => d.Type == type).Sum(d => d.Value);
        }
    }

    public bool HasDebuff(ICombatant target, DebuffType type)
    {
        return target switch
        {
            Player p => HasDebuffSafe(p.DebuffsLock, p.ActiveDebuffs, type),
            Monster m => HasDebuffSafe(m.DebuffsLock, m.ActiveDebuffs, type),
            _ => false
        };
    }

    private static bool HasDebuffSafe(object syncRoot, List<ActiveDebuff> debuffs, DebuffType type)
    {
        lock (syncRoot)
        {
            return debuffs.Any(d => d.Type == type);
        }
    }

    public void ClearDebuffs(Player target)
    {
        lock (target.DebuffsLock)
            target.ActiveDebuffs.Clear();
    }

    public void ClearDebuffs(Monster target)
    {
        lock (target.DebuffsLock)
            target.ActiveDebuffs.Clear();
    }

    public void RefreshDualWieldBuff(Player target)
    {
        lock (target.DebuffsLock)
        {
            var existing = target.ActiveDebuffs.FirstOrDefault(d => d.Type == DebuffType.DualWieldBonus);
            if (target.Equipment.IsDualWielding())
            {
                if (existing != null)
                {
                    existing.RemainingMs = Balance.DualWieldBuffRefreshMs;
                    existing.DurationMs = Balance.DualWieldBuffRefreshMs;
                }
                else
                {
                    target.ActiveDebuffs.Add(ActiveDebuff.Create(DebuffType.DualWieldBonus, Balance.DualWieldBonusValue,
                        Balance.DualWieldBuffRefreshMs, "passive", "Второе оружие",
                        $"Двойная атака, +{(int)(Balance.DualWieldBonusValue * 100)}% к скорости атаки"));
                }
            }
            else if (existing != null)
            {
                target.ActiveDebuffs.Remove(existing);
            }
        }
    }

    /// <summary>
    /// Обновляет/удаляет бафф двойного оружия. Возвращает true, если состояние изменилось.
    /// </summary>
    public bool CheckDualWieldBuff(Player target)
    {
        bool hadBuff;
        lock (target.DebuffsLock)
            hadBuff = target.ActiveDebuffs.Any(d => d.Type == DebuffType.DualWieldBonus);
        RefreshDualWieldBuff(target);
        bool hasBuff;
        lock (target.DebuffsLock)
            hasBuff = target.ActiveDebuffs.Any(d => d.Type == DebuffType.DualWieldBonus);
        return hadBuff != hasBuff;
    }

    public (ActiveDebuff Debuff, bool IsNew) OnWeaponProc(ICombatant attacker, ICombatant defender, string weaponSubtype)
    {
        var rng = Random.Shared;
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
