using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1006)]
public class AddAmbidextrousSkill : ForwardOnlyMigration
{
    public override void Up()
    {
        // Active skills branch
        Update.Table("skills")
            .Set(new { type = "Активные" }).Where(new { id = "SK0001" });
        Update.Table("skills")
            .Set(new { type = "Активные" }).Where(new { id = "SK0002" });

        // Passive skills branch
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0003",
            name = "Амбидекстр",
            description = "Урон от удара левой рукой увеличивается с 50% до 75% (при двойном оружии ближнего боя).",
            type = "Пассивные",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0001",
            tier = 1
        });
    }
}
