using LostAndDivine.Shared.Models;
using LostAndDivine.Server;

namespace LostAndDivine.Tests;

public class PlayerStatsTests
{
    [Fact]
    public void GetBaseDamage_Level1_Returns1()
    {
        var p = new Player { Level = 1 };
        Assert.Equal(1, p.GetBaseDamage());
    }

    [Fact]
    public void GetBaseDamage_Level5_Returns5()
    {
        var p = new Player { Level = 5 };
        Assert.Equal(5, p.GetBaseDamage());
    }

    [Fact]
    public void GetBaseDefense_Level1_Returns1()
    {
        var p = new Player { Level = 1 };
        Assert.Equal(1, p.GetBaseDefense());
    }

    [Fact]
    public void GetTotalAttack_NoStats_Returns1()
    {
        var p = new Player { Level = 1, Strength = 1 };
        Assert.Equal(1, p.GetTotalAttack());
    }

    [Fact]
    public void GetTotalAttack_WithStrength_ReturnsCorrectly()
    {
        var p = new Player { Level = 5, Strength = 5 };
        // BaseDmg=5 + (5-1)*2=8 + equipBonus=0 = 13
        Assert.Equal(13, p.GetTotalAttack());
    }

    [Fact]
    public void GetTotalDefense_NoStats_Returns1()
    {
        var p = new Player { Level = 1, Endurance = 1 };
        Assert.Equal(1, p.GetTotalDefense());
    }

    [Fact]
    public void GetTotalDefense_LevelOnly_ReturnsLevel()
    {
        var p = new Player { Level = 3, Endurance = 5 };
        // Защита = база от уровня (1/уровень) + шмот; выносливость больше не даёт защиту.
        Assert.Equal(3, p.GetTotalDefense());
    }

    [Fact]
    public void GetCritChance_NoCunning_Returns1()
    {
        var p = new Player { Cunning = 1, BaseCritChance = 1.0 };
        Assert.Equal(1.0, p.GetCritChance());
    }

    [Fact]
    public void GetCritChance_WithCunning_ReturnsCorrectly()
    {
        var p = new Player { Cunning = 6, BaseCritChance = 1.0 };
        // 1.0 + (6-1)*0.5 = 3.5 (убывающая отдача: 1 очко = 0.5%)
        Assert.Equal(3.5, p.GetCritChance());
    }

    [Fact]
    public void GetCritChance_AfterDiminishingThreshold_SlowsDown()
    {
        var p = new Player { Cunning = 52, BaseCritChance = 1.0 };
        // 51 очко: первые 50 дают 0.5% (25%), 51-е — 0.25% → 25.25 + 1 = 26.25
        Assert.Equal(26.25, p.GetCritChance());
    }

    [Fact]
    public void GetCritChance_CappedAt75()
    {
        var p = new Player { Cunning = 1000, BaseCritChance = 1.0 };
        Assert.Equal(75.0, p.GetCritChance());
    }

    [Fact]
    public void GetCritDamage_NoStrength_Returns1_5()
    {
        var p = new Player { Strength = 1, BaseCritDamage = 1.5 };
        Assert.Equal(1.5, p.GetCritDamage());
    }

    [Fact]
    public void GetCritDamage_ClassBaseStrength_NoBonus()
    {
        var p = new Player { Strength = 3, BaseCritDamage = 1.5 };
        // Воин: сила 3 — это базис класса, «бесплатного» крит-урона нет
        Assert.Equal(1.5, p.GetCritDamage());
    }

    [Fact]
    public void GetCritDamage_WithStrength_Returns1_96()
    {
        var p = new Player { Strength = 26, BaseCritDamage = 1.5 };
        // Воин: 23 вложенных очка × 0.02 = 0.46 → 1.96
        Assert.Equal(1.96, p.GetCritDamage(), 3);
    }

    [Fact]
    public void GetCritDamage_AfterDiminishingThreshold_SlowsDown()
    {
        var p = new Player { Strength = 31, BaseCritDamage = 1.5 };
        // Воин: 28 очков: 25 полных + 3 с половинным темпом = 25.75 × 0.02 = 0.515 → 2.015
        Assert.Equal(2.015, p.GetCritDamage(), 3);
    }

