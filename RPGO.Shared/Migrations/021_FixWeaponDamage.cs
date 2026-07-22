using FluentMigrator;

namespace RPGO.Shared.Migrations;

[Migration(21)]
public class FixWeaponDamage : ForwardOnlyMigration
{
    public override void Up()
    {
        // Фиксированный урон: damage_min = damage_max = среднее(min, max), округлённое
        // === Мечи (I0001-I0005) ===
        Execute.Sql("UPDATE items SET damage_min = 2, damage_max = 2 WHERE id = 'I0001'");
        Execute.Sql("UPDATE items SET damage_min = 5, damage_max = 5 WHERE id = 'I0002'");
        Execute.Sql("UPDATE items SET damage_min = 6, damage_max = 6 WHERE id = 'I0003'");
        Execute.Sql("UPDATE items SET damage_min = 9, damage_max = 9 WHERE id = 'I0004'");
        Execute.Sql("UPDATE items SET damage_min = 15, damage_max = 15 WHERE id = 'I0005'");

        // === Топоры (I0301-I0305) ===
        Execute.Sql("UPDATE items SET damage_min = 2, damage_max = 2 WHERE id = 'I0301'");
        Execute.Sql("UPDATE items SET damage_min = 5, damage_max = 5 WHERE id = 'I0302'");
        Execute.Sql("UPDATE items SET damage_min = 8, damage_max = 8 WHERE id = 'I0303'");
        Execute.Sql("UPDATE items SET damage_min = 11, damage_max = 11 WHERE id = 'I0304'");
        Execute.Sql("UPDATE items SET damage_min = 18, damage_max = 18 WHERE id = 'I0305'");

        // === Булавы (I0306-I0310) ===
        Execute.Sql("UPDATE items SET damage_min = 3, damage_max = 3 WHERE id = 'I0306'");
        Execute.Sql("UPDATE items SET damage_min = 6, damage_max = 6 WHERE id = 'I0307'");
        Execute.Sql("UPDATE items SET damage_min = 11, damage_max = 11 WHERE id = 'I0308'");
        Execute.Sql("UPDATE items SET damage_min = 16, damage_max = 16 WHERE id = 'I0309'");
        Execute.Sql("UPDATE items SET damage_min = 24, damage_max = 24 WHERE id = 'I0310'");

        // === Молоты (I0311-I0315) ===
        Execute.Sql("UPDATE items SET damage_min = 5, damage_max = 5 WHERE id = 'I0311'");
        Execute.Sql("UPDATE items SET damage_min = 8, damage_max = 8 WHERE id = 'I0312'");
        Execute.Sql("UPDATE items SET damage_min = 13, damage_max = 13 WHERE id = 'I0313'");
        Execute.Sql("UPDATE items SET damage_min = 19, damage_max = 19 WHERE id = 'I0314'");
        Execute.Sql("UPDATE items SET damage_min = 27, damage_max = 27 WHERE id = 'I0315'");

        // === Кинжалы (I0316-I0320) ===
        Execute.Sql("UPDATE items SET damage_min = 1, damage_max = 1 WHERE id = 'I0316'");
        Execute.Sql("UPDATE items SET damage_min = 3, damage_max = 3 WHERE id = 'I0317'");
        Execute.Sql("UPDATE items SET damage_min = 6, damage_max = 6 WHERE id = 'I0318'");
        Execute.Sql("UPDATE items SET damage_min = 10, damage_max = 10 WHERE id = 'I0319'");
        Execute.Sql("UPDATE items SET damage_min = 14, damage_max = 14 WHERE id = 'I0320'");

        // Синхронизация уже выданных предметов
        Execute.Sql("UPDATE inventory SET damage_min = (SELECT damage_min FROM items WHERE id = inventory.template_id), damage_max = (SELECT damage_max FROM items WHERE id = inventory.template_id) WHERE template_id IS NOT NULL AND template_id != ''");
    }
}
