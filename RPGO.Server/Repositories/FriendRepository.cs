namespace RPGGame.Server.Repositories;

internal static class FriendRepository
{
    public const int MaxFriends = 50;

    internal static void Add(string ownerName, string friendName)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO friends (owner_name, friend_name, created_at)
                VALUES ($owner, $friend, $now)";
            cmd.Parameters.AddWithValue("$owner", ownerName);
            cmd.Parameters.AddWithValue("$friend", friendName);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }

    internal static void Remove(string ownerName, string friendName)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM friends WHERE owner_name = $owner AND friend_name = $friend";
            cmd.Parameters.AddWithValue("$owner", ownerName);
            cmd.Parameters.AddWithValue("$friend", friendName);
            cmd.ExecuteNonQuery();
        }
    }

    internal static List<string> GetNames(string ownerName)
    {
        var names = new List<string>();
        using var conn = Db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT friend_name FROM friends WHERE owner_name = $owner ORDER BY friend_name";
        cmd.Parameters.AddWithValue("$owner", ownerName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    internal static bool Exists(string ownerName, string friendName)
    {
        using var conn = Db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT 1 FROM friends WHERE owner_name = $owner AND friend_name = $friend LIMIT 1";
        cmd.Parameters.AddWithValue("$owner", ownerName);
        cmd.Parameters.AddWithValue("$friend", friendName);
        return cmd.ExecuteScalar() != null;
    }

    internal static List<string> GetReverseNames(string ownerName)
    {
        var names = new List<string>();
        using var conn = Db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT owner_name FROM friends WHERE friend_name = $owner";
        cmd.Parameters.AddWithValue("$owner", ownerName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    internal static bool PlayerNameExists(string playerName)
    {
        using var conn = Db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM accounts WHERE player_name = $name LIMIT 1";
        cmd.Parameters.AddWithValue("$name", playerName);
        return cmd.ExecuteScalar() != null;
    }
}
