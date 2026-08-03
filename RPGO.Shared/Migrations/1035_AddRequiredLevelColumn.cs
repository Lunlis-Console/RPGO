using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1035)]
public class AddRequiredLevelColumn : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Column("required_level").OnTable("items").AsInt32().WithDefaultValue(0);
        Create.Column("required_level").OnTable("storage_items").AsInt32().WithDefaultValue(0);
    }
}
