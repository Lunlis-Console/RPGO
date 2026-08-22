using LostAndDivine.Shared.Data;

namespace LostAndDivine.Server.Repositories;

internal static class MerchantRepository
{
    internal static List<(string ItemId, int Stock)> LoadStock(string npcId)
    {
        lock (Db.ContentLock)
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
        lock (Db.ContentLock)
        {
            using var connection = Db.OpenContent();
            using var transaction = connection.BeginTransaction();
            ContentStore.DeleteMerchantStock(connection, transaction, npcId);
            foreach (var (itemId, stock) in items)
            {
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                ContentStore.InsertMerchantStock(connection, transaction, npcId, itemId, stock);
            }
            transaction.Commit();
        }
    }
}
