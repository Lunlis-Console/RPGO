namespace LostAndDivine.Shared.Models;

/// <summary>
/// Вынесенная боевая логика из God-Class Player.cs (384 строки).
/// Хранит ссылку на Player и делегирует расчёты, используя BalanceStatic/CombatMath/Equipment.
/// Player остаётся фасадом для сохранения публичного API и сериализации.
/// </summary>
public sealed class PlayerCombatStats
{
    private readonly Player _p;
    public PlayerCombatStats(Player player) => _p = player;

    public double GetAttackSpeedPoints() => 0;
    public double GetAttackSpeedGearMultiplier() => 1.0 + _p.Equipment.GetBonusAttackSpeed() / 100.0;

    public int GetEffStrength() => _p.Strength + _p.Equipment.GetBonusStrength();
    public int GetEffEndurance() => _p.Endurance + _p.Equipment.GetBonusEndurance();
    public int GetEffAgility() => _p.Agility + _p.Equipment.GetBonusAgility();
    public int GetEffCunning() => _p.Cunning + _p.Equipment.GetBonusCunning();
    public int GetEffIntellect() => _p.Intellect + _p.Equipment.GetBonusIntellect();
    public int GetEffWisdom() => _p.Wisdom + _p.Equipment.GetBonusWisdom();

    public int GetPhysAttack() => GetBaseDamage() + (GetEffStrength() - 1) * BalanceStatic.AttackPerStrength + (GetEffAgility() - 1) * BalanceStatic.AttackPerAgility + _p.Equipment.GetBonusPhysAttack();
    public int GetMagAttack() => GetBaseDamage() + (GetEffIntellect() - 1) * BalanceStatic.AttackPerIntellect + _p.Equipment.GetBonusMagAttack();

    public int GetDefense() => GetBaseDefense() + _p.Equipment.GetDefense() + _p.Equipment.GetBonusDefense();
    public int GetResistance() => GetBaseDefense() + _p.Equipment.GetMagicDefense() + _p.Equipment.GetBonusResistance();

    public double GetCritChance() => Math.Min(BalanceStatic.MaxCritChance, _p.BaseCritChance + CombatMath.ApplyCritDiminishingReturns(GetEffCunning() - 1) + _p.Equipment.GetBonusCritChance());
    public double GetCritDamage() => Math.Min(BalanceStatic.MaxCritDamage, _p.BaseCritDamage + CombatMath.ApplyCritDamageDiminishingReturns(Math.Max(0, GetEffStrength() - 1)) * BalanceStatic.CritDamagePerStrength + _p.Equipment.GetBonusCritDamage() / 100.0);
    public double GetEvadeChance() => Math.Min(BalanceStatic.MaxEvadeChance, _p.BaseEvadeChance + CombatMath.ApplyEvadeDiminishingReturns(GetEffAgility() - 1) + _p.Equipment.GetBonusEvadeChance());
    public double GetBlockChance()
    {
        double shieldBase = _p.Equipment.GetEquippedShield() != null ? 2.0 : 0.0;
        return Math.Min(BalanceStatic.MaxBlockChance, _p.BaseBlockChance + shieldBase + Math.Min(BalanceStatic.MaxBlockGearBonus, _p.Equipment.GetBonusBlockChance()));
    }
    public double GetParryChance() => Math.Min(BalanceStatic.MaxParryChance, _p.BaseParryChance + Math.Min(BalanceStatic.MaxParryGearBonus, _p.Equipment.GetBonusParryChance()) + GetReflexesParryBonus());

    public double GetReflexesParryBonus()
    {
        if (!_p.LearnedSkills.Contains(SkillIds.Reflexes)) return 0;
        return _p.Equipment.IsDualWielding() ? 10.0 * GetPassiveRankMult(SkillIds.Reflexes) : 0.0;
    }

    public double GetOffHandDamageFraction()
    {
        if (!_p.LearnedSkills.Contains(SkillIds.Ambidextrous)) return Equipment.OffHandDamageFraction;
        double bonus = GetSkillRank(SkillIds.Ambidextrous) switch { 3 => 0.50, 2 => 0.40, _ => 0.25 };
        return Math.Min(1.0, Equipment.OffHandDamageFraction + bonus);
    }

