using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(29)]
public class AddSkillTreeAndBurstStrike : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE skills ADD COLUMN parent_id TEXT");
        Execute.Sql("ALTER TABLE skills ADD COLUMN tier INTEGER NOT NULL DEFAULT 1");

        // Крепкая рука — тир 1
        Execute.Sql("UPDATE skills SET tier = 1 WHERE id = 'SK0001'");

        // Поток ударов — тир 2, потомок Крепкой руки
        Execute.Sql(@"INSERT INTO skills (id, name, description, type, mp_cost, cooldown_ms, damage_multiplier, min_level, skill_point_cost, parent_id, tier)
            VALUES ('SK0002', 'Поток ударов', 'Накладывает бафф Проворность (+30% к скорости атаки) на 10 секунд.', 'active', 10, 5000, 1.0, 1, 1, 'SK0001', 2)");
    }
}
