using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Добавляем подтип оружия (sword/axe/mace/hammer/dagger) для отображения в тултипах.
/// </summary>
[Migration(17)]
public class AddWeaponSubtype : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE items ADD COLUMN weapon_subtype TEXT NOT NULL DEFAULT ''");

        // Мечи (I0001-I0005)
        Execute.Sql("UPDATE items SET weapon_subtype = 'sword' WHERE id IN ('I0001','I0002','I0003','I0004','I0005')");
        // Топоры (I0301-I0305)
        Execute.Sql("UPDATE items SET weapon_subtype = 'axe' WHERE id IN ('I0301','I0302','I0303','I0304','I0305')");
        // Булавы (I0306-I0310)
        Execute.Sql("UPDATE items SET weapon_subtype = 'mace' WHERE id IN ('I0306','I0307','I0308','I0309','I0310')");
        // Молоты (I0311-I0315)
        Execute.Sql("UPDATE items SET weapon_subtype = 'hammer' WHERE id IN ('I0311','I0312','I0313','I0314','I0315')");
        // Кинжалы (I0316-I0320)
        Execute.Sql("UPDATE items SET weapon_subtype = 'dagger' WHERE id IN ('I0316','I0317','I0318','I0319','I0320')");
    }
}
