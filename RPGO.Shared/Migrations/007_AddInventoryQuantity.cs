using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(7)]
public class AddInventoryQuantity : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE inventory ADD COLUMN quantity INTEGER NOT NULL DEFAULT 1");

        // Схлопываем старые дубликаты: для каждой группы (player_name, template_id)
        // оставляем одну строку с item_id, а остальные удаляем, суммируя количество.
        Execute.Sql(@"
            UPDATE inventory
            SET quantity = (
                SELECT COUNT(*)
                FROM inventory AS dup
                WHERE dup.player_name = inventory.player_name
                  AND COALESCE(dup.template_id, '') = COALESCE(inventory.template_id, '')
                  AND dup.name = inventory.name
                  AND dup.id <= inventory.id
            )
            WHERE id = (
                SELECT MIN(d.id)
                FROM inventory AS d
                WHERE d.player_name = inventory.player_name
                  AND COALESCE(d.template_id, '') = COALESCE(inventory.template_id, '')
                  AND d.name = inventory.name
            );
        ");

        Execute.Sql(@"
            DELETE FROM inventory
            WHERE id NOT IN (
                SELECT MIN(id)
                FROM inventory
                GROUP BY player_name, COALESCE(template_id, ''), name
            );
        ");
    }

    public override void Down()
    {
        // Откат: развернуть quantity обратно в отдельные строки невозможно надёжно,
        // поэтому просто убираем колонку.
        Execute.Sql("ALTER TABLE inventory DROP COLUMN quantity");
    }
}
