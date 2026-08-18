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
    public void GetCritDamage_WithStrength_Returns2()
    {
        var p = new Player { Strength = 26, BaseCritDamage = 1.5 };
        // 25 очков по 0.02 = 0.5 → 2.0 (граница линейного участка)
        Assert.Equal(2.0, p.GetCritDamage());
    }

    [Fact]
    public void GetCritDamage_AfterDiminishingThreshold_SlowsDown()
    {
        var p = new Player { Strength = 31, BaseCritDamage = 1.5 };
        // 30 очков: 25 по 0.02 (0.5) + 5 по 0.005 (0.025) → 1.5+0.525 = 2.025
        Assert.Equal(2.025, p.GetCritDamage());
    }

    [Fact]
    public void GetCritDamage_CapReachedAt226Strength()
    {
        var p = new Player { Strength = 226, BaseCritDamage = 1.5 };
        // 225 очков: 25×0.02 + 200×0.005 = 1.5 → ровно кап 3.0
        Assert.Equal(3.0, p.GetCritDamage());
    }

    [Fact]
    public void GetCritDamage_CappedAt3()
    {
        var p = new Player { Strength = 1000, BaseCritDamage = 1.5 };
        Assert.Equal(3.0, p.GetCritDamage());
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
    public void GetParryChance_NoAgility_Returns1()
    {
        var p = new Player { Agility = 1, BaseParryChance = 1.0 };
        Assert.Equal(1.0, p.GetParryChance());
    }

    [Fact]
    public void GetParryChance_WithAgility_Returns2_5()
    {
        var p = new Player { Agility = 11, BaseParryChance = 1.0 };
        // 1.0 + (11-1)*0.15 = 2.5 (убывающая отдача: 1 очко = 0.15%)
        Assert.Equal(2.5, p.GetParryChance());
    }

    [Fact]
    public void GetParryChance_AfterDiminishingThreshold_SlowsDown()
    {
        var p = new Player { Agility = 111, BaseParryChance = 1.0 };
        // 110 очков: 100 по 0.15 (15) + 10 по 0.075 (0.75) → 1 + 15.75 = 16.75
        Assert.Equal(16.75, p.GetParryChance());
    }

    [Fact]
    public void GetParryChance_CapReachedAt221Agility()
    {
        var p = new Player { Agility = 221, BaseParryChance = 1.0 };
        // 220 очков: 100×0.15 + 120×0.075 = 24 → 1 + 24 = 25 → кап
        Assert.Equal(25.0, p.GetParryChance());
    }

    [Fact]
    public void GetParryChance_CappedAt25()
    {
        var p = new Player { Agility = 1000, BaseParryChance = 1.0 };
        Assert.Equal(25.0, p.GetParryChance());
    }

    [Fact]
    public void GetBlockChance_NoEndurance_Returns1()
    {
        var p = new Player { Endurance = 1, BaseBlockChance = 1.0 };
        Assert.Equal(1.0, p.GetBlockChance());
    }

    [Fact]
    public void GetBlockChance_WithEndurance_Returns2_5()
    {
        var p = new Player { Endurance = 11, BaseBlockChance = 1.0 };
        // 1.0 + (11-1)*0.15 = 2.5 (убывающая отдача: 1 очко = 0.15%)
        Assert.Equal(2.5, p.GetBlockChance());
    }

    [Fact]
    public void GetBlockChance_AfterDiminishingThreshold_SlowsDown()
    {
        var p = new Player { Endurance = 111, BaseBlockChance = 1.0 };
        // 110 очков: 100 по 0.15 (15) + 10 по 0.075 (0.75) → 1 + 15.75 = 16.75
        Assert.Equal(16.75, p.GetBlockChance());
    }

    [Fact]
    public void GetBlockChance_CapReachedAt221Endurance()
    {
        var p = new Player { Endurance = 221, BaseBlockChance = 1.0 };
        // 220 очков: 100×0.15 + 120×0.075 = 24 → 1 + 24 = 25 → кап
        Assert.Equal(25.0, p.GetBlockChance());
    }

    [Fact]
    public void GetBlockChance_CappedAt25()
    {
        var p = new Player { Endurance = 1000, BaseBlockChance = 1.0 };
        Assert.Equal(25.0, p.GetBlockChance());
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
