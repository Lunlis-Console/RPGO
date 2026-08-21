using LostAndDivine.Server;
using LostAndDivine.Shared.Migrations;
using LostAndDivine.Shared.Models;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Tests;

public class QuestStageTests
{
    private static QuestManager CreateManager(params QuestDefinition[] defs)
    {
        var qm = new QuestManager(new GameWorld(100, 100));
        if (defs.Length > 0) qm.SetDefinitions(defs);
        return qm;
    }

    private static QuestDefinition StagedDef(string id, params (string Type, string Target, int Count, int Stage)[] objs)
    {
        var def = new QuestDefinition { Id = id, Type = objs[0].Type, Target = objs[0].Count };
        def.Objectives = objs.Select(o => new QuestObjective { Type = o.Type, Target = o.Target, Count = o.Count, Stage = o.Stage }).ToList();
        return def;
    }

    private static Player Player()
        => new Player { Name = "Tester", Level = 1 };

    [Fact]
    public void IsObjectiveUnlocked_FirstStageAlwaysOpen_NextOpensAfterPreviousDone()
    {
        var objectives = new List<QuestObjective>
        {
            new() { TypeEnum = QuestType.Kill, Target = "M0006", Count = 5, Stage = 0 },
            new() { TypeEnum = QuestType.Talk, Target = "N0003", Count = 1, Stage = 1 }
        };
        var currents = new List<int> { 0, 0 };

        Assert.True(QuestManager.IsObjectiveUnlocked(objectives, currents, 0));
        Assert.False(QuestManager.IsObjectiveUnlocked(objectives, currents, 1));

        currents[0] = 5;
        Assert.True(QuestManager.IsObjectiveUnlocked(objectives, currents, 1));
    }

    [Fact]
    public void IsObjectiveUnlocked_SameStageObjectivesDoNotBlockEachOther()
    {
        var objectives = new List<QuestObjective>
        {
            new() { TypeEnum = QuestType.Kill, Target = "M0006", Count = 5, Stage = 0 },
            new() { TypeEnum = QuestType.Kill, Target = "M0013", Count = 3, Stage = 0 },
            new() { TypeEnum = QuestType.Talk, Target = "N0003", Count = 1, Stage = 1 }
        };
        var currents = new List<int> { 0, 0, 0 };

        // Параллельная цель той же стадии открыта, даже если первая ещё не выполнена
        Assert.True(QuestManager.IsObjectiveUnlocked(objectives, currents, 1));
        Assert.False(QuestManager.IsObjectiveUnlocked(objectives, currents, 2));

        // Следующая стадия открывается только когда ВСЕ цели предыдущей выполнены
        currents[0] = 5;
        Assert.False(QuestManager.IsObjectiveUnlocked(objectives, currents, 2));
        currents[1] = 3;
        Assert.True(QuestManager.IsObjectiveUnlocked(objectives, currents, 2));
    }

    [Fact]
    public void IncrementProgress_LockedStageNotIncremented_ThenUnlocks()
    {
        var def = StagedDef("Q0060", ("kill", "M0006", 5, 0), ("talk", "N0003", 1, 1));
        var qm = CreateManager(def);
        var p = Player();
        qm.TakeQuest(p, def);
        var prog = p.ActiveQuests.Single();

        // Пока волки не убиты, разговор со старостой не засчитывается
        qm.IncrementTalkProgress(p, "N0003");
        Assert.Equal(0, prog.Currents[1]);

        for (int i = 0; i < 5; i++)
            qm.IncrementKillProgress(p, "M0006");
        Assert.Equal(5, prog.Currents[0]);
        Assert.False(prog.Completed);

        // После убийства всех волков цель «рассказать старосте» открывается
        qm.IncrementTalkProgress(p, "N0003");
        Assert.Equal(1, prog.Currents[1]);
        Assert.True(prog.Completed);
    }

    [Fact]
    public void CompleteQuest_StagedQuest_RejectsUntilAllStagesDone()
    {
        var def = StagedDef("Q0061", ("kill", "M0006", 5, 0), ("talk", "N0003", 1, 1));
        var qm = CreateManager(def);
        var p = Player();
        qm.TakeQuest(p, def);

        for (int i = 0; i < 5; i++)
            qm.IncrementKillProgress(p, "M0006");

        var result = qm.CompleteQuest(p, "Q0061");
        Assert.False(result.Success);
        Assert.Equal(2, result.ErrorKind);

        qm.IncrementTalkProgress(p, "N0003");
        result = qm.CompleteQuest(p, "Q0061");
        Assert.True(result.Success);
    }

    [Fact]
    public void IsReadyToComplete_TrueWhenOnlyLastStageRemains()
    {
        var objectives = new List<QuestObjective>
        {
            new() { TypeEnum = QuestType.Kill, Target = "M0006", Count = 5, Stage = 0 },
            new() { TypeEnum = QuestType.Talk, Target = "N0003", Count = 1, Stage = 1 }
        };

        Assert.False(QuestManager.IsReadyToComplete(objectives, new List<int> { 4, 0 }));
        // Все стадии, кроме последней, выполнены — квест готов к сдаче,
        // даже если последний этап («рассказать старосте») ещё не начат
        Assert.True(QuestManager.IsReadyToComplete(objectives, new List<int> { 5, 0 }));
        Assert.True(QuestManager.IsReadyToComplete(objectives, new List<int> { 5, 1 }));
    }

    [Fact]
    public void IsReadyToComplete_NoStages_RequiresAllDone()
    {
        var objectives = new List<QuestObjective>
        {
            new() { TypeEnum = QuestType.Kill, Target = "M0006", Count = 5, Stage = 0 },
            new() { TypeEnum = QuestType.Kill, Target = "M0013", Count = 3, Stage = 0 }
        };

        Assert.False(QuestManager.IsReadyToComplete(objectives, new List<int> { 5, 2 }));
        Assert.True(QuestManager.IsReadyToComplete(objectives, new List<int> { 5, 3 }));
    }

    [Fact]
    public void Migrations_1061_StagesQuestsAndRemovesQ0011()
    {
        string db = Path.Combine(Path.GetTempPath(), $"rpg_mig_{Guid.NewGuid():N}.db");
        try
        {
            DbMigrationRunner.RunMigrations($"Data Source={db}");

            using var conn = new SqliteConnection($"Data Source={db}");
            conn.Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT objectives FROM quests_def WHERE id = 'Q0007'";
            var q0007 = (string?)cmd.ExecuteScalar() ?? "";
            Assert.Contains("\"stage\":0", q0007);
            Assert.Contains("\"stage\":1", q0007);
            Assert.Contains("\"type\":\"talk\"", q0007);

            cmd.CommandText = "SELECT objectives FROM quests_def WHERE id = 'Q0004'";
            var q0004 = (string?)cmd.ExecuteScalar() ?? "";
            Assert.Contains("\"stage\":0", q0004);
            Assert.Contains("\"stage\":1", q0004);
            Assert.Contains("\"type\":\"talk\"", q0004);

            cmd.CommandText = "SELECT COUNT(*) FROM quests_def WHERE id = 'Q0011'";
            Assert.Equal(0, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(db);
        }
    }
}
