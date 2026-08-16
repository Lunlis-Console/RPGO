using LostAndDivine.Server.Instances;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Tests;

public class InstanceManagerTests
{
    [Theory]
    [InlineData("Подземелье (ур. 1-5)", 1, 5)]
    [InlineData("Подземелье (ур. 41-45)", 41, 45)]
    [InlineData("Подземелье (ур. 46-50)", 46, 50)]
    public void ParseLevelBracket_FromName(string name, int min, int max)
    {
        var t = new InstanceTemplate { Name = name };
        Assert.Equal((min, max), InstanceManager.ParseLevelBracket(t));
    }

    [Fact]
    public void ParseLevelBracket_NoRange_NoRestriction()
    {
        var t = new InstanceTemplate { Name = "Катакомбы" };
        Assert.Null(InstanceManager.ParseLevelBracket(t));
    }

    private static bool CanEnter(int playerLevel, int dungeonMin, int dungeonMax)
    {
        if (dungeonMin > playerLevel) return false;
        int ownMin = ((playerLevel - 1) / 5) * 5 + 1;
        return dungeonMin >= ownMin - 5;
    }

    [Theory]
    [InlineData(50, 46, 50, true)]   // свой диапазон
    [InlineData(50, 41, 45, true)]   // на один ниже
    [InlineData(50, 36, 40, false)]  // ниже не пускает
    [InlineData(50, 51, 55, false)]  // выше тоже
    [InlineData(44, 41, 45, true)]
    [InlineData(44, 36, 40, true)]
    [InlineData(1, 1, 5, true)]
    [InlineData(2, 6, 10, false)]
    [InlineData(46, 41, 45, true)]
    [InlineData(10, 6, 10, true)]
    [InlineData(10, 1, 5, true)]
    public void LevelGate_Rule(int level, int dMin, int dMax, bool expected)
        => Assert.Equal(expected, CanEnter(level, dMin, dMax));
}