using LostAndDivine.Shared.Migrations;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Tests;

public class MigrationRepairTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"rpg_mig_{Guid.NewGuid():N}.db");

    private static void Cleanup(string db)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(db);
    }

    [Fact]
    public void Migrations_ApplyOnFreshDb_AndRerunIsNoOp()
    {
        string db = TempDb();
        try
        {
            DbMigrationRunner.RunMigrations($"Data Source={db}");
            DbMigrationRunner.RunMigrations($"Data Source={db}");

            using var conn = new SqliteConnection($"Data Source={db}");
            conn.Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='characters'";
            Assert.NotNull(cmd.ExecuteScalar());

            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('quests_def') WHERE name='target_zone_id'";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));

            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('items') WHERE name='quest_item'";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));

            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='monster_drops'";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));

            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='loot_tables'";
            Assert.Equal(0, Convert.ToInt32(cmd.ExecuteScalar()));

            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('monsters') WHERE name='gold_max'";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));

            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='uq_player_completed_quests'";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void Migrations_1062_UniqueIndex_BlocksDuplicateCompletedQuests()
    {
        string db = TempDb();
        try
        {
            DbMigrationRunner.RunMigrations($"Data Source={db}");

            using var conn = new SqliteConnection($"Data Source={db}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO player_completed_quests (player_name, quest_id, completed_at) VALUES ('Hero', 'Q0001', '2026-08-01T10:00:00Z')";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT OR IGNORE INTO player_completed_quests (player_name, quest_id, completed_at) VALUES ('Hero', 'Q0001', '2026-08-02T10:00:00Z')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT COUNT(*) FROM player_completed_quests WHERE player_name = 'Hero' AND quest_id = 'Q0001'";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void Migrations_TablesWithoutVersionInfo_ThrowsFailSafe_ByDefault()
    {
        string db = TempDb();
        try
        {
            DbMigrationRunner.RunMigrations($"Data Source={db}");

            // Имитация порчи tracking-таблицы: таблицы есть, VersionInfo удалена целиком
            using (var conn = new SqliteConnection($"Data Source={db}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DROP TABLE VersionInfo";
                cmd.ExecuteNonQuery();
            }

            // По умолчанию (без явного флага сброса) старт НЕ должен уничтожать данные
            Assert.Throws<MigrationException>(() => DbMigrationRunner.RunMigrations($"Data Source={db}"));

            // Данные должны уцелеть: бэкап и таблицы на месте
            Assert.True(File.Exists(db + ".bak"));
            using var conn2 = new SqliteConnection($"Data Source={db}");
            conn2.Open();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='accounts'";
            Assert.NotNull(cmd2.ExecuteScalar());
        }
        finally { Cleanup(db); Cleanup(db + ".bak"); }
    }

    [Fact]
    public void Migrations_TablesWithoutVersionInfo_ResetAllowed_DropsAndRecreates()
    {
        string db = TempDb();
        try
        {
            DbMigrationRunner.RunMigrations($"Data Source={db}");
            using (var conn = new SqliteConnection($"Data Source={db}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DROP TABLE VersionInfo";
                cmd.ExecuteNonQuery();
            }

            // С явным флагом сброса — таблицы пересоздаются без исключения
            DbMigrationRunner.RunMigrations($"Data Source={db}", allowDestructiveReset: true);

            using var conn2 = new SqliteConnection($"Data Source={db}");
            conn2.Open();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='accounts'";
            Assert.NotNull(cmd2.ExecuteScalar());
        }
        finally { Cleanup(db); Cleanup(db + ".bak"); }
    }

    [Fact]
    public void Migrations_Repair_WhenTableExistsButVersionInfoRowLost()
    {
        string db = TempDb();
        try
        {
            DbMigrationRunner.RunMigrations($"Data Source={db}");

            // Имитация сбоя, при котором запись о применённой миграции потеряна,
            // а сама таблица осталась
            using (var conn = new SqliteConnection($"Data Source={db}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM VersionInfo WHERE Version = 1047";
                cmd.ExecuteNonQuery();
            }

            // Повторный запуск не должен падать с «table already exists»
            DbMigrationRunner.RunMigrations($"Data Source={db}");

            // После повторного применения 1048/1049 должны быть применены
            using var conn2 = new SqliteConnection($"Data Source={db}");
            conn2.Open();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM pragma_table_info('items') WHERE name='quest_item'";
            Assert.Equal(1, Convert.ToInt32(cmd2.ExecuteScalar()));
        }
        finally { Cleanup(db); }
    }
}
