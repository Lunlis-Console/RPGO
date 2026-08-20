using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Колонка quality в inventory: качество теперь роллится на экземпляре предмета
/// (шаблон один, Обычного качества), поэтому его нужно хранить в инвентаре.
/// </summary>
[Migration(1069)]
public class AddInventoryQualityColumn : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.WithConnection((conn, trans) =>
        {
            // Идемпотентность: таблица inventory есть только в game.db.
            bool hasTable = false;
            using (var t = conn.CreateCommand())
            {
                t.Transaction = trans;
                t.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='inventory'";
                hasTable = Convert.ToInt64(t.ExecuteScalar()) > 0;
            }

            if (!hasTable) return;

            bool hasQualityCol = false;
            using (var c = conn.CreateCommand())
            {
                c.Transaction = trans;
                c.CommandText = "PRAGMA table_info(inventory)";
                using var rd = c.ExecuteReader();
                while (rd.Read())
                {
                    if (string.Equals(rd.GetString(1), "quality", StringComparison.OrdinalIgnoreCase))
                    {
                        hasQualityCol = true;
                        break;
                    }
                }
            }

            if (!hasQualityCol)
            {
                using var alt = conn.CreateCommand();
                alt.Transaction = trans;
                alt.CommandText = "ALTER TABLE inventory ADD COLUMN quality INTEGER NOT NULL DEFAULT 0";
                alt.ExecuteNonQuery();
            }

            // Бэкфилл: для старых строк (quality=0) берём качество из шаблона.
            // items может отсутствовать в game.db — тогда просто пропускаем.
            bool hasItemsTable = false;
            using (var t = conn.CreateCommand())
            {
                t.Transaction = trans;
                t.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='items'";
                hasItemsTable = Convert.ToInt64(t.ExecuteScalar()) > 0;
            }

            if (!hasItemsTable) return;

            using var backfill = conn.CreateCommand();
            backfill.Transaction = trans;
            backfill.CommandText = @"UPDATE inventory
                SET quality = COALESCE((SELECT items.quality FROM items WHERE items.id = inventory.template_id), 0)
                WHERE quality = 0 AND COALESCE(template_id, '') != ''";
            backfill.ExecuteNonQuery();
        });
    }
}