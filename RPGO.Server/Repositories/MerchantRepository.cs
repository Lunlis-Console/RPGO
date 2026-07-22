namespace RPGGame.Server.Repositories;

internal static class MerchantRepository
{
    internal static List<string> LoadStock(string npcId)
    {
        lock (Db.Lock)
        {
            var result = new List<string>();
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT item_id FROM merchant_stock WHERE npc_id = $npc ORDER BY item_id";
            cmd.Parameters.AddWithValue("$npc", npcId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetString(0));
            return result;
        }
    }

    internal static void SaveStock(string npcId, IEnumerable<string> itemIds)
    {
        lock (Db.Lock)
        {
            using var connection = Db.Open();
            using var transaction = connection.BeginTransaction();
            using (var del = connection.CreateCommand())
            {
                del.CommandText = "DELETE FROM merchant_stock WHERE npc_id = $npc";
                del.Parameters.AddWithValue("$npc", npcId);
                del.ExecuteNonQuery();
            }
            foreach (var itemId in itemIds)
            {
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ($npc, $item)";
                cmd.Parameters.AddWithValue("$npc", npcId);
                cmd.Parameters.AddWithValue("$item", itemId);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }
}
