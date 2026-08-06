using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1017)]
public class AddLifestealPassive : ForwardOnlyMigration
{
    public override void Up()
    {
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0010",
            name = "Кровопускание",
            description = "Вы восстанавливаете 10% от урона, нанесённого мечом.",
            type = "Пассивные",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0008",
            tier = 4
        });
    }
}
