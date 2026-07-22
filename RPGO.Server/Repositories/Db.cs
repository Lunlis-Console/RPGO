using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace RPGGame.Server.Repositories;

internal static class Db
{
    internal static readonly string ConnectionString;
    internal static readonly object Lock = new();

    static Db()
    {
        ConnectionString = $"Data Source={ResolveDbPath()}";
    }

    internal static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    private static string ResolveDbPath()
    {
        var candidates = new List<string>();
        string? baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "game.db"));
        for (int i = 0; i < 6; i++)
        {
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;
            candidates.Add(Path.Combine(baseDir, "game.db"));
        }
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        string? root = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            root = Path.GetDirectoryName(root);
            if (root == null) break;
            if (File.Exists(Path.Combine(root, "RPGO.Server.csproj")))
                return Path.Combine(root, "game.db");
        }
        return "game.db";
    }
}
