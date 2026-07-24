using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(28)]
public class ReworkSkillSilverHand : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE skills ADD COLUMN skill_point_cost INTEGER NOT NULL DEFAULT 1");

        Execute.Sql(@"UPDATE skills SET
            name = 'Крепкая рука',
            description = 'Увеличивает урон ближней атаки на 15%. 100% шанс прока оружия.',
            cooldown_ms = 5000,
            damage_multiplier = 1.15,
            skill_point_cost = 1
            WHERE id = 'SK0001'");
    }
}
