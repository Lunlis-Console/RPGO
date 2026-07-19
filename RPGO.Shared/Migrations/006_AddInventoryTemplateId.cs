using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(6)]
public class AddInventoryTemplateId : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE inventory ADD COLUMN template_id TEXT DEFAULT ''");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE inventory DROP COLUMN template_id");
    }
}
