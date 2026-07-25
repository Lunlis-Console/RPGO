using Microsoft.Data.Sqlite;

namespace RPGGame.Server.Repositories;

public static class MailRepository
{
    private const int MaxInbox = 50;
    private const int MaxOutbox = 30;

    public static int Send(string sender, string recipient, string subject, string body,
        int goldAmount, string itemId, string itemName, string itemType, int itemQuantity)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM mail WHERE recipient_name = $r AND is_deleted_recipient = 0";
            countCmd.Parameters.AddWithValue("$r", recipient);
            int recipientCount = Convert.ToInt32(countCmd.ExecuteScalar());

            using var outCmd = conn.CreateCommand();
            outCmd.CommandText = "SELECT COUNT(*) FROM mail WHERE sender_name = $s AND is_deleted_sender = 0";
            outCmd.Parameters.AddWithValue("$s", sender);
            int senderCount = Convert.ToInt32(outCmd.ExecuteScalar());

            if (recipientCount >= MaxInbox)
                return -1; // recipient inbox full
            if (senderCount >= MaxOutbox)
                return -2; // sender outbox full

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO mail (sender_name, recipient_name, subject, body, gold_amount,
                item_id, item_name, item_type, item_quantity, sent_at)
                VALUES ($sender, $recipient, $subject, $body, $gold, $itemId, $itemName, $itemType, $itemQty, $sentAt)";
            cmd.Parameters.AddWithValue("$sender", sender);
            cmd.Parameters.AddWithValue("$recipient", recipient);
            cmd.Parameters.AddWithValue("$subject", subject);
            cmd.Parameters.AddWithValue("$body", body);
            cmd.Parameters.AddWithValue("$gold", goldAmount);
            cmd.Parameters.AddWithValue("$itemId", itemId);
            cmd.Parameters.AddWithValue("$itemName", itemName);
            cmd.Parameters.AddWithValue("$itemType", itemType);
            cmd.Parameters.AddWithValue("$itemQty", itemQuantity);
            cmd.Parameters.AddWithValue("$sentAt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();

            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid()";
            return Convert.ToInt32(idCmd.ExecuteScalar());
        }
    }

    public static List<MailData> GetInbox(string playerName)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sender_name, recipient_name, subject, body, gold_amount,
            item_id, item_name, item_type, item_quantity, sent_at, read_at, taken_at
            FROM mail WHERE recipient_name = $name AND is_deleted_recipient = 0 ORDER BY id DESC";
        cmd.Parameters.AddWithValue("$name", playerName);
        return ReadMailList(cmd);
    }

    public static List<MailData> GetOutbox(string playerName)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sender_name, recipient_name, subject, body, gold_amount,
            item_id, item_name, item_type, item_quantity, sent_at, read_at, taken_at
            FROM mail WHERE sender_name = $name AND is_deleted_sender = 0 ORDER BY id DESC";
        cmd.Parameters.AddWithValue("$name", playerName);
        return ReadMailList(cmd);
    }

    public static MailData? GetById(int id)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sender_name, recipient_name, subject, body, gold_amount,
            item_id, item_name, item_type, item_quantity, sent_at, read_at, taken_at
            FROM mail WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadMailList(cmd).FirstOrDefault();
    }

    public static void MarkRead(int id)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE mail SET read_at = $now WHERE id = $id AND (read_at = '' OR read_at IS NULL)";
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public static void TakeAttachment(int id)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE mail SET taken_at = $now WHERE id = $id";
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public static void DeleteMail(int id, string playerName)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE mail SET
                is_deleted_sender = CASE WHEN sender_name = $name THEN 1 ELSE is_deleted_sender END,
                is_deleted_recipient = CASE WHEN recipient_name = $name THEN 1 ELSE is_deleted_recipient END
                WHERE id = $id";
            cmd.Parameters.AddWithValue("$name", playerName);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public static int CountUnread(string playerName)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM mail WHERE recipient_name = $name
            AND is_deleted_recipient = 0 AND (read_at = '' OR read_at IS NULL)";
        cmd.Parameters.AddWithValue("$name", playerName);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static bool PlayerExists(string playerName)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM accounts WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static List<MailData> ReadMailList(SqliteCommand cmd)
    {
        var result = new List<MailData>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new MailData
            {
                Id = reader.GetInt32(0),
                SenderName = reader.GetString(1),
                RecipientName = reader.GetString(2),
                Subject = reader.GetString(3),
                Body = reader.GetString(4),
                GoldAmount = reader.GetInt32(5),
                ItemId = reader.GetString(6),
                ItemName = reader.GetString(7),
                ItemType = reader.GetString(8),
                ItemQuantity = reader.GetInt32(9),
                SentAt = reader.GetString(10),
                ReadAt = reader.GetString(11),
                TakenAt = reader.GetString(12)
            });
        }
        return result;
    }
}

public class MailData
{
    public int Id { get; set; }
    public string SenderName { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public int GoldAmount { get; set; }
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string ItemType { get; set; } = "";
    public int ItemQuantity { get; set; }
    public string SentAt { get; set; } = "";
    public string ReadAt { get; set; } = "";
    public string TakenAt { get; set; } = "";
}
