using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1013)]
public class AddHolyTrinitySkill : ForwardOnlyMigration
{
    public override void Up()
    {
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0007",
            name = "Святая троица",
            description = "Три последовательных удара с уроном 200% от базы. Каждый удар имеет 15% шанс наложить Обездвиживание, Обезоруживание или Контузию на 3 секунды.",
            type = "Активные",
            mp_cost = 25,
            cooldown_ms = 18000,
            damage_multiplier = 2.0,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0004",
            tier = 4
        });
    }
}
