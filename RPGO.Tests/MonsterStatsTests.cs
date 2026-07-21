using RPGGame.Shared.Models;

namespace RPGO.Tests;

public class MonsterStatsTests
{
    [Fact]
    public void GetBaseDamage_Level1_Returns1()
    {
        var m = new Monster { Level = 1 };
        Assert.Equal(1, m.GetBaseDamage());
    }

    [Fact]
    public void GetBaseDamage_Level5_Returns5()
    {
        var m = new Monster { Level = 5 };
        Assert.Equal(5, m.GetBaseDamage());
    }

    [Fact]
    public void GetBaseDefense_Level1_Returns1()
    {
        var m = new Monster { Level = 1 };
        Assert.Equal(1, m.GetBaseDefense());
    }

    [Fact]
    public void GetTotalAttack_Level3_Str5_Returns11()
    {
        var m = new Monster { Level = 3, Strength = 5 };
        // BaseDmg=3 + (5-1)*2=8 = 11
        Assert.Equal(11, m.GetTotalAttack());
    }

    [Fact]
    public void GetTotalDefense_Level3_Sta5_Returns7()
    {
        var m = new Monster { Level = 3, Endurance = 5 };
        // BaseDef=3 + (5-1)*1=4 = 7
        Assert.Equal(7, m.GetTotalDefense());
    }

    [Fact]
    public void GetCritChance_Agi1_Base1_Returns1()
    {
        var m = new Monster { Agility = 1, CritChance = 1.0 };
        Assert.Equal(1.0, m.GetCritChance());
    }

    [Fact]
    public void GetCritDamage_Str1_Base1_5_Returns1_5()
    {
        var m = new Monster { Strength = 1, CritDamage = 1.5 };
        Assert.Equal(1.5, m.GetCritDamage());
    }

    [Fact]
    public void GetEvadeChance_Agi1_Base1_Returns1()
    {
        var m = new Monster { Agility = 1, EvadeChance = 1.0 };
        Assert.Equal(1.0, m.GetEvadeChance());
    }
}
