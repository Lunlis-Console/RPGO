using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(30)]
public class FixBurstStrikeCooldown : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("UPDATE skills SET cooldown_ms = 20000 WHERE id = 'SK0002'");
    }
}
