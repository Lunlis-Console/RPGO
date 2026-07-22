using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(20)]
public class AddWeaponDamage : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE items ADD COLUMN damage_min INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE items ADD COLUMN damage_max INTEGER NOT NULL DEFAULT 0");

        Execute.Sql("ALTER TABLE inventory ADD COLUMN damage_min INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE inventory ADD COLUMN damage_max INTEGER NOT NULL DEFAULT 0");

        // === Мечи (I0001-I0005) ===
        Execute.Sql("UPDATE items SET damage_min = 1, damage_max = 2, bonus_phys_attack = 0 WHERE id = 'I0001'");
        Execute.Sql("UPDATE items SET damage_min = 3, damage_max = 6, bonus_phys_attack = 0 WHERE id = 'I0002'");
        Execute.Sql("UPDATE items SET damage_min = 4, damage_max = 8, bonus_phys_attack = 0 WHERE id = 'I0003'");
        Execute.Sql("UPDATE items SET damage_min = 6, damage_max = 12, bonus_phys_attack = 0 WHERE id = 'I0004'");
        Execute.Sql("UPDATE items SET damage_min = 10, damage_max = 20, bonus_phys_attack = 0 WHERE id = 'I0005'");

        // === Топоры (I0301-I0305) ===
        Execute.Sql("UPDATE items SET damage_min = 1, damage_max = 3, bonus_phys_attack = 0 WHERE id = 'I0301'");
        Execute.Sql("UPDATE items SET damage_min = 3, damage_max = 7, bonus_phys_attack = 0 WHERE id = 'I0302'");
        Execute.Sql("UPDATE items SET damage_min = 5, damage_max = 11, bonus_phys_attack = 0 WHERE id = 'I0303'");
        Execute.Sql("UPDATE items SET damage_min = 7, damage_max = 15, bonus_phys_attack = 0 WHERE id = 'I0304'");
        Execute.Sql("UPDATE items SET damage_min = 12, damage_max = 24, bonus_phys_attack = 0 WHERE id = 'I0305'");

        // === Булавы (I0306-I0310) ===
        Execute.Sql("UPDATE items SET damage_min = 2, damage_max = 5, bonus_phys_attack = 0 WHERE id = 'I0306'");
        Execute.Sql("UPDATE items SET damage_min = 4, damage_max = 9, bonus_phys_attack = 0 WHERE id = 'I0307'");
        Execute.Sql("UPDATE items SET damage_min = 7, damage_max = 15, bonus_phys_attack = 0 WHERE id = 'I0308'");
        Execute.Sql("UPDATE items SET damage_min = 10, damage_max = 21, bonus_phys_attack = 0 WHERE id = 'I0309'");
        Execute.Sql("UPDATE items SET damage_min = 16, damage_max = 32, bonus_phys_attack = 0 WHERE id = 'I0310'");

        // === Молоты (I0311-I0315) ===
        Execute.Sql("UPDATE items SET damage_min = 3, damage_max = 7, bonus_phys_attack = 0 WHERE id = 'I0311'");
        Execute.Sql("UPDATE items SET damage_min = 5, damage_max = 11, bonus_phys_attack = 0 WHERE id = 'I0312'");
        Execute.Sql("UPDATE items SET damage_min = 8, damage_max = 17, bonus_phys_attack = 0 WHERE id = 'I0313'");
        Execute.Sql("UPDATE items SET damage_min = 12, damage_max = 25, bonus_phys_attack = 0 WHERE id = 'I0314'");
        Execute.Sql("UPDATE items SET damage_min = 18, damage_max = 36, bonus_phys_attack = 0 WHERE id = 'I0315'");

        // === Кинжалы (I0316-I0320) ===
        Execute.Sql("UPDATE items SET damage_min = 1, damage_max = 2, bonus_phys_attack = 0 WHERE id = 'I0316'");
        Execute.Sql("UPDATE items SET damage_min = 2, damage_max = 5, bonus_phys_attack = 0 WHERE id = 'I0317'");
        Execute.Sql("UPDATE items SET damage_min = 4, damage_max = 9, bonus_phys_attack = 0 WHERE id = 'I0318'");
        Execute.Sql("UPDATE items SET damage_min = 6, damage_max = 13, bonus_phys_attack = 0 WHERE id = 'I0319'");
        Execute.Sql("UPDATE items SET damage_min = 9, damage_max = 19, bonus_phys_attack = 0 WHERE id = 'I0320'");

        // Обновляем уже выданные предметы в инвентарях игроков
        Execute.Sql("UPDATE inventory SET damage_min = (SELECT damage_min FROM items WHERE id = inventory.template_id), damage_max = (SELECT damage_max FROM items WHERE id = inventory.template_id), bonus_phys_attack = (SELECT bonus_phys_attack FROM items WHERE id = inventory.template_id) WHERE template_id IS NOT NULL AND template_id != ''");
    }
}
