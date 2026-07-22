namespace RPGGame.Server.Repositories;

internal static class WorldConfigRepository
{
    internal static int GetInt(string key, int defaultValue = 0)
    {
        lock (Db.Lock)
        {
            try
            {
                using var connection = Db.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM world_config WHERE key = $k";
                cmd.Parameters.AddWithValue("$k", key);
                var v = cmd.ExecuteScalar();
                return v == null ? defaultValue : (int)(long)v;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
