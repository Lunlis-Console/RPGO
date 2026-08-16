using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Repositories;

internal static class LootRepository
{
    internal static List<MonsterDrop> LoadAll()
    {
        lock (Db.Lock)
        {
            var result = new List<MonsterDrop>();
            using var connection = Db.OpenContent();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT monster_id, item_id, drop_chance FROM monster_drops ORDER BY monster_id, item_id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new MonsterDrop
                {
                    MonsterId = reader.GetString(0),
                    ItemId = reader.GetString(1),
                    DropChance = reader.GetInt32(2),
                });
            }
            return result;
        }
    }
}
