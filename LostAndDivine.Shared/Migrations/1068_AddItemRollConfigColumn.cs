using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Колонка roll_config (JSON) в items — конфигурация случайных бонусов
/// для предметов Необычного/Редкого/Эпического качества.
/// </summary>
[Migration(1068)]
public class AddItemRollConfigColumn : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.WithConnection((conn, trans) =>
        {
            // Идемпотентность: миграция прогоняется и на game.db, и на content.db.
            bool hasItemsTable = false;
            using (var t = conn.CreateCommand())
            {
                t.Transaction = trans;
                t.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='items'";
                hasItemsTable = Convert.ToInt64(t.ExecuteScalar()) > 0;
            }

            if (!hasItemsTable) return;

            bool hasRollConfigCol = false;
            using (var c = conn.CreateCommand())
            {
                c.Transaction = trans;
                c.CommandText = "PRAGMA table_info(items)";
                using var rd = c.ExecuteReader();
                while (rd.Read())
                {
                    if (string.Equals(rd.GetString(1), "roll_config", StringComparison.OrdinalIgnoreCase))
                    {
                        hasRollConfigCol = true;
                        break;
                    }
                }
            }

            if (!hasRollConfigCol)
            {
                using var alt = conn.CreateCommand();
                alt.Transaction = trans;
                alt.CommandText = "ALTER TABLE items ADD COLUMN roll_config TEXT";
                alt.ExecuteNonQuery();
            }
        });
    }
}
