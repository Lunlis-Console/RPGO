using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Лут становится обычными предметами: записи loot_tables превращаются в предметы
/// типа "trophy" в таблице items, а дропы монстров переезжают в monster_drops
/// (монстр → предмет с шансом). Таблица loot_tables удаляется.
/// </summary>
[Migration(1054)]
public class MonsterDropsAsItems : ForwardOnlyMigration
{
    public override void Up()
    {
        // 1. Уникальные записи лута → предметы-трофеи (id вида T0001, T0002, ...)
        Execute.Sql(@"
            INSERT INTO items (id, name, type, value, description, quest_item)
            SELECT 'T' || printf('%04d', ROW_NUMBER() OVER (ORDER BY name)),
                   name, 'trophy', value, description, quest_item
            FROM (
                SELECT DISTINCT name, description, value, COALESCE(quest_item, 0) AS quest_item
                FROM loot_tables
            )");

        // 2. Дропы монстров
        Create.Table("monster_drops")
            .WithColumn("monster_id").AsString().NotNullable()
            .WithColumn("item_id").AsString().NotNullable()
            .WithColumn("drop_chance").AsInt32().WithDefaultValue(30);

        Execute.Sql(@"
            INSERT INTO monster_drops (monster_id, item_id, drop_chance)
            SELECT l.monster_id, i.id, l.drop_chance
            FROM loot_tables l
            JOIN items i
                ON i.type = 'trophy'
               AND i.name = l.name
               AND i.description = l.description
               AND i.value = l.value
               AND i.quest_item = COALESCE(l.quest_item, 0)");

        // 3. Старая таблица больше не нужна
        Execute.Sql("DROP TABLE loot_tables");
    }
}
