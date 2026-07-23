using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(24)]
public class FixWeaponRanges : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("UPDATE items SET attack_range = 5 WHERE weapon_subtype = 'bow'");
        Execute.Sql("UPDATE items SET attack_range = 4 WHERE weapon_subtype = 'staff'");
        Execute.Sql("UPDATE inventory SET attack_range = 5 WHERE template_id IN (SELECT id FROM items WHERE weapon_subtype = 'bow')");
        Execute.Sql("UPDATE inventory SET attack_range = 4 WHERE template_id IN (SELECT id FROM items WHERE weapon_subtype = 'staff')");
    }
}
