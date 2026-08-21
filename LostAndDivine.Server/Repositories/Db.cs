using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Server.Repositories;

internal static class Db
{
    internal static readonly string ConnectionString;
    internal static readonly string ContentConnectionString;
    internal static readonly string RuntimePath;
    internal static readonly string ContentPath;
    internal static readonly object GameLock = new();
    internal static readonly object ContentLock = new();

    static Db()
    {
        RuntimePath = ResolveDbPath("game.db");
        ConnectionString = $"Data Source={RuntimePath}";
        ContentPath = ResolveDbPath("content.db");
        ContentConnectionString = $"Data Source={ContentPath}";
    }

    internal static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    internal static SqliteConnection OpenContent()
    {
        var conn = new SqliteConnection(ContentConnectionString);
        conn.Open();
        return conn;
    }

    private static string ResolveDbPath(string fileName)
    {
        var candidates = new List<string>();
        string? baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, fileName));
        for (int i = 0; i < 6; i++)
        {
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;
            candidates.Add(Path.Combine(baseDir, fileName));
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
            if (File.Exists(Path.Combine(root, "LostAndDivine.Server.csproj")))
                return Path.Combine(root, fileName);
        }
        return fileName;
    }
}