    public int GetOffHandTotalAttack(int dist)
    {
        var offHand = _p.Equipment.GetOffHandWeapon();
        bool useMag = offHand != null && Equipment.IsCasterWeapon(offHand);
        return (int)(((useMag ? GetMagAttack() : GetPhysAttack()) + _p.Equipment.GetOffHandMaxDamage()) * GetBerserkMultiplier());
    }

    public int GetBlockValue() => (int)(_p.Equipment.GetShieldBonusDefense() * BalanceStatic.ShieldBlockValueMultiplier);

    private bool IsUsingStaff() => _p.Equipment.GetWeaponCategory() == WeaponCategory.Staff;
    private bool UsesMagicAttack(int dist) => _p.Equipment.GetWeaponCategory() is WeaponCategory.Staff or WeaponCategory.Grimoire or WeaponCategory.Sphere;
    public bool IsMagicalDamage() => UsesMagicAttack(1);
    public bool IsOffHandMagical() => _p.Equipment.GetOffHandWeapon() is { } oh && Equipment.IsCasterWeapon(oh);

    public int GetBaseDamage() => 1 + (_p.Level - 1);
    public int GetBaseDefense() => 1 + (_p.Level - 1);
    public int GetTotalAttack() => GetTotalAttack(1);
    public int GetTotalDefense() => GetDefense();
    public int GetTotalResistance() => GetResistance();
    public int RollAttackDamage() => RollAttackDamage(1);
    public int RollOffHandDamage()
    {
        var offHand = _p.Equipment.GetOffHandWeapon();
        if (offHand != null && Equipment.IsCasterWeapon(offHand))
            return GetMagAttack() + _p.Equipment.RollOffHandDamage();
        return GetPhysAttack() + _p.Equipment.RollOffHandDamage();
    }
    public int GetMaxAttackDamage() => GetMaxAttackDamage(1);
    public int GetTotalAttack(int dist) => (int)(((UsesMagicAttack(dist) ? GetMagAttack() : GetPhysAttack()) + _p.Equipment.GetWeaponMaxDamage()) * GetBerserkMultiplier());

    public double GetBerserkMultiplier()
    {
        if (!_p.LearnedSkills.Contains(SkillIds.Berserk)) return 1.0;
        int maxHp = _p.MaxHealth + _p.Equipment.GetBonusMaxHealth();
        if (maxHp <= 0) return 1.0;
        double percentMissing = (maxHp - _p.Health) / (double)maxHp * 100.0;
        return 1.0 + BalanceStatic.BerserkDamagePer5Percent * GetPassiveRankMult(SkillIds.Berserk) * (percentMissing / 5.0);
    }

