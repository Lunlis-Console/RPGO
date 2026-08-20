namespace LostAndDivine.Server.Repositories;

internal static class MerchantRepository
{
    internal static List<(string ItemId, int Stock)> LoadStock(string npcId)
    {
        lock (Db.Lock)
        {
            var result = new List<(string, int)>();
            using var connection = Db.OpenContent();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT item_id, stock FROM merchant_stock WHERE npc_id = $npc ORDER BY item_id";
            cmd.Parameters.AddWithValue("$npc", npcId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add((reader.GetString(0), reader.GetInt32(1)));
            return result;
        }
    }

    internal static void SaveStock(string npcId, IEnumerable<(string ItemId, int Stock)> items)
    {
        lock (Db.Lock)
        {
            using var connection = Db.OpenContent();
            using var transaction = connection.BeginTransaction();
            using (var del = connection.CreateCommand())
            {
                del.CommandText = "DELETE FROM merchant_stock WHERE npc_id = $npc";
                del.Parameters.AddWithValue("$npc", npcId);
                del.ExecuteNonQuery();
            }
            foreach (var (itemId, stock) in items)
            {
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO merchant_stock (npc_id, item_id, stock) VALUES ($npc, $item, $stock)";
                cmd.Parameters.AddWithValue("$npc", npcId);
                cmd.Parameters.AddWithValue("$item", itemId);
                cmd.Parameters.AddWithValue("$stock", Math.Max(1, stock));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }
}
