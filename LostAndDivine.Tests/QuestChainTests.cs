using LostAndDivine.Server;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Tests;

public class QuestChainTests
{
    private static QuestManager CreateManager(params QuestDefinition[] defs)
    {
        var qm = new QuestManager(new GameWorld(100, 100));
        if (defs.Length > 0) qm.SetDefinitions(defs);
        return qm;
    }

    private static QuestDefinition Def(string id, string prereq = "", int minLevel = 1)
        => new QuestDefinition { Id = id, PrerequisiteQuestId = prereq, MinLevel = minLevel, Type = "kill", Target = 1 };

    private static Player Player(int level = 1)
        => new Player { Name = "Tester", Level = level };

    [Fact]
    public void CanTakeQuest_FirstLink_NoPrereq()
    {
        var qm = CreateManager();
        var def = Def("Q0009");
        Assert.True(qm.CanTakeQuest(Player(), def));
    }

    [Fact]
    public void CanTakeQuest_SecondLink_LockedUntilPrereqCompleted()
    {
        var qm = CreateManager();
        var next = Def("Q0010", prereq: "Q0009");
        var p = Player();

        Assert.False(qm.CanTakeQuest(p, next));

        p.CompletedQuestIds.Add("Q0009");
        Assert.True(qm.CanTakeQuest(p, next));
    }

    [Fact]
    public void CanTakeQuest_RejectsIfAlreadyCompleted()
    {
        var qm = CreateManager();
        var def = Def("Q0009");
        var p = Player();
        p.CompletedQuestIds.Add("Q0009");
        Assert.False(qm.CanTakeQuest(p, def));
    }

    [Fact]
    public void CanTakeQuest_RejectsIfActive()
    {
        var qm = CreateManager();
        var def = Def("Q0009");
        var p = Player();
        qm.TakeQuest(p, def);
        Assert.False(qm.CanTakeQuest(p, def));
    }

    [Fact]
    public void CanTakeQuest_RejectsBelowMinLevel()
    {
        var qm = CreateManager();
        var def = Def("Q0010", minLevel: 5);
        Assert.False(qm.CanTakeQuest(Player(level: 3), def));
        Assert.True(qm.CanTakeQuest(Player(level: 5), def));
    }

    [Fact]
    public void CompleteQuest_RecordsHistory()
    {
        var def = Def("Q0009");
        var qm = CreateManager(def);
        var p = Player();
        qm.TakeQuest(p, def);
        var prog = p.ActiveQuests.Single();
        prog.Completed = true;

        var result = qm.CompleteQuest(p, "Q0009");

        Assert.True(result.Success);
        Assert.Contains("Q0009", p.CompletedQuestIds);
        Assert.Empty(p.ActiveQuests);
    }

    [Fact]
    public void CompleteQuest_RejectsNotCompleted()
    {
        var def = Def("Q0009");
        var qm = CreateManager(def);
        var p = Player();
        qm.TakeQuest(p, def);

        var result = qm.CompleteQuest(p, "Q0009");

        Assert.False(result.Success);
        Assert.Equal(2, result.ErrorKind);
    }

    [Fact]
    public void IncrementTalkProgress_MarksCompleteAtTarget()
    {
        var def = new QuestDefinition { Id = "Q0020", Type = "talk", TargetNpcId = "N0003", Target = 2 };
        var qm = CreateManager(def);

        var p = Player();
        qm.TakeQuest(p, def);
        var prog = p.ActiveQuests.Single();

        qm.IncrementTalkProgress(p, "N0003");
        qm.IncrementTalkProgress(p, "N0003");
        Assert.Equal(2, prog.Current);
        Assert.True(prog.Completed);
    }
}
