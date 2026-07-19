using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace RPGGame.Shared.Migrations;

public static class DbMigrationRunner
{
    public static void RunMigrations(string connectionString)
    {
        bool hasExistingTables = HasExistingTables(connectionString);
        bool hasVersionInfo = hasExistingTables && HasVersionInfo(connectionString);

        if (hasExistingTables && !hasVersionInfo)
        {
            Console.WriteLine("[Migrations] Existing DB without migration history — dropping all tables...");
            DropAllTables(connectionString);
            Console.WriteLine("[Migrations] All tables dropped. Applying all migrations...");
        }
        else if (hasExistingTables)
        {
            Console.WriteLine("[Migrations] Checking for pending migrations...");
            RepairVersionInfo(connectionString);
        }
        else
        {
            Console.WriteLine("[Migrations] Fresh database — applying all migrations...");
        }

        RunMigrateUp(connectionString);
        Console.WriteLine("[Migrations] Done.");
    }

    private static void RunMigrateUp(string connectionString)
    {
        using var serviceProvider = CreateServices(connectionString).BuildServiceProvider();
        var runner = serviceProvider.GetRequiredService<IMigrationRunner>();

        try
        {
            runner.MigrateUp();
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("VersionInfo"))
        {
            Console.WriteLine("[Migrations] VersionInfo conflict — cleaning and retrying...");
            CleanStaleVersionInfo(connectionString);
            using var retryProvider = CreateServices(connectionString).BuildServiceProvider();
            var retryRunner = retryProvider.GetRequiredService<IMigrationRunner>();
            retryRunner.MigrateUp();
        }
    }

    private static void RepairVersionInfo(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='VersionInfo'";
        if (check.ExecuteScalar() == null) return;

        var allVersions = new List<long>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT DISTINCT Version FROM VersionInfo ORDER BY Version";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                allVersions.Add(reader.GetInt64(0));
        }

        if (allVersions.Count <= 1) return;

        Console.WriteLine($"[Migrations] Found {allVersions.Count} VersionInfo rows — removing duplicates...");
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM VersionInfo WHERE Rowid NOT IN (SELECT MIN(Rowid) FROM VersionInfo GROUP BY Version)";
            del.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void CleanStaleVersionInfo(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM VersionInfo";
        cmd.ExecuteNonQuery();
    }

    private static bool HasExistingTables(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='accounts'";
        return cmd.ExecuteScalar() != null;
    }

    private static bool HasVersionInfo(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='VersionInfo'";
        return cmd.ExecuteScalar() != null;
    }

    private static void DropAllTables(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name != 'sqlite_sequence'";
        var tables = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }
        using var tx = conn.BeginTransaction();
        foreach (var table in tables)
        {
            using var drop = conn.CreateCommand();
            drop.CommandText = $"DROP TABLE [{table}]";
            drop.ExecuteNonQuery();
        }
        tx.Commit();
        Console.WriteLine($"[Migrations] Dropped {tables.Count} table(s).");
    }

    private static IServiceCollection CreateServices(string connectionString)
    {
        return new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(DbMigrationRunner).Assembly).For.Migrations())
            .AddLogging(lb => lb
                .AddFluentMigratorConsole());
    }
}
