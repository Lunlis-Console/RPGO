using FluentMigrator;
using RPGGame.Shared.Models;

namespace RPGGame.Shared.Migrations;

[Migration(1042)]
public class FixQualityBonuses : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            UPDATE items SET
                bonus_strength = 0, bonus_endurance = 0, bonus_agility = 0,
                bonus_cunning = 0, bonus_intellect = 0, bonus_wisdom = 0,
                bonus_phys_attack = 0, bonus_mag_attack = 0,
                bonus_defense = 0, bonus_resistance = 0
            WHERE description LIKE 'Качество:%'
        ");

        Execute.WithConnection((conn, trans) =>
        {
            using var readCmd = conn.CreateCommand();
            readCmd.Transaction = trans;
            readCmd.CommandText = "SELECT id, description, required_level FROM items WHERE description LIKE 'Качество:%'";

            var updates = new List<(string id, string stat, int value)>();
            using var reader = readCmd.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                string desc = reader.GetString(1);
                int reqLvl = reader.GetInt32(2);
                var quality = ItemQualityExtensions.ParseFromDescription(desc);
                int target = quality switch
                {
                    ItemQuality.Uncommon => 1,
                    ItemQuality.Rare => 2,
                    ItemQuality.Epic => 3,
                    _ => 0
                };
                if (target == 0) continue;

                int bonusValue = Math.Max(1, reqLvl / 5);
                string[] allStats = { "bonus_strength", "bonus_endurance", "bonus_agility", "bonus_cunning", "bonus_intellect", "bonus_wisdom" };
                int itemNum = int.Parse(id.AsSpan(1));

                for (int t = 0; t < target; t++)
                {
                    int si = (itemNum + t * 2) % allStats.Length;
                    updates.Add((id, allStats[si], bonusValue));
                }
            }

            foreach (var (id, stat, value) in updates)
            {
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = trans;
                updateCmd.CommandText = $"UPDATE items SET {stat} = {value} WHERE id = '{id}'";
                updateCmd.ExecuteNonQuery();
            }
        });
    }
}
