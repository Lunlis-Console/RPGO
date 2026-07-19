using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(4)]
public class AddSnake : Migration
{
    public override void Up()
    {
        Execute.Sql(@"INSERT OR IGNORE INTO monsters (id, name, tier, health, attack, defense, xp_reward, gold_reward, symbol, strength, stamina, agility, cunning, wisdom, will)
            VALUES ('M0013', 'Змея', 1, 20, 4, 1, 6, 3, 'n', 1, 1, 3, 2, 1, 1)");

        Execute.Sql("INSERT OR IGNORE INTO loot_tables (monster_id, name, description, value, drop_chance) VALUES ('M0013', 'Змеиная кожа', 'Гладкая чешуя. Используется в кожевенном деле.', 4, 45)");
        Execute.Sql("INSERT OR IGNORE INTO loot_tables (monster_id, name, description, value, drop_chance) VALUES ('M0013', 'Змеиный яд', 'Капля этого яда — смертельна для мышей.', 8, 20)");
    }

    public override void Down()
    {
        Execute.Sql("DELETE FROM loot_tables WHERE monster_id = 'M0013'");
        Execute.Sql("DELETE FROM monsters WHERE id = 'M0013'");
    }
}
