using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1020)]
public class AddSkillCastTime : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Column("cast_time_ms").OnTable("skills").AsInt32().WithDefaultValue(0);
    }
}
