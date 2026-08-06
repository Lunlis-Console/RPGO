using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1019)]
public class AddManaColumn : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Column("mana").OnTable("accounts").AsInt32().WithDefaultValue(100);
    }
}
