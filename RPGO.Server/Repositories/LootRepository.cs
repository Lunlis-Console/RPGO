using RPGGame.Shared.Models;

namespace RPGGame.Server.Repositories;

internal static class LootRepository
{
    internal static List<LootEntry> LoadAll()
    {
        lock (Db.Lock)
        {
            var result = new List<LootEntry>();
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, monster_id, name, description, value, drop_chance FROM loot_tables ORDER BY monster_id, id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new LootEntry
                {
                    Id = reader.GetInt32(0),
                    MonsterId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Description = reader.GetString(3),
                    Value = reader.GetInt32(4),
                    DropChance = reader.GetInt32(5),
                });
            }
            return result;
        }
    }
}
