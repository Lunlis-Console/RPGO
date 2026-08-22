using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Data;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Repositories;

internal static class NpcRepository
{
    internal static void SaveSingle(string id, string name, string type, int x, int y, string? data, int wanderRadius = 0)
    {
        using var conn = Db.OpenContent();
        Upsert(conn, null, id, name, type, x, y, data, wanderRadius);
    }

    internal static List<NpcRecord> LoadAll()
    {
        var list = new List<NpcRecord>();
        using var conn = Db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, x, y, data, wander_radius FROM npcs ORDER BY id";
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
                WanderRadius = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
            });
        }
        return list;
    }

    internal static void SaveAll(List<NpcRecord> npcs)
    {
        using var conn = Db.OpenContent();
        using var transaction = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM npcs";
            del.ExecuteNonQuery();
        }
        foreach (var n in npcs)
        {
            Upsert(conn, transaction, n.Id, n.Name, n.Type, n.X, n.Y, n.Data, n.WanderRadius);
        }
        transaction.Commit();
    }

    // Единый upsert через ContentStore; сохраняет location, которую manage-ит редактор
    // (P1-7: устранение дрейфа npcs x,y vs location между Server и Editor).
    private static void Upsert(SqliteConnection connection, SqliteTransaction? tx, string id, string name, string type, int x, int y, string? data, int wanderRadius)
    {
        string? existingLocation = null;
        using (var r = connection.CreateCommand())
        {
            r.Transaction = tx;
            r.CommandText = "SELECT location FROM npcs WHERE id = $id";
            r.Parameters.AddWithValue("$id", id);
            var v = r.ExecuteScalar();
            if (v != null && v != DBNull.Value) existingLocation = v.ToString();
        }
        ContentStore.UpsertNpc(connection, tx, id, name, type, x, y, existingLocation, data, wanderRadius);
    }
}
