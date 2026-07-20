using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(10)]
public class AddPlayerEquipmentTable : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("player_equipment")
            .WithColumn("player_name").AsString().NotNullable()
            .WithColumn("slot").AsString().NotNullable()
            .WithColumn("item_id").AsString().NotNullable();

        // Переносим старую экипировку (weapon/armor/accessory) в новую таблицу слотов
        Execute.Sql(@"
            INSERT INTO player_equipment (player_name, slot, item_id)
            SELECT player_name, 'weapon', weapon_id FROM accounts WHERE weapon_id IS NOT NULL AND weapon_id <> ''
            UNION ALL
            SELECT player_name, 'armor', armor_id FROM accounts WHERE armor_id IS NOT NULL AND armor_id <> ''
            UNION ALL
            SELECT player_name, 'accessory', accessory_id FROM accounts WHERE accessory_id IS NOT NULL AND accessory_id <> ''");
    }
}
