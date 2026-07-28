using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1022)]
public class AddSkillRanksToAccounts : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Column("skill_ranks").OnTable("accounts").AsString().WithDefaultValue("{}");
    }
}