    // Ранги (делегируем в Progression, но для удобства дублируем)
    public int GetSkillRank(string skillId) => _p.SkillRanks.TryGetValue(skillId, out int r) ? r : 1;
    public double GetSkillRankDmgMult(string skillId) => 1.0 + (GetSkillRank(skillId) - 1) * 0.12;
    public double GetSkillRankCdMult(string skillId) => Math.Max(0.5, (1.0 - (GetSkillRank(skillId) - 1) * 0.08) * (1.0 - GetCooldownReduction() / 100.0));
    public double GetTenacity() => Math.Min(BalanceStatic.MaxTenacity, CombatMath.ApplyTenacityDiminishingReturns(Math.Max(0, GetEffEndurance() - 1)) + _p.Equipment.GetBonusTenacity());
    public double GetArmorPenetration() => Math.Min(BalanceStatic.MaxArmorPenetration, _p.Equipment.GetBonusArmorPenetration());
    public double GetCooldownReduction() => Math.Min(BalanceStatic.MaxCooldownReduction, CombatMath.ApplyCdrDiminishingReturns(Math.Max(0, GetEffWisdom() - 1)) + _p.Equipment.GetBonusCooldownReduction());
    public double GetHealthRegenPercent() => Math.Min(BalanceStatic.MaxHealthRegen, _p.Equipment.GetBonusHpRegen());
    public double GetManaRegenPercent() => Math.Min(BalanceStatic.MaxManaRegen, _p.Equipment.GetBonusMpRegen());
    public double GetCastSpeedReduction() => Math.Min(BalanceStatic.MaxCastSpeedReduction, CombatMath.ApplyCastSpeedDiminishingReturns(Math.Max(0, GetEffIntellect() - 1)));
    public double GetCastTimeMultiplier() => Math.Max(0.0, 1.0 - GetCastSpeedReduction() / 100.0);
    public double GetPassiveRankMult(string skillId) => 1.0 + (GetSkillRank(skillId) - 1) * 0.33;
    public bool IsWieldingBow() => _p.Equipment.IsBowEquipped();
    public int GetEffectiveAttackRange() => _p.Equipment.GetWeaponAttackRange() + GetBowRangeBonus();
    public double GetExtraArrowChance()
    {
        if (!_p.LearnedSkills.Contains(SkillIds.ExtraArrow) || !IsWieldingBow()) return 0;
        return BalanceStatic.ExtraArrowChance * GetPassiveRankMult(SkillIds.ExtraArrow);
    }
    public double GetAccuracy() => Math.Min(BalanceStatic.AccuracyMax, BalanceStatic.AccuracyBase + CombatMath.ApplyAccuracyDiminishingReturns(Math.Max(0, GetEffCunning() - 1)) + _p.Equipment.GetBonusAccuracy() + GetBowAccuracyBonus());
    public double GetBowAccuracyBonus()
    {
        if (!_p.LearnedSkills.Contains(SkillIds.BowAccuracy) || !IsWieldingBow()) return 0;
        return BalanceStatic.BowAccuracyBonus * GetPassiveRankMult(SkillIds.BowAccuracy);
    }
    public double GetMeleeEvadeBonus()
    {
        if (!_p.LearnedSkills.Contains(SkillIds.MeleeEvade)) return 0;
        return BalanceStatic.MeleeEvadeBonus * GetPassiveRankMult(SkillIds.MeleeEvade);
    }
    public int GetBowRangeBonus() => _p.LearnedSkills.Contains(SkillIds.LongRangeSight) && IsWieldingBow() ? BalanceStatic.BowRangeBonus : 0;
    public double GetCloseRangeArmorPen(int dist)
    {
        if (!_p.LearnedSkills.Contains(SkillIds.LongRangeSight) || !IsWieldingBow()) return 0;
        if (dist > BalanceStatic.CloseRangeArmorPenDist) return 0;
        double t = 1.0 - (dist - 1) / (double)BalanceStatic.CloseRangeArmorPenDist;
        if (dist <= 1) t = 1.0;
        return BalanceStatic.CloseRangeArmorPenMax * GetPassiveRankMult(SkillIds.LongRangeSight) * Math.Clamp(t, 0, 1);
    }
    public double GetHunterInstinctCritBonus(ICombatant target)
    {
        if (!_p.LearnedSkills.Contains(SkillIds.HuntingInstinct) || !IsWieldingBow()) return 0;
        bool marked = target switch
        {
            Player p => p.GetDebuffsSnapshot().Any(d => d.Type is DebuffType.Root or DebuffType.Slow or DebuffType.AccuracyReduction),
            Monster m => m.GetDebuffsSnapshot().Any(d => d.Type is DebuffType.Root or DebuffType.Slow or DebuffType.AccuracyReduction),
            _ => false
        };
        return marked ? BalanceStatic.HunterInstinctCritBonus * GetPassiveRankMult(SkillIds.HuntingInstinct) : 0;
    }
    public int RollAttackDamage(int dist) => (UsesMagicAttack(dist) ? GetMagAttack() : GetPhysAttack()) + _p.Equipment.RollWeaponDamage();
    public int GetMaxAttackDamage(int dist) => (UsesMagicAttack(dist) ? GetMagAttack() : GetPhysAttack()) + _p.Equipment.GetWeaponMaxDamage();
}
