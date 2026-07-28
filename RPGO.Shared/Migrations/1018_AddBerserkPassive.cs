using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1018)]
public class AddBerserkPassive : ForwardOnlyMigration
{
    public override void Up()
    {
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0011",
            name = "Берсерк",
            description = "Чем ниже здоровье, тем выше урон: +2% к урону за каждые 5% потерянного здоровья (до ~+40% на 1 HP).",
            type = "Пассивные",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0010",
            tier = 5
        });
    }
}
