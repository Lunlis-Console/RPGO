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
        ConnectionString = $"Data Source={RuntimePath};Pooling=True;Cache=Shared";
        ContentPath = ResolveDbPath("content.db");
        ContentConnectionString = $"Data Source={ContentPath};Pooling=True;Cache=Shared";
    }

    internal static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        ApplyPragmas(conn);
        return conn;
    }

    internal static SqliteConnection OpenContent()
    {
        var conn = new SqliteConnection(ContentConnectionString);
        conn.Open();
        ApplyPragmas(conn);
        return conn;
    }

    private static void ApplyPragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        // WAL для 2000 CCU: писатели не блокируют читателей, busy_timeout спасает от SQLITE_BUSY
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA cache_size=-64000;";
        try { cmd.ExecuteNonQuery(); } catch { /* pragma not critical on older sqlite */ }
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
