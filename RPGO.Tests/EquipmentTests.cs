using RPGGame.Shared.Models;

namespace RPGO.Tests;

public class EquipmentTests
{
    [Fact]
    public void EmptyEquipment_AllBonusesZero()
    {
        var eq = new Equipment();
        Assert.Equal(0, eq.GetBonusPhysAttack());
        Assert.Equal(0, eq.GetBonusDefense());
        Assert.Equal(0, eq.GetBonusMaxHealth());
        Assert.Equal(0, eq.GetBonusStrength());
        Assert.Equal(0, eq.GetBonusEndurance());
        Assert.Equal(0, eq.GetBonusAgility());
        Assert.Equal(0, eq.GetBonusCunning());
        Assert.Equal(0, eq.GetBonusIntellect());
        Assert.Equal(0, eq.GetBonusWisdom());
        Assert.Equal(0, eq.GetBonusCritChance());
        Assert.Equal(0, eq.GetBonusCritDamage());
        Assert.Equal(0, eq.GetBonusEvadeChance());
    }

    [Fact]
    public void WeaponAttack_AddsToBonus()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { BonusPhysAttack = 10 };
        Assert.Equal(10, eq.GetBonusPhysAttack());
    }

    [Fact]
    public void AllSlotsAttack_SumCorrectly()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { BonusPhysAttack = 5 };
        eq[EquipmentSlots.Torso] = new Item { BonusPhysAttack = 3 };
        eq[EquipmentSlots.Neck] = new Item { BonusPhysAttack = 2 };
        Assert.Equal(10, eq.GetBonusPhysAttack());
    }

    [Fact]
    public void ArmorDefense_AddsToBonus()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusDefense = 8 };
        Assert.Equal(8, eq.GetBonusDefense());
    }

    [Fact]
    public void AllSlotsMaxHealth_SumCorrectly()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { MaxHealthBonus = 10 };
        eq[EquipmentSlots.Torso] = new Item { MaxHealthBonus = 10 };
        eq[EquipmentSlots.Neck] = new Item { MaxHealthBonus = 10 };
        Assert.Equal(30, eq.GetBonusMaxHealth());
    }

    [Fact]
    public void Strength_BonusFromWeapon()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { BonusStrength = 3 };
        Assert.Equal(3, eq.GetBonusStrength());
    }

    [Fact]
    public void Stamina_BonusFromArmor()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusEndurance = 5 };
        Assert.Equal(5, eq.GetBonusEndurance());
    }

    [Fact]
    public void Agility_FromMultipleSlots_Sums()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { BonusAgility = 3 };
        eq[EquipmentSlots.Neck] = new Item { BonusAgility = 2 };
        Assert.Equal(5, eq.GetBonusAgility());
    }

    [Fact]
    public void CritChance_AddsFromAllSlots()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { BonusCritChance = 5.0 };
        eq[EquipmentSlots.Torso] = new Item { BonusCritChance = 2.0 };
        Assert.Equal(7.0, eq.GetBonusCritChance());
    }

    [Fact]
    public void CritDamage_AddsFromAllSlots()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { BonusCritDamage = 0.3 };
        eq[EquipmentSlots.Torso] = new Item { BonusCritDamage = 0.2 };
        Assert.Equal(0.5, eq.GetBonusCritDamage());
    }

    [Fact]
    public void EvadeChance_AddsFromAllSlots()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Neck] = new Item { BonusEvadeChance = 4.0 };
        Assert.Equal(4.0, eq.GetBonusEvadeChance());
    }

    [Fact]
    public void Intellect_BonusFromAccessory()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Neck] = new Item { BonusIntellect = 7 };
        Assert.Equal(7, eq.GetBonusIntellect());
    }

    [Fact]
    public void Wisdom_BonusFromArmor()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.Torso] = new Item { BonusWisdom = 4 };
        Assert.Equal(4, eq.GetBonusWisdom());
    }

    [Fact]
    public void WeaponSpeedModifier_DefaultIsOne()
    {
        var eq = new Equipment();
        Assert.Equal(1.0, eq.GetWeaponSpeedModifier());
    }

    [Fact]
    public void WeaponSpeedModifier_FromEquippedWeapon()
    {
        var eq = new Equipment();
        eq[EquipmentSlots.RightHand] = new Item { AttackSpeedModifier = 1.3 };
        Assert.Equal(1.3, eq.GetWeaponSpeedModifier());
    }

    [Fact]
    public void WeaponDamageType_ReturnsFromWeapon()
    {
        var eq = new Equipment();
        Assert.Equal("", eq.GetWeaponDamageType());
        eq[EquipmentSlots.RightHand] = new Item { DamageType = "slashing" };
        Assert.Equal("slashing", eq.GetWeaponDamageType());
    }
}
