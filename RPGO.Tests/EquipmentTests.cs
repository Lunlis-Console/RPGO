using RPGGame.Shared.Models;

namespace RPGO.Tests;

public class EquipmentTests
{
    [Fact]
    public void EmptyEquipment_AllBonusesZero()
    {
        var eq = new Equipment();
        Assert.Equal(0, eq.GetBonusAttack());
        Assert.Equal(0, eq.GetBonusDefense());
        Assert.Equal(0, eq.GetBonusMaxHealth());
        Assert.Equal(0, eq.GetBonusStrength());
        Assert.Equal(0, eq.GetBonusStamina());
        Assert.Equal(0, eq.GetBonusAgility());
        Assert.Equal(0, eq.GetBonusCunning());
        Assert.Equal(0, eq.GetBonusWisdom());
        Assert.Equal(0, eq.GetBonusWill());
        Assert.Equal(0, eq.GetBonusCritChance());
        Assert.Equal(0, eq.GetBonusCritDamage());
        Assert.Equal(0, eq.GetBonusEvadeChance());
    }

    [Fact]
    public void WeaponAttack_AddsToBonus()
    {
        var eq = new Equipment { Weapon = new Item { Attack = 10 } };
        Assert.Equal(10, eq.GetBonusAttack());
    }

    [Fact]
    public void AllSlotsAttack_SumCorrectly()
    {
        var eq = new Equipment
        {
            Weapon = new Item { Attack = 5 },
            Armor = new Item { Attack = 3 },
            Accessory = new Item { Attack = 2 }
        };
        Assert.Equal(10, eq.GetBonusAttack());
    }

    [Fact]
    public void ArmorDefense_AddsToBonus()
    {
        var eq = new Equipment { Armor = new Item { Defense = 8 } };
        Assert.Equal(8, eq.GetBonusDefense());
    }

    [Fact]
    public void AllSlotsMaxHealth_SumCorrectly()
    {
        var eq = new Equipment
        {
            Weapon = new Item { MaxHealthBonus = 10 },
            Armor = new Item { MaxHealthBonus = 10 },
            Accessory = new Item { MaxHealthBonus = 10 }
        };
        Assert.Equal(30, eq.GetBonusMaxHealth());
    }

    [Fact]
    public void Strength_BonusFromWeapon()
    {
        var eq = new Equipment { Weapon = new Item { BonusStrength = 3 } };
        Assert.Equal(3, eq.GetBonusStrength());
    }

    [Fact]
    public void Stamina_BonusFromArmor()
    {
        var eq = new Equipment { Armor = new Item { BonusStamina = 5 } };
        Assert.Equal(5, eq.GetBonusStamina());
    }

    [Fact]
    public void Agility_FromMultipleSlots_Sums()
    {
        var eq = new Equipment
        {
            Weapon = new Item { BonusAgility = 3 },
            Accessory = new Item { BonusAgility = 2 }
        };
        Assert.Equal(5, eq.GetBonusAgility());
    }

    [Fact]
    public void CritChance_AddsFromAllSlots()
    {
        var eq = new Equipment
        {
            Weapon = new Item { BonusCritChance = 5.0 },
            Armor = new Item { BonusCritChance = 2.0 }
        };
        Assert.Equal(7.0, eq.GetBonusCritChance());
    }

    [Fact]
    public void CritDamage_AddsFromAllSlots()
    {
        var eq = new Equipment
        {
            Weapon = new Item { BonusCritDamage = 0.3 },
            Armor = new Item { BonusCritDamage = 0.2 }
        };
        Assert.Equal(0.5, eq.GetBonusCritDamage());
    }

    [Fact]
    public void EvadeChance_AddsFromAllSlots()
    {
        var eq = new Equipment
        {
            Accessory = new Item { BonusEvadeChance = 4.0 }
        };
        Assert.Equal(4.0, eq.GetBonusEvadeChance());
    }

    [Fact]
    public void Wisdom_BonusFromAccessory()
    {
        var eq = new Equipment { Accessory = new Item { BonusWisdom = 7 } };
        Assert.Equal(7, eq.GetBonusWisdom());
    }

    [Fact]
    public void Will_BonusFromArmor()
    {
        var eq = new Equipment { Armor = new Item { BonusWill = 4 } };
        Assert.Equal(4, eq.GetBonusWill());
    }
}
