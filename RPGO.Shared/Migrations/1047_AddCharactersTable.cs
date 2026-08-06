using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1047)]
public class AddCharactersTable : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("characters")
            .WithColumn("player_name").AsString().NotNullable().PrimaryKey()
            .WithColumn("account_login").AsString().NotNullable()
            .WithColumn("class").AsInt32().WithDefaultValue(0)
            .WithColumn("level").AsInt32().WithDefaultValue(1)
            .WithColumn("experience").AsInt32().WithDefaultValue(0)
            .WithColumn("health").AsInt32().WithDefaultValue(100)
            .WithColumn("max_health").AsInt32().WithDefaultValue(100)
            .WithColumn("mana").AsInt32().WithDefaultValue(100)
            .WithColumn("gold").AsInt32().WithDefaultValue(0)
            .WithColumn("strength").AsInt32().WithDefaultValue(1)
            .WithColumn("endurance").AsInt32().WithDefaultValue(1)
            .WithColumn("agility").AsInt32().WithDefaultValue(1)
            .WithColumn("cunning").AsInt32().WithDefaultValue(1)
            .WithColumn("intellect").AsInt32().WithDefaultValue(1)
            .WithColumn("wisdom").AsInt32().WithDefaultValue(1)
            .WithColumn("attribute_points").AsInt32().WithDefaultValue(0)
            .WithColumn("skill_points").AsInt32().WithDefaultValue(0)
            .WithColumn("learned_skills").AsString().WithDefaultValue("[]")
            .WithColumn("skill_ranks").AsString().WithDefaultValue("{}")
            .WithColumn("speed").AsInt32().WithDefaultValue(1)
            .WithColumn("pos_x").AsInt32().WithDefaultValue(-1)
            .WithColumn("pos_y").AsInt32().WithDefaultValue(-1)
            .WithColumn("hotbar_slots").AsString().WithDefaultValue("[]")
            .WithColumn("current_zone").AsString().WithDefaultValue("main")
            .WithColumn("created_at").AsString().NotNullable()
            .WithColumn("last_login").AsString().NotNullable();

        // Migrate existing accounts: one character per account
        Execute.Sql(@"
            INSERT OR IGNORE INTO characters
                (player_name, account_login, class, level, experience, health, max_health, mana, gold,
                 strength, endurance, agility, cunning, intellect, wisdom, attribute_points, skill_points,
                 learned_skills, skill_ranks, speed, pos_x, pos_y, hotbar_slots, current_zone,
                 created_at, last_login)
            SELECT
                player_name, login, 0, level, experience, health, max_health, COALESCE(mana, 100), gold,
                strength, endurance, agility, cunning, intellect, wisdom, attribute_points, COALESCE(skill_points, 0),
                COALESCE(learned_skills, '[]'), COALESCE(skill_ranks, '{}'), speed, COALESCE(pos_x, -1), COALESCE(pos_y, -1),
                COALESCE(hotbar_slots, '[]'), COALESCE(current_zone, 'main'),
                created_at, last_login
            FROM accounts
            WHERE NOT EXISTS (SELECT 1 FROM characters WHERE characters.player_name = accounts.player_name)
        ");
    }
}
