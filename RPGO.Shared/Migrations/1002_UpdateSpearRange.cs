using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1002)]
public class UpdateSpearRange : ForwardOnlyMigration
{
    public override void Up()
    {
        // Update spear items (I0421-I0425) to have melee range (1) instead of ranged (2)
        Execute.Sql("UPDATE items SET attack_range = 1 WHERE id IN ('I0421', 'I0422', 'I0423', 'I0424', 'I0425')");
    }
}