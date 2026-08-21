using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Колонка enhancement_level в inventory: уровень заточки/усиления предмета.
/// Хранится только уровень, сами статьи предмета не мутируются (бонус считается
/// на лету через EnhancementHelper).
/// </summary>
[Migration(1070)]
public class AddEnhancementLevelColumn : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.WithConnection((conn, trans) =>
        {
            bool hasTable = false;
            using (var t = conn.CreateCommand())
            {
                t.Transaction = trans;
                t.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='inventory'";
                hasTable = Convert.ToInt64(t.ExecuteScalar()) > 0;
            }

            if (!hasTable) return;

            bool hasCol = false;
            using (var c = conn.CreateCommand())
            {
                c.Transaction = trans;
                c.CommandText = "PRAGMA table_info(inventory)";
                using var rd = c.ExecuteReader();
                while (rd.Read())
                {
                    if (string.Equals(rd.GetString(1), "enhancement_level", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCol = true;
                        break;
                    }
                }
            }

            if (!hasCol)
            {
                using var alt = conn.CreateCommand();
                alt.Transaction = trans;
                alt.CommandText = "ALTER TABLE inventory ADD COLUMN enhancement_level INTEGER NOT NULL DEFAULT 0";
                alt.ExecuteNonQuery();
            }
        });
    }
}
