using RPGGame.Shared.Models;

namespace RPGO.Tests;

public class QuestProgressTests
{
    [Fact]
    public void NewProgress_IsNotCompleted()
    {
        var q = new QuestProgress { QuestId = "Q0001", Current = 0 };
        // Initial state: current < target, not completed
        Assert.False(q.Completed);
        Assert.Equal(0, q.Current);
    }

    [Fact]
    public void Progress_IncrementsCurrent()
    {
        var q = new QuestProgress { QuestId = "Q0001", Current = 0 };
        q.Current++;
        q.Current++;
        Assert.Equal(2, q.Current);
    }

    [Fact]
    public void QuestDefinition_HasCorrectDefaults()
    {
        var def = new QuestDefinition
        {
            Id = "Q0001",
            Title = "Test",
            Type = "kill",
            Target = 5,
            XpReward = 100,
            GoldReward = 50
        };
        Assert.Equal("kill", def.Type);
        Assert.Equal(5, def.Target);
        Assert.Equal(100, def.XpReward);
    }

    [Fact]
    public void MultipleQuests_IndependentProgress()
    {
        var q1 = new QuestProgress { QuestId = "Q0001", Current = 3 };
        var q2 = new QuestProgress { QuestId = "Q0002", Current = 7 };

        q1.Current++;
        Assert.Equal(4, q1.Current);
        Assert.Equal(7, q2.Current);
    }
}
