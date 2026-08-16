using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1047)]
public class AddCharactersTable : ForwardOnlyMigration
{
    public override void Up()
    {
        // IF NOT EXISTS: переживает ситуацию, когда таблица уже создана, но запись
        // о миграции потеряна из VersionInfo (например, после сбоя применения).
        Execute.Sql(@"CREATE TABLE IF NOT EXISTS ""characters"" (
            ""player_name"" TEXT NOT NULL,
            ""account_login"" TEXT NOT NULL,
            ""class"" INTEGER NOT NULL DEFAULT 0,
            ""level"" INTEGER NOT NULL DEFAULT 1,
            ""experience"" INTEGER NOT NULL DEFAULT 0,
            ""health"" INTEGER NOT NULL DEFAULT 100,
            ""max_health"" INTEGER NOT NULL DEFAULT 100,
            ""mana"" INTEGER NOT NULL DEFAULT 100,
            ""gold"" INTEGER NOT NULL DEFAULT 0,
            ""strength"" INTEGER NOT NULL DEFAULT 1,
            ""endurance"" INTEGER NOT NULL DEFAULT 1,
            ""agility"" INTEGER NOT NULL DEFAULT 1,
            ""cunning"" INTEGER NOT NULL DEFAULT 1,
            ""intellect"" INTEGER NOT NULL DEFAULT 1,
            ""wisdom"" INTEGER NOT NULL DEFAULT 1,
            ""attribute_points"" INTEGER NOT NULL DEFAULT 0,
            ""skill_points"" INTEGER NOT NULL DEFAULT 0,
            ""learned_skills"" TEXT NOT NULL DEFAULT '[]',
            ""skill_ranks"" TEXT NOT NULL DEFAULT '{}',
            ""speed"" INTEGER NOT NULL DEFAULT 1,
            ""pos_x"" INTEGER NOT NULL DEFAULT -1,
            ""pos_y"" INTEGER NOT NULL DEFAULT -1,
            ""hotbar_slots"" TEXT NOT NULL DEFAULT '[]',
            ""current_zone"" TEXT NOT NULL DEFAULT 'main',
            ""created_at"" TEXT NOT NULL,
            ""last_login"" TEXT NOT NULL,
            CONSTRAINT ""PK_characters"" PRIMARY KEY (""player_name""))");

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
