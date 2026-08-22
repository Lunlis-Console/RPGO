using LostAndDivine.Editor;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LostAndDivine.Tests;

/// <summary>
/// Тесты слоя доступа к контенту редактора (P2-10/P1-8): неразрушающий save
/// (DeleteMissingRows) и целевое обновление диалога (UpdateNpcData).
/// </summary>
public class DbTests
{
    [Fact]
    public void DeleteMissingRows_RemovesOnlyAbsentRows_KeepsPresent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE t(id TEXT PRIMARY KEY, v TEXT)";
            cmd.ExecuteNonQuery();
            foreach (var (id, v) in new[] { ("a", "1"), ("b", "2"), ("c", "3") })
            {
                cmd.CommandText = "INSERT INTO t(id, v) VALUES ($id, $v)";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$v", v);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
            }
        }

        using (var tx = conn.BeginTransaction())
        {
            // 'b' отсутствует в списке — должен быть удалён; 'a' и 'c' сохранены.
            Db.DeleteMissingRows(conn, tx, "t", "id", new[] { "a", "c" });
            tx.Commit();
        }

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM t";
        Assert.Equal(2L, Convert.ToInt64(check.ExecuteScalar()));
        check.CommandText = "SELECT COUNT(*) FROM t WHERE id='b'";
        Assert.Equal(0L, Convert.ToInt64(check.ExecuteScalar()));
        check.CommandText = "SELECT COUNT(*) FROM t WHERE id IN ('a','c')";
        Assert.Equal(2L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public void DeleteMissingRows_EmptyIds_DeletesAll()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE t(id TEXT PRIMARY KEY, v TEXT)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO t(id, v) VALUES ('a','1')";
            cmd.ExecuteNonQuery();
        }

        using (var tx = conn.BeginTransaction())
        {
            Db.DeleteMissingRows(conn, tx, "t", "id", new List<string>());
            tx.Commit();
        }

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM t";
        Assert.Equal(0L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public void UpdateNpcData_UpdatesExistingRow_AndReturnsAffectedCount()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lad_dbtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dbPath = Path.Combine(dir, "editor.db");
            var contentPath = Path.Combine(dir, "content.editor.db");
            File.WriteAllText(dbPath, "");
            using (var c = new SqliteConnection($"Data Source={contentPath}"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE npcs(id TEXT PRIMARY KEY, data TEXT)";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "INSERT INTO npcs(id, data) VALUES ('N1','old')";
                cmd.ExecuteNonQuery();
            }

            var db = new Db(dbPath);
            Assert.Equal(1, db.UpdateNpcData("N1", "new"));

            using var c2 = new SqliteConnection($"Data Source={contentPath}");
            c2.Open();
            using var cmd2 = c2.CreateCommand();
            cmd2.CommandText = "SELECT data FROM npcs WHERE id='N1'";
            Assert.Equal("new", cmd2.ExecuteScalar());

            // несуществующий NPC — затронутых строк 0
            Assert.Equal(0, db.UpdateNpcData("NOPE", "x"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
