using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1021)]
public class AddSkillMaxRank : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Column("max_rank").OnTable("skills").AsInt32().WithDefaultValue(3);
    }
}
