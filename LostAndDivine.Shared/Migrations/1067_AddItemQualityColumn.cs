using FluentMigrator;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Shared.Migrations;

[Migration(1067)]
public class AddItemQualityColumn : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.WithConnection((conn, trans) =>
        {
            // Идемпотентность: миграция прогоняется и на game.db, и на content.db;
            // колонка quality может уже существовать (например, в game.db).
            bool hasItemsTable = false;
            using (var t = conn.CreateCommand())
            {
                t.Transaction = trans;
                t.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='items'";
                hasItemsTable = Convert.ToInt64(t.ExecuteScalar()) > 0;
            }

            if (!hasItemsTable) return;

            bool hasQualityCol = false;
            using (var c = conn.CreateCommand())
            {
                c.Transaction = trans;
                c.CommandText = "PRAGMA table_info(items)";
                using var rd = c.ExecuteReader();
                while (rd.Read())
                {
                    if (string.Equals(rd.GetString(1), "quality", StringComparison.OrdinalIgnoreCase)) { hasQualityCol = true; break; }
                }
            }

            if (!hasQualityCol)
            {
                using var alt = conn.CreateCommand();
                alt.Transaction = trans;
                alt.CommandText = "ALTER TABLE items ADD COLUMN quality INTEGER NOT NULL DEFAULT 0";
                alt.ExecuteNonQuery();
            }

            // Перенос качества из описания в колонку и очистка префикса.
            using var readCmd = conn.CreateCommand();
            readCmd.Transaction = trans;
            readCmd.CommandText = "SELECT id, description FROM items WHERE description LIKE 'Качество:%'";

            var updates = new List<(string id, int quality, string desc)>();
            using var reader = readCmd.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                string desc = reader.GetString(1);
                int quality = (int)ItemQualityExtensions.ParseFromDescription(desc);
                string clean = ItemQualityExtensions.StripQualityPrefix(desc);
                updates.Add((id, quality, clean));
            }

            foreach (var (id, quality, clean) in updates)
            {
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = trans;
                updateCmd.CommandText = "UPDATE items SET quality = @q, description = @d WHERE id = @id";
                var pq = updateCmd.CreateParameter();
                pq.ParameterName = "@q";
                pq.Value = quality;
                updateCmd.Parameters.Add(pq);
                var pd = updateCmd.CreateParameter();
                pd.ParameterName = "@d";
                pd.Value = clean;
                updateCmd.Parameters.Add(pd);
                var pid = updateCmd.CreateParameter();
                pid.ParameterName = "@id";
                pid.Value = id;
                updateCmd.Parameters.Add(pid);
                updateCmd.ExecuteNonQuery();
            }
        });
    }
}