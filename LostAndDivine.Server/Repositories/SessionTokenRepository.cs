using Microsoft.Data.Sqlite;

namespace LostAndDivine.Server.Repositories;

/// <summary>
/// Persists reconnect tokens in SQLite so they survive a server restart.
/// </summary>
internal static class SessionTokenRepository
{
    internal static void Save(string token, string playerName, long expiry)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO session_tokens (token, player_name, expiry)
                VALUES ($token, $name, $exp)";
            cmd.Parameters.AddWithValue("$token", token);
            cmd.Parameters.AddWithValue("$name", playerName);
            cmd.Parameters.AddWithValue("$exp", expiry);
            cmd.ExecuteNonQuery();
        }
    }

    internal static (string PlayerName, long Expiry)? Find(string token)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT player_name, expiry FROM session_tokens WHERE token = $token";
            cmd.Parameters.AddWithValue("$token", token);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return (reader.GetString(0), reader.GetInt64(1));
        }
    }

    internal static void Delete(string token)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM session_tokens WHERE token = $token";
            cmd.Parameters.AddWithValue("$token", token);
            cmd.ExecuteNonQuery();
        }
    }

    internal static void DeleteExpired(long now)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM session_tokens WHERE expiry < $now";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Удаляет все токены игрока (используется при выдаче нового токена).</summary>
    internal static void DeleteForPlayer(string playerName)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM session_tokens WHERE player_name = $name";
            cmd.Parameters.AddWithValue("$name", playerName);
            cmd.ExecuteNonQuery();
        }
    }
}