    [Fact]
    public void GetCritDamage_CapReachedAt229Strength()
    {
        var p = new Player { Strength = 229, BaseCritDamage = 1.5 };
        // Воин: 226 вложенных очков: 25 + 201×0.25 = 75.25 × 0.02 = 1.505 → кап 3.0
        Assert.Equal(3.0, p.GetCritDamage());
    }

    [Fact]
    public void GetCritDamage_CappedAt3()
    {
        var p = new Player { Strength = 1000, BaseCritDamage = 1.5 };
        Assert.Equal(3.0, p.GetCritDamage());
    }

    [Fact]
    public void GetAttackSpeed_ClassBaseAgility_Returns1()
    {
        var p = new Player { Agility = 1 };
        // Воин: ловкость 1 — базис класса, скорость атаки ровно 1.0
        Assert.Equal(1.0, Balance.GetAttackSpeed(p.GetAttackSpeedPoints()));
    }

    [Fact]
    public void GetAttackSpeed_WithInvestedAgility_Increases()
    {
        var p = new Player { Agility = 11 };
        // Воин: 10 вложенных очков: 30×10/40/12 = 0.625 → 1.625
        Assert.Equal(1.625, Balance.GetAttackSpeed(p.GetAttackSpeedPoints()), 3);
    }

    [Fact]
    public void GetAttackSpeed_WithGearBonus_AddsPoints()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusAttackSpeed = 10 };
        var p = new Player { Agility = 11, Equipment = eq };
        // 10 ловкости + 10 шмота = 20 очков: 30×20/50/12 = 1.0 → 2.0
        Assert.Equal(2.0, Balance.GetAttackSpeed(p.GetAttackSpeedPoints()), 3);
    }

    [Fact]
    public void GetAttackSpeedWithWeapon_CappedAt2x()
    {
        // Огромное количество очков + быстрый модификатор оружия — не быстрее 2.0 (200%)
        Assert.Equal(2.0, Balance.GetAttackSpeedWithWeapon(10000, 1.4));
    }

    [Fact]
    public void GetAttackSpeedWithWeapon_SlowWeapon_GoesBelow100()
    {
        // Молот с множителем 0.7 — медленнее базовой скорости (70%)
        Assert.Equal(0.7, Balance.GetAttackSpeedWithWeapon(0, 0.7), 3);
    }

    [Fact]
    public void GetEvadeChance_NoCunning_Returns1()
    {
        var p = new Player { Cunning = 1, BaseEvadeChance = 1.0 };
        Assert.Equal(1.0, p.GetEvadeChance());
    }

    [Fact]
    public void GetEvadeChance_WithCunning_Returns4()
    {
        var p = new Player { Cunning = 11, BaseEvadeChance = 1.0 };
        // 1.0 + (11-1)*0.3 = 4.0 (убывающая отдача: 1 очко = 0.3%)
        Assert.Equal(4.0, p.GetEvadeChance());
    }

    [Fact]
    public void GetEvadeChance_AfterDiminishingThreshold_SlowsDown()
    {
        var p = new Player { Cunning = 111, BaseEvadeChance = 1.0 };
        // 110 очков: 100 по 0.3 (30) + 10 по 0.15 (1.5) → 1 + 31.5 = 32.5
        Assert.Equal(32.5, p.GetEvadeChance());
    }

    [Fact]
    public void GetEvadeChance_CapReachedAt228Cunning()
    {
        var p = new Player { Cunning = 228, BaseEvadeChance = 1.0 };
        // 227 очков: 100×0.3 + 127×0.15 = 49.05 → 1 + 49.05 = 50.05 → кап 50
        Assert.Equal(50.0, p.GetEvadeChance());
    }

    [Fact]
    public void GetEvadeChance_CappedAt50()
    {
        var p = new Player { Cunning = 1000, BaseEvadeChance = 1.0 };
        Assert.Equal(50.0, p.GetEvadeChance());
    }

    [Fact]
    public void GetParryChance_NoGear_ReturnsBase()
    {
        var p = new Player { Agility = 1000, BaseParryChance = 1.0 };
        // Атрибуты больше не влияют на парирование — только шмот
        Assert.Equal(1.0, p.GetParryChance());
    }

    [Fact]
    public void GetParryChance_WithGearPercent_AddsDirectly()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusParryChance = 3 };
        var p = new Player { BaseParryChance = 1.0, Equipment = eq };
        // Шмот даёт прямой процент: 1.0 + 3.0 = 4.0
        Assert.Equal(4.0, p.GetParryChance());
    }

    [Fact]
    public void GetParryChance_GearPercentCappedAt5()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusParryChance = 10 };
        var p = new Player { BaseParryChance = 1.0, Equipment = eq };
        // Вклад шмота не больше 5%: 1.0 + 5.0 = 6.0
        Assert.Equal(6.0, p.GetParryChance());
    }

    [Fact]
    public void GetParryChance_CappedAt25()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusParryChance = 5 };
        var p = new Player { BaseParryChance = 20.0, Equipment = eq };
        Assert.Equal(25.0, p.GetParryChance());
    }

    [Fact]
    public void GetBlockChance_NoGear_ReturnsBase()
    {
        var p = new Player { Endurance = 1000, BaseBlockChance = 1.0 };
        // Атрибуты больше не влияют на блок — только шмот
        Assert.Equal(1.0, p.GetBlockChance());
    }

    [Fact]
    public void GetBlockChance_WithGearPercent_AddsDirectly()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusBlockChance = 3 };
        var p = new Player { BaseBlockChance = 1.0, Equipment = eq };
        // Шмот даёт прямой процент: 1.0 + 3.0 = 4.0
        Assert.Equal(4.0, p.GetBlockChance());
    }

    [Fact]
    public void GetBlockChance_GearPercentCappedAt5()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusBlockChance = 10 };
        var p = new Player { BaseBlockChance = 1.0, Equipment = eq };
        // Вклад шмота не больше 5%: 1.0 + 5.0 = 6.0
        Assert.Equal(6.0, p.GetBlockChance());
    }

    [Fact]
    public void GetBlockChance_CappedAt25()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusBlockChance = 5 };
        var p = new Player { BaseBlockChance = 20.0, Equipment = eq };
        Assert.Equal(25.0, p.GetBlockChance());
    }

    [Fact]
    public void GetBlockChance_WithShield_AddsBaseBonus()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.LeftHand] = new Item { Type = "shield" };
        var p = new Player { BaseBlockChance = 1.0, Equipment = eq };
        // Щит даёт базовые +2% к блоку (атрибуты не влияют)
        Assert.Equal(3.0, p.GetBlockChance());
    }

    [Fact]
    public void GetAccuracy_NoAgility_Returns100()
    {
        var p = new Player { Agility = 1 };
        Assert.Equal(100.0, p.GetAccuracy());
    }

    [Fact]
    public void GetAccuracy_WithAgility_Returns115()
    {
        var p = new Player { Agility = 51 };
        // 100 + (51-1)*0.3 = 115 (убывающая отдача: 1 очко = 0.3%)
        Assert.Equal(115.0, p.GetAccuracy());
    }

    [Fact]
    public void GetAccuracy_AfterDiminishingThreshold_SlowsDown()
    {
        var p = new Player { Agility = 111 };
        // 110 очков: 100 по 0.3 (30) + 10 по 0.15 (1.5) → 100 + 31.5 = 131.5
        Assert.Equal(131.5, p.GetAccuracy());
    }

    [Fact]
    public void GetAccuracy_CapReachedAt235Agility()
    {
        var p = new Player { Agility = 235 };
        // 234 очка: 100×0.3 + 134×0.15 = 50.1 → 100 + 50.1 = 150.1 → кап 150
        Assert.Equal(150.0, p.GetAccuracy());
    }

    [Fact]
    public void GetAccuracy_CappedAt150()
    {
        var p = new Player { Agility = 1000 };
        Assert.Equal(150.0, p.GetAccuracy());
    }

    [Fact]
    public void GetTenacity_NoEndurance_Returns0()
    {
        var p = new Player { Endurance = 1 };
        Assert.Equal(0.0, p.GetTenacity());
    }

    [Fact]
    public void GetTenacity_WithEndurance_Returns14_4()
    {
        var p = new Player { Endurance = 51 };
        // Воин (база выносливости 3): 48 вложенных очков × 0.3 = 14.4
        Assert.Equal(14.4, p.GetTenacity(), 3);
    }

    [Fact]
    public void GetTenacity_AfterDiminishingThreshold_SlowsDown()
    {
        var p = new Player { Endurance = 111 };
        // Воин: 108 очков: 100×0.3 + 8×0.15 = 31.2
        Assert.Equal(31.2, p.GetTenacity());
    }

    [Fact]
    public void GetTenacity_CapReachedAt237Endurance()
    {
        var p = new Player { Endurance = 237 };
        // Воин: 234 очка: 100×0.3 + 134×0.15 = 50.1 → кап 50
        Assert.Equal(50.0, p.GetTenacity());
    }

    [Fact]
    public void GetTenacity_CappedAt50()
    {
        var p = new Player { Endurance = 1000 };
        Assert.Equal(50.0, p.GetTenacity());
    }

    [Fact]
    public void GetArmorPenetration_NoStrength_Returns0()
    {
        var p = new Player { Strength = 1 };
        Assert.Equal(0.0, p.GetArmorPenetration());
    }

    [Fact]
    public void GetArmorPenetration_WithStrength_Returns7_2()
    {
        var p = new Player { Strength = 51 };
        // Воин (база силы 3): 48 очков × 0.15 = 7.2
        Assert.Equal(7.2, p.GetArmorPenetration(), 3);
    }

    [Fact]
    public void GetArmorPenetration_CapReachedAt237Strength()
    {
        var p = new Player { Strength = 237 };
        // Воин: 234 очка: 100×0.15 + 134×0.075 = 25.05 → кап 25
        Assert.Equal(25.0, p.GetArmorPenetration());
    }

    [Fact]
    public void GetArmorPenetration_CappedAt25()
    {
        var p = new Player { Strength = 1000 };
        Assert.Equal(25.0, p.GetArmorPenetration());
    }

    [Fact]
    public void GetCooldownReduction_NoWisdom_Returns0()
    {
        var p = new Player { Wisdom = 1 };
        Assert.Equal(0.0, p.GetCooldownReduction());
    }

    [Fact]
    public void GetCooldownReduction_WithWisdom_Returns15()
    {
        var p = new Player { Wisdom = 51 };
        // Воин (база мудрости 1): 50 очков × 0.3 = 15
        Assert.Equal(15.0, p.GetCooldownReduction());
    }

    [Fact]
    public void GetCooldownReduction_CappedAt50()
    {
        var p = new Player { Wisdom = 1000 };
        Assert.Equal(50.0, p.GetCooldownReduction());
    }

    [Fact]
    public void GetCooldownReduction_ReducesSkillCdMult()
    {
        var p = new Player { Wisdom = 168 };
        // CDR 40.05 → множитель кулдауна 1 ранга = 1 × (1 − 0.4005) = 0.5995
        Assert.Equal(0.5995, p.GetSkillRankCdMult("x"), 3);
    }

    [Fact]
    public void GetHealthRegenPercent_NoEndurance_Returns0()
    {
        var p = new Player { Endurance = 1 };
        Assert.Equal(0.0, p.GetHealthRegenPercent());
    }

    [Fact]
    public void GetHealthRegenPercent_WithEndurance_Returns7_2()
    {
        var p = new Player { Endurance = 51 };
        // Воин: 48 очков × 0.15 = 7.2
        Assert.Equal(7.2, p.GetHealthRegenPercent(), 3);
    }

    [Fact]
    public void GetHealthRegenPercent_CapReachedAt237Endurance()
    {
        var p = new Player { Endurance = 237 };
        // Воин: 234 очка: 100×0.15 + 134×0.075 = 25.05 → кап 25
        Assert.Equal(25.0, p.GetHealthRegenPercent());
    }

    [Fact]
    public void GetTenacity_WithGearBonus_AddsToDiminishingCurve()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusTenacity = 20 };
        var p = new Player { Endurance = 51, Equipment = eq };
        // (51 − 3) + 20 = 68 очков: 68 × 0.3 = 20.4
        Assert.Equal(20.4, p.GetTenacity(), 3);
    }

    [Fact]
    public void GetArmorPenetration_WithGearBonus_AddsToDiminishingCurve()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusArmorPenetration = 20 };
        var p = new Player { Strength = 51, Equipment = eq };
        // (51 − 3) + 20 = 68 очков: 68 × 0.15 = 10.2
        Assert.Equal(10.2, p.GetArmorPenetration(), 3);
    }

    [Fact]
    public void GetCooldownReduction_WithGearBonus_AddsToDiminishingCurve()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusCooldownReduction = 20 };
        var p = new Player { Wisdom = 51, Equipment = eq };
        // (51 − 1) + 20 = 70 очков: 70 × 0.3 = 21
        Assert.Equal(21.0, p.GetCooldownReduction(), 3);
    }

    [Fact]
    public void GetHealthRegenPercent_WithGearBonus_AddsToDiminishingCurve()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusHpRegen = 20 };
        var p = new Player { Endurance = 51, Equipment = eq };
        // (51 − 3) + 20 = 68 очков: 68 × 0.15 = 10.2
        Assert.Equal(10.2, p.GetHealthRegenPercent(), 3);
    }

    [Fact]
    public void GetManaRegenPercent_WithGearBonus_AddsToDiminishingCurve()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusMpRegen = 20 };
        var p = new Player { Wisdom = 51, Equipment = eq };
        // (51 − 1) + 20 = 70 очков: 70 × 0.15 = 10.5
        Assert.Equal(10.5, p.GetManaRegenPercent(), 3);
    }

    [Fact]
    public void GetAccuracy_WithGearBonus_AddsToDiminishingCurve()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusAccuracy = 20 };
        var p = new Player { Agility = 51, Equipment = eq };
        // База 100 + (51 − 1) + 20 = 70 очков × 0.3 = 21 → 121
        Assert.Equal(121.0, p.GetAccuracy(), 3);
    }

    [Fact]
    public void GetTenacity_WithGearBonus_CapStillEnforced()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusTenacity = 500 };
        var p = new Player { Endurance = 237, Equipment = eq };
        Assert.Equal(50.0, p.GetTenacity());
    }

    [Fact]
    public void GetHealthRegenPercent_CappedAt25()
    {
        var p = new Player { Endurance = 1000 };
        Assert.Equal(25.0, p.GetHealthRegenPercent());
    }

    [Fact]
    public void GetManaRegenPercent_NoWisdom_Returns0()
    {
        var p = new Player { Wisdom = 1 };
        Assert.Equal(0.0, p.GetManaRegenPercent());
    }

    [Fact]
    public void GetManaRegenPercent_WithWisdom_Returns7_5()
    {
        var p = new Player { Wisdom = 51 };
        // 50 очков × 0.15 = 7.5
        Assert.Equal(7.5, p.GetManaRegenPercent());
    }

    [Fact]
    public void GetManaRegenPercent_CapReachedAt168Wisdom()
    {
        var p = new Player { Wisdom = 168 };
        // 167 очков: 100×0.15 + 67×0.075 = 20.025 → кап 20
        Assert.Equal(20.0, p.GetManaRegenPercent());
    }

    [Fact]
    public void GetEffStrength_WithEquipment_ReturnsSum()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { BonusStrength = 3 };
        eq[EquipmentSlots.Torso] = new Item { BonusStrength = 1 };
        var p = new Player
        {
            Strength = 2,
            Equipment = eq
        };
        // 2 + 3 + 1 = 6
        Assert.Equal(6, p.GetEffStrength());
    }

    [Fact]
    public void GetTotalAttack_WithEquipment_AddsBonus()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { BonusPhysAttack = 10 };
        var p = new Player
        {
            Level = 1,
            Strength = 1,
            Equipment = eq
        };
        // BaseDmg=1 + (1-1)*2=0 + weaponAtk=10 = 11
        Assert.Equal(11, p.GetTotalAttack());
    }

    [Fact]
    public void GetTotalDefense_WithEquipment_AddsBonus()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusDefense = 8 };
        var p = new Player
        {
            Level = 1,
            Endurance = 1,
            Equipment = eq
        };
        // BaseDef=1 + (1-1)*1=0 + armorDef=8 = 9
        Assert.Equal(9, p.GetTotalDefense());
    }
}
