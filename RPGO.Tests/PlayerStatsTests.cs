using RPGGame.Shared.Models;
using RPGGame.Server;

namespace RPGO.Tests;

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
        var p = new Player { Level = 1, Stamina = 1 };
        Assert.Equal(1, p.GetTotalDefense());
    }

    [Fact]
    public void GetTotalDefense_WithStamina_ReturnsCorrectly()
    {
        var p = new Player { Level = 3, Stamina = 5 };
        // BaseDef=3 + (5-1)*1=4 = 7
        Assert.Equal(7, p.GetTotalDefense());
    }

    [Fact]
    public void GetCritChance_NoAgility_Returns1()
    {
        var p = new Player { Agility = 1, BaseCritChance = 1.0 };
        Assert.Equal(1.0, p.GetCritChance());
    }

    [Fact]
    public void GetCritChance_WithAgility_ReturnsCorrectly()
    {
        var p = new Player { Agility = 6, BaseCritChance = 1.0 };
        // 1.0 + (6-1)*1.0 = 6.0
        Assert.Equal(6.0, p.GetCritChance());
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
        var p = new Player { Strength = 11, BaseCritDamage = 1.5 };
        // 1.5 + (11-1)*0.05 = 2.0
        Assert.Equal(2.0, p.GetCritDamage());
    }

    [Fact]
    public void GetEvadeChance_NoAgility_Returns1()
    {
        var p = new Player { Agility = 1, BaseEvadeChance = 1.0 };
        Assert.Equal(1.0, p.GetEvadeChance());
    }

    [Fact]
    public void GetEvadeChance_WithAgility_Returns11()
    {
        var p = new Player { Agility = 11, BaseEvadeChance = 1.0 };
        // 1.0 + (11-1)*1.0 = 11.0
        Assert.Equal(11.0, p.GetEvadeChance());
    }

    [Fact]
    public void GetEffStrength_WithEquipment_ReturnsSum()
    {
        var p = new Player
        {
            Strength = 2,
            Equipment = new Equipment
            {
                Weapon = new Item { BonusStrength = 3 },
                Armor = new Item { BonusStrength = 1 }
            }
        };
        // 2 + 3 + 1 = 6
        Assert.Equal(6, p.GetEffStrength());
    }

    [Fact]
    public void GetTotalAttack_WithEquipment_AddsBonus()
    {
        var p = new Player
        {
            Level = 1,
            Strength = 1,
            Equipment = new Equipment
            {
                Weapon = new Item { Attack = 10 }
            }
        };
        // BaseDmg=1 + (1-1)*2=0 + weaponAtk=10 = 11
        Assert.Equal(11, p.GetTotalAttack());
    }

    [Fact]
    public void GetTotalDefense_WithEquipment_AddsBonus()
    {
        var p = new Player
        {
            Level = 1,
            Stamina = 1,
            Equipment = new Equipment
            {
                Armor = new Item { Defense = 8 }
            }
        };
        // BaseDef=1 + (1-1)*1=0 + armorDef=8 = 9
        Assert.Equal(9, p.GetTotalDefense());
    }
}
