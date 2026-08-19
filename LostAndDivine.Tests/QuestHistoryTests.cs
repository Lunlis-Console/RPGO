using LostAndDivine.Server.Repositories;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Tests;

public class QuestHistoryTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"rpg_quests_{Guid.NewGuid():N}.db");

    private static void Cleanup(string db)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(db);
    }

    private static SqliteConnection Open(string db)
    {
        var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE player_completed_quests (
                player_name TEXT NOT NULL,
                quest_id TEXT NOT NULL,
                completed_at TEXT,
                PRIMARY KEY (player_name, quest_id)
            )";
        cmd.ExecuteNonQuery();
        return conn;
    }

    [Fact]
    public void SaveCompleted_KeepsFirstCompletionTime_OnReSave()
    {
        string db = TempDb();
        try
        {
            using (var conn = Open(db))
            {
                QuestRepository.SaveCompleted(conn, "Hero", new List<string> { "Q0001", "Q0002" });
                var first = QuestRepository.LoadCompleted(conn, "Hero").ToDictionary(c => c.QuestId, c => c.CompletedAt);
                Assert.True(first["Q0001"].Length > 0);

                // Повторное сохранение того же списка не должно менять время первого выполнения
                QuestRepository.SaveCompleted(conn, "Hero", new List<string> { "Q0001", "Q0002" });
                var second = QuestRepository.LoadCompleted(conn, "Hero").ToDictionary(c => c.QuestId, c => c.CompletedAt);
                Assert.Equal(first["Q0001"], second["Q0001"]);
                Assert.Equal(first["Q0002"], second["Q0002"]);
            }
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void SaveCompleted_RemovesDroppedQuests_AndAddsNewOnes()
    {
        string db = TempDb();
        try
        {
            using (var conn = Open(db))
            {
                QuestRepository.SaveCompleted(conn, "Hero", new List<string> { "Q0001", "Q0002", "Q0003" });
                QuestRepository.SaveCompleted(conn, "Hero", new List<string> { "Q0002", "Q0004" });

                var ids = QuestRepository.LoadCompleted(conn, "Hero").Select(c => c.QuestId).ToList();
                Assert.Contains("Q0002", ids);
                Assert.Contains("Q0004", ids);
                Assert.DoesNotContain("Q0001", ids);
                Assert.DoesNotContain("Q0003", ids);
            }
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void LoadCompleted_ReturnsInInsertionOrder()
    {
        string db = TempDb();
        try
        {
            using (var conn = Open(db))
            {
                QuestRepository.SaveCompleted(conn, "Hero", new List<string> { "Q0001", "Q0002", "Q0003" });

                var ids = QuestRepository.LoadCompleted(conn, "Hero").Select(c => c.QuestId).ToList();
                Assert.Equal(new[] { "Q0001", "Q0002", "Q0003" }, ids);
            }
        }
        finally { Cleanup(db); }
    }
}