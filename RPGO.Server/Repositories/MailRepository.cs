using Microsoft.Data.Sqlite;

namespace RPGGame.Server.Repositories;

public static class MailRepository
{
    private const int MaxInbox = 50;
    private const int MaxOutbox = 30;

    public static int Send(string sender, string recipient, string subject, string body,
        int goldAmount, List<MailAttachment> attachments)
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
            cmd.CommandText = @"INSERT INTO mail (sender_name, recipient_name, subject, body, gold_amount, sent_at)
                VALUES ($sender, $recipient, $subject, $body, $gold, $sentAt)";
            cmd.Parameters.AddWithValue("$sender", sender);
            cmd.Parameters.AddWithValue("$recipient", recipient);
            cmd.Parameters.AddWithValue("$subject", subject);
            cmd.Parameters.AddWithValue("$body", body);
            cmd.Parameters.AddWithValue("$gold", goldAmount);
            cmd.Parameters.AddWithValue("$sentAt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();

            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid()";
            int mailId = Convert.ToInt32(idCmd.ExecuteScalar());

            foreach (var att in attachments)
            {
                if (att == null || string.IsNullOrEmpty(att.TemplateId) || att.Quantity <= 0) continue;
                using var aCmd = conn.CreateCommand();
                aCmd.CommandText = @"INSERT INTO mail_attachments (mail_id, template_id, name, type, quantity, weapon_subtype, heal_amount, restore_mana)
                    VALUES ($mailId, $tid, $name, $type, $qty, $weaponSubtype, $healAmount, $restoreMana)";
                aCmd.Parameters.AddWithValue("$mailId", mailId);
                aCmd.Parameters.AddWithValue("$tid", att.TemplateId);
                aCmd.Parameters.AddWithValue("$name", att.Name);
                aCmd.Parameters.AddWithValue("$type", att.Type);
                aCmd.Parameters.AddWithValue("$qty", att.Quantity);
                aCmd.Parameters.AddWithValue("$weaponSubtype", att.WeaponSubtype);
                aCmd.Parameters.AddWithValue("$healAmount", att.HealAmount);
                aCmd.Parameters.AddWithValue("$restoreMana", att.RestoreMana);
                aCmd.ExecuteNonQuery();
            }

            return mailId;
        }
    }

    public static List<MailData> GetInbox(string playerName)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sender_name, recipient_name, subject, body, gold_amount, sent_at, read_at, taken_at
            FROM mail WHERE recipient_name = $name AND is_deleted_recipient = 0 ORDER BY id DESC";
        cmd.Parameters.AddWithValue("$name", playerName);
        var result = ReadMailList(cmd);
        LoadAttachments(conn, result);
        return result;
    }

    public static List<MailData> GetOutbox(string playerName)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sender_name, recipient_name, subject, body, gold_amount, sent_at, read_at, taken_at
            FROM mail WHERE sender_name = $name AND is_deleted_sender = 0 ORDER BY id DESC";
        cmd.Parameters.AddWithValue("$name", playerName);
        var result = ReadMailList(cmd);
        LoadAttachments(conn, result);
        return result;
    }

    public static MailData? GetById(int id)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sender_name, recipient_name, subject, body, gold_amount, sent_at, read_at, taken_at
            FROM mail WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var result = ReadMailList(cmd);
        if (result.Count == 0) return null;
        LoadAttachments(conn, result);
        return result[0];
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
                SentAt = reader.GetString(6),
                ReadAt = reader.GetString(7),
                TakenAt = reader.GetString(8)
            });
        }
        return result;
    }

    private static void LoadAttachments(SqliteConnection conn, List<MailData> mails)
    {
        if (mails.Count == 0) return;
        var ids = mails.Select(m => m.Id).ToList();
        string inClause = string.Join(",", ids.Select((_, i) => "$p" + i));

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT mail_id, template_id, name, type, quantity, weapon_subtype, heal_amount, restore_mana FROM mail_attachments WHERE mail_id IN ({inClause}) ORDER BY id";
        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue("$p" + i, ids[i]);
        using var reader = cmd.ExecuteReader();
        var map = mails.ToDictionary(m => m.Id);
        while (reader.Read())
        {
            int mailId = reader.GetInt32(0);
            if (map.TryGetValue(mailId, out var mail))
            {
                mail.Attachments.Add(new MailAttachment
                {
                    TemplateId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Type = reader.GetString(3),
                    Quantity = reader.GetInt32(4),
                    WeaponSubtype = reader.GetString(5),
                    HealAmount = reader.GetInt32(6),
                    RestoreMana = reader.GetInt32(7)
                });
            }
        }
    }
}

public class MailAttachment
{
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string WeaponSubtype { get; set; } = "";
    public int HealAmount { get; set; }
    public int RestoreMana { get; set; }
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
    public List<MailAttachment> Attachments { get; set; } = new();
    public string SentAt { get; set; } = "";
    public string ReadAt { get; set; } = "";
    public string TakenAt { get; set; } = "";
}
