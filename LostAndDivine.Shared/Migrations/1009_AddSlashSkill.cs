using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1009)]
public class AddSlashSkill : ForwardOnlyMigration
{
    public override void Up()
    {
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0004",
            name = "Разрез",
            description = "Две последовательные атаки (правой и левой рукой) с увеличенным уроном 150% от базового физического урона с повышенным крит. уроном на 20%.",
            type = "Активные",
            mp_cost = 15,
            cooldown_ms = 10000,
            damage_multiplier = 1.5,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0002",
            tier = 3
        });
    }
}
