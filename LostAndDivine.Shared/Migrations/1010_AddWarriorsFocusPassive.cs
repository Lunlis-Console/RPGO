using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1010)]
public class AddWarriorsFocusPassive : ForwardOnlyMigration
{
    public override void Up()
    {
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0006",
            name = "Концентрация воина",
            description = "Увеличивает точность на 10% и шанс критического удара на 10% по оглушённым целям.",
            type = "Пассивные",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0003",
            tier = 2
        });
    }
}
