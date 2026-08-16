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
        }
        finally { Cleanup(db); }
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
