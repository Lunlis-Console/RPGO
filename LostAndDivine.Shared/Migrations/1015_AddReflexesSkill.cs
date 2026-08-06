using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1015)]
public class AddReflexesSkill : ForwardOnlyMigration
{
    public override void Up()
    {
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0008",
            name = "Рефлексы",
            description = "Когда экипировано два одноручных оружия, шанс парирования повышен на 10%.",
            type = "Пассивные",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0006",
            tier = 3
        });
    }
}
