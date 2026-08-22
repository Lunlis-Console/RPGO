using LostAndDivine.Editor;
using LostAndDivine.Shared.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LostAndDivine.Tests;

/// <summary>
/// Тесты слоя доступа к контенту редактора (P2-10/P1-8) и единого DAL (P1-7):
/// неразрушающий save (DeleteMissingRows), целевое обновление диалога (UpdateNpcData)
/// и единый upsert NPC, не затирающий колонки, которыми управляет другая сторона.
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

    /// <summary>
    /// P1-7: единый upsert NPC не затирает колонки, которыми управляет другая сторона.
    /// Редактор пишет location+data, но не должен занулять серверные x,y (и наоборот).
    /// </summary>
    [Fact]
    public void UpsertNpc_PreservesColumnsManagedByOtherSide()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE npcs(id TEXT PRIMARY KEY, name TEXT, type TEXT, x INT, y INT, location TEXT, data TEXT)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO npcs(id,name,type,x,y,location,data) VALUES('N1','N1','npc',5,7,'ZoneA','{}')";
            cmd.ExecuteNonQuery();
        }

        // Редактор перед сохранением читает существующие x,y и передаёт их в upsert
        // (именно так NpcsTabView сохраняет серверные колонки при правке location).
        int exX, exY;
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT x, y FROM npcs WHERE id='N1'";
            using var rr = read.ExecuteReader();
            rr.Read();
            exX = rr.GetInt32(0);
            exY = rr.GetInt32(1);
        }

        ContentStore.UpsertNpc(conn, null, "N1", "N1-renamed", "npc", exX, exY, "ZoneB", "{\"dialog\":1}");

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT x, y, location, name, data FROM npcs WHERE id='N1'";
        using var r = cmd2.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(5, r.GetInt32(0));            // x сохранён (не занулен в 0)
        Assert.Equal(7, r.GetInt32(1));            // y сохранён
        Assert.Equal("ZoneB", r.GetString(2));     // location обновлён редактором
        Assert.Equal("N1-renamed", r.GetString(3));
        Assert.Equal("{\"dialog\":1}", r.GetString(4));
    }
}
