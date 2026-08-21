using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LostAndDivine.Shared.Migrations;

public static class DbMigrationRunner
{
    /// <summary>
    /// Применяет миграции к БД.
    /// </summary>
    /// <param name="connectionString">Строка подключения SQLite.</param>
    /// <param name="allowDestructiveReset">
    /// Если <c>true</c> — при обнаружении БД с таблицами, но без истории миграций
    /// (VersionInfo), таблицы будут удалены и пересозданы. По умолчанию <c>false</c>:
    /// в этом случае старт завершается исключением <see cref="MigrationException"/>,
    /// чтобы избежать непреднамеренного уничтожения данных (P0-1).
    /// </param>
    public static void RunMigrations(string connectionString, bool allowDestructiveReset = false)
    {
        string? dbPath = ExtractDataSource(connectionString);

        bool hasExistingTables = HasExistingTables(connectionString);
        bool hasVersionInfo = hasExistingTables && HasVersionInfo(connectionString);

        if (hasExistingTables && !hasVersionInfo)
        {
            // P0-1: ранее здесь молча вызывался DropAllTables — катастрофический вайп
            // при любой порче tracking-таблицы. Теперь сброс возможен только явно.
            // Бэкап сохраняем в любом случае — оператору нужен восстановимый снимок.
            BackupBeforeMigration(dbPath);
            if (allowDestructiveReset)
            {
                Console.WriteLine("[Migrations] WARNING: явный сброс БД (allowDestructiveReset). Бэкап сохранён перед удалением таблиц.");
                DropAllTables(connectionString);
                Console.WriteLine("[Migrations] All tables dropped. Applying all migrations...");
            }
            else
            {
                throw new MigrationException(
                    "База данных содержит таблицы, но отсутствует история миграций (VersionInfo). " +
                    "Автоматический сброс таблиц отключён во избежание потери данных. " +
                    "Восстановите БД из резервной копии (.bak) или запустите с явным флагом сброса " +
                    "(переменная окружения LAD_ALLOW_DB_RESET=1), предварительно убедившись, что сброс безопасен.");
            }
        }
        else if (hasExistingTables)
        {
            // Бэкап перед применением миграций на существующей БД (защита данных).
            BackupBeforeMigration(dbPath);
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
        catch (Exception ex)
        {
            Console.WriteLine($"[Migrations] Failed: {ex.Message}");
            // Запись о неудачной миграции в VersionInfo не создаётся (версия фиксируется
            // только после успешного применения), поэтому при следующем запуске миграция
            // запустится заново сама. НЕ удаляем соседние записи VersionInfo:
            // это может «откатить» уже применённые миграции и сломать БД.
            throw;
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

    private static void BackupBeforeMigration(string? dbPath)
    {
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            return;

        try
        {
            string backupPath = dbPath + ".bak";
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            // VACUUM INTO даёт консистентную онлайн-копию БД без необходимости
            // ручного копирования sidecar-файлов (-wal/-shm).
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
            cmd.ExecuteNonQuery();

            Console.WriteLine($"[Migrations] Pre-migration backup saved: {backupPath}");
        }
        catch (Exception ex)
        {
            // Бэкап — мера безопасности; если он не удался, продолжаем миграцию,
            // но предупреждаем (в отличие от дропа — здесь данные ещё целы).
            Console.WriteLine($"[Migrations] WARNING: pre-migration backup failed: {ex.Message}");
        }
    }

    private static string? ExtractDataSource(string connectionString)
    {
        foreach (var part in connectionString.Split(';'))
        {
            var trimmed = part.Trim();
            if (!trimmed.StartsWith("Data Source", StringComparison.OrdinalIgnoreCase))
                continue;
            int eq = trimmed.IndexOf('=');
            if (eq < 0) continue;
            var value = trimmed.Substring(eq + 1).Trim().Trim('\'', '"');
            return value.Length == 0 ? null : value;
        }
        return null;
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
