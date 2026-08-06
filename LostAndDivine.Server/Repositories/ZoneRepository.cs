using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Repositories;

internal static class ZoneRepository
{
    internal static List<Zone> LoadAll()
    {
        lock (Db.Lock)
        {
            using var conn = Db.OpenContent();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, width, height, spawn_x, spawn_y, pvp_enabled FROM zones";
            var zones = new List<Zone>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                zones.Add(new Zone
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Width = reader.GetInt32(2),
                    Height = reader.GetInt32(3),
                    SpawnX = reader.GetInt32(4),
                    SpawnY = reader.GetInt32(5),
                    PvpEnabled = reader.GetInt32(6) != 0
                });
            }
            return zones;
        }
    }

    internal static List<WorldPortal> LoadPortals()
    {
        lock (Db.Lock)
        {
            using var conn = Db.OpenContent();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, from_zone, from_x, from_y, to_zone, to_x, to_y FROM world_portals";
            var portals = new List<WorldPortal>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                portals.Add(new WorldPortal
                {
                    Id = reader.GetString(0),
                    FromZone = reader.GetString(1),
                    FromX = reader.GetInt32(2),
                    FromY = reader.GetInt32(3),
                    ToZone = reader.GetString(4),
                    ToX = reader.GetInt32(5),
                    ToY = reader.GetInt32(6)
                });
            }
            return portals;
        }
    }
}
