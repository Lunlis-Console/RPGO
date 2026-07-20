using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(11)]
public class AddCloakSlot : ForwardOnlyMigration
{
    public override void Up()
    {
        // Синхронизация справочника equipment_slots с RPGGame.Shared.Models.EquipmentSlots
        // (в коде добавлен слот "cloak" — Плащ). INSERT OR IGNORE — безопасно при повторном запуске.
        Execute.Sql(
            "INSERT OR IGNORE INTO equipment_slots (id, name_ru, is_paperdoll, z_order, accepts_two_handed, blocked_by_two_handed) " +
            "VALUES ('cloak', 'Плащ', 0, 0, 0, 0);");
    }
}
