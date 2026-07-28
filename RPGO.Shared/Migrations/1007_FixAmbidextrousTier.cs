using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1007)]
public class FixAmbidextrousTier : ForwardOnlyMigration
{
    public override void Up()
    {
        Update.Table("skills")
            .Set(new { tier = 1 }).Where(new { id = "SK0003" });
    }
}
