using RPGGame.Server;

namespace RPGO.Tests;

public class BalanceTests
{
    [Fact]
    public void MoveIntervalMs_Speed1_Returns500()
        => Assert.Equal(500, Balance.MoveIntervalMs(1));

    [Fact]
    public void MoveIntervalMs_Speed5_Returns100()
        => Assert.Equal(100, Balance.MoveIntervalMs(5));

    [Fact]
    public void MoveIntervalMs_Speed0_ClampsTo500()
        => Assert.Equal(500, Balance.MoveIntervalMs(0));

    [Fact]
    public void AttackIntervalMs_Speed1_Returns1000()
        => Assert.Equal(1000, Balance.AttackIntervalMs(1));

    [Fact]
    public void AttackIntervalMs_Speed2_Returns500()
        => Assert.Equal(500, Balance.AttackIntervalMs(2));

    [Fact]
    public void GetAttackSpeed_Agi0_Returns1()
        => Assert.Equal(1, Balance.GetAttackSpeed(0));

    [Fact]
    public void GetAttackSpeed_Agi10_Returns2()
        => Assert.Equal(2, Balance.GetAttackSpeed(10));

    [Fact]
    public void XpNeededForNextLevel_Level1_Returns50()
        => Assert.Equal(50, Balance.XpNeededForNextLevel(1));

    [Fact]
    public void XpNeededForNextLevel_Level10_Returns500()
        => Assert.Equal(500, Balance.XpNeededForNextLevel(10));

    [Fact]
    public void BuyPrice_ReturnsCorrectly()
    {
        Assert.Equal(100, Balance.BuyPrice(100));
        Assert.Equal(90, Balance.BuyPrice(90));
        Assert.Equal(70, Balance.BuyPrice(70));
    }

    [Fact]
    public void SellPrice_Returns50Percent()
    {
        Assert.Equal(50, Balance.SellPrice(100));
        Assert.Equal(1, Balance.SellPrice(1));
        Assert.Equal(1, Balance.SellPrice(3));
    }

    [Fact]
    public void BuybackPrice_Returns50Percent()
    {
        Assert.Equal(50, Balance.BuybackPrice(100));
        Assert.Equal(1, Balance.BuybackPrice(1));
    }

    [Fact]
    public void ComputeDeathGoldLoss_CappedAt20()
    {
        Assert.Equal(10, Balance.ComputeDeathGoldLoss(10));
        Assert.Equal(20, Balance.ComputeDeathGoldLoss(50));
        Assert.Equal(0, Balance.ComputeDeathGoldLoss(0));
    }

    [Fact]
    public void RespawnHealth_Returns50Percent()
    {
        Assert.Equal(50, Balance.RespawnHealth(100));
        Assert.Equal(100, Balance.RespawnHealth(200));
    }

    [Fact]
    public void MaxMana_Will1_Returns20()
        => Assert.Equal(20, Balance.MaxMana(1));

    [Fact]
    public void MaxMana_Will5_Returns40()
        => Assert.Equal(40, Balance.MaxMana(5));

    [Fact]
    public void MonsterTierByDistance_ReturnsCorrectTier()
    {
        Assert.Equal(1, Balance.MonsterTierByDistance(10));
        Assert.Equal(1, Balance.MonsterTierByDistance(15));
        Assert.Equal(2, Balance.MonsterTierByDistance(20));
        Assert.Equal(3, Balance.MonsterTierByDistance(30));
        Assert.Equal(4, Balance.MonsterTierByDistance(40));
    }

    [Fact]
    public void MaxStackForType_Consumable_Returns10()
        => Assert.Equal(10, Balance.MaxStackForType("consumable"));

    [Fact]
    public void MaxStackForType_Weapon_Returns1()
        => Assert.Equal(1, Balance.MaxStackForType("weapon"));

    [Fact]
    public void MaxStackForType_Null_Returns1()
        => Assert.Equal(1, Balance.MaxStackForType(null));
}
