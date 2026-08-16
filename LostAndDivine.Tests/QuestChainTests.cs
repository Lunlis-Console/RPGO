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
    public void CompleteQuest_Repeatable_NotRecordedInHistory()
    {
        var def = Def("Q0009");
        def.Repeatable = true;
        var qm = CreateManager(def);
        var p = Player();
        qm.TakeQuest(p, def);
        p.ActiveQuests.Single().Completed = true;

        var result = qm.CompleteQuest(p, "Q0009");

        Assert.True(result.Success);
        Assert.Empty(p.CompletedQuestIds);
        Assert.True(qm.CanTakeQuest(p, def));
    }

    [Fact]
    public void CompleteQuest_NonRepeatable_BlocksRetake()
    {
        var def = Def("Q0009");
        var qm = CreateManager(def);
        var p = Player();
        qm.TakeQuest(p, def);
        p.ActiveQuests.Single().Completed = true;
        qm.CompleteQuest(p, "Q0009");

        Assert.False(qm.CanTakeQuest(p, def));
    }

    [Fact]
    public void CanTakeQuest_Repeatable_AllowsRetakeFromHistory()
    {
        // Запись в истории выполнения могла появиться до включения флага
        // «повторяемый» — повторяемый квест всё равно можно взять снова.
        var def = Def("Q0009");
        def.Repeatable = true;
        var qm = CreateManager(def);
        var p = Player();
        p.CompletedQuestIds.Add("Q0009");

        Assert.True(qm.CanTakeQuest(p, def));
        Assert.True(qm.TakeQuest(p, def));
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

    [Fact]
    public void IncrementTravelProgress_ReachesCoordinates()
    {
        var def = new QuestDefinition { Id = "Q0030", Type = "travel", TargetX = 10, TargetY = 12, Target = 1 };
        var qm = CreateManager(def);

        var p = Player();
        qm.TakeQuest(p, def);
        var prog = p.ActiveQuests.Single();

        Assert.Empty(qm.IncrementTravelProgress(p, "zone_main", 5, 5));
        Assert.Equal(0, prog.Current);

        var results = qm.IncrementTravelProgress(p, "zone_main", 10, 12);
        Assert.Single(results);
        Assert.True(results[0].Completed);
        Assert.True(prog.Completed);
    }

    [Fact]
    public void IncrementTravelProgress_ReachesNpc()
    {
        var def = new QuestDefinition { Id = "Q0031", Type = "travel", TargetNpcId = "N0005", Target = 1 };
        var qm = CreateManager(def);
        qm.NpcLookup = (zoneId, npcId) => npcId == "N0005" && zoneId == "zone_main"
            ? new NpcPosition { Id = "N0005", ZoneId = "zone_main", X = 20, Y = 25 }
            : null;

        var p = Player();
        qm.TakeQuest(p, def);
        var prog = p.ActiveQuests.Single();

        Assert.Empty(qm.IncrementTravelProgress(p, "zone_main", 18, 25));
        Assert.Empty(qm.IncrementTravelProgress(p, "zone_forest", 20, 25));

        var results = qm.IncrementTravelProgress(p, "zone_main", 21, 25);
        Assert.Single(results);
        Assert.True(prog.Completed);
    }

    [Fact]
    public void IncrementUseProgress_CountsConsumedItems()
    {
        var def = new QuestDefinition { Id = "Q0032", Type = "use", TargetItemId = "I0020", Target = 3 };
        var qm = CreateManager(def);

        var p = Player();
        qm.TakeQuest(p, def);
        var prog = p.ActiveQuests.Single();

        qm.IncrementUseProgress(p, "I0020");
        qm.IncrementUseProgress(p, "I0020");
        Assert.Equal(2, prog.Current);
        Assert.False(prog.Completed);
        Assert.Empty(qm.IncrementUseProgress(p, "I9999"));

        var results = qm.IncrementUseProgress(p, "I0020");
        Assert.True(results[0].Completed);
        Assert.True(prog.Completed);
    }

    [Fact]
    public void IncrementExploreProgress_CompletesOnZoneEnter()
    {
        var def = new QuestDefinition { Id = "Q0033", Type = "explore", TargetZoneId = "zone_forest", Target = 1 };
        var qm = CreateManager(def);

        var p = Player();
        qm.TakeQuest(p, def);
        var prog = p.ActiveQuests.Single();

        Assert.Empty(qm.IncrementExploreProgress(p, "zone_main"));

        var results = qm.IncrementExploreProgress(p, "zone_forest");
        Assert.Single(results);
        Assert.True(prog.Completed);
    }

    [Fact]
    public void TryAutoGrant_GrantsMatchingQuestOnce()
    {
        var def = new QuestDefinition { Id = "Q0040", Type = "kill", Target = 1, AutoGrant = true, TargetZoneId = "zone_main" };
        var qm = CreateManager(def);

        var p = Player();
        Assert.Empty(qm.TryAutoGrant(p, "zone_forest"));

        var granted = qm.TryAutoGrant(p, "zone_main");
        Assert.Single(granted);
        Assert.Equal("Q0040", granted[0].Id);
        Assert.Single(p.ActiveQuests);

        Assert.Empty(qm.TryAutoGrant(p, "zone_main"));
    }

    [Fact]
    public void TryAutoGrant_RespectsPrerequisite()
    {
        var second = new QuestDefinition { Id = "Q0042", Type = "kill", Target = 1, AutoGrant = true, PrerequisiteQuestId = "Q0041" };
        var qm = CreateManager(second);

        var p = Player();
        Assert.Empty(qm.TryAutoGrant(p, "zone_main"));

        p.CompletedQuestIds.Add("Q0041");
        Assert.Single(qm.TryAutoGrant(p, "zone_main"));
    }

    [Fact]
    public void IsQuestItem_OnlyFlaggedTemplatesAndLoot()
    {
        var qm = CreateManager();
        qm.SetQuestItemIds(new[] { "I0090" });
        qm.SetQuestItemNames(new[] { "Ключ от подвала" });

        // Помеченный шаблон — нельзя продавать (по TemplateId и по Id)
        Assert.True(qm.IsQuestItem(new Item { TemplateId = "I0090" }));
        Assert.True(qm.IsQuestItem(new Item { Id = "I0090" }));
        Assert.True(qm.IsQuestItem("I0090"));

        // Квестовый лут без шаблона — по названию
        Assert.True(qm.IsQuestItem(new Item { Name = "Ключ от подвала", Type = "trophy" }));

        // Обычные предметы и собираемые (ягоды) — продаются всегда
        Assert.False(qm.IsQuestItem(new Item { TemplateId = "I0100" }));
        Assert.False(qm.IsQuestItem(new Item { Name = "Ягоды", Type = "collectible" }));
        Assert.False(qm.IsQuestItem(new Item { Name = "Ключ от подвала", TemplateId = "I0091" }));
        Assert.False(qm.IsQuestItem(""));
    }
}
