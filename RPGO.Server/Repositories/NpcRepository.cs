using Microsoft.Data.Sqlite;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Repositories;

internal static class NpcRepository
{
    internal static void SaveSingle(string id, string name, string type, int x, int y, string? data)
    {
        using var conn = Db.Open();
        Upsert(conn, id, name, type, x, y, data);
    }

    internal static List<NpcRecord> LoadAll()
    {
        var list = new List<NpcRecord>();
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, x, y, data FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new NpcRecord
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Type = reader.GetString(2),
                X = reader.GetInt32(3),
                Y = reader.GetInt32(4),
                Data = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return list;
    }

    internal static void SaveAll(List<NpcRecord> npcs)
    {
        using var conn = Db.Open();
        using var transaction = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM npcs";
            del.ExecuteNonQuery();
        }
        foreach (var n in npcs)
        {
            Upsert(conn, n.Id, n.Name, n.Type, n.X, n.Y, n.Data);
        }
        transaction.Commit();
    }

    private static void Upsert(SqliteConnection connection, string id, string name, string type, int x, int y, string? data)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO npcs (id, name, type, x, y, data) VALUES ($id,$n,$t,$x,$y,$d)
            ON CONFLICT(id) DO UPDATE SET name=$n, type=$t, x=$x, y=$y, data=$d";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$y", y);
        cmd.Parameters.AddWithValue("$d", (object?)data ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
