using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1048)]
public class QuestStoryExtras : ForwardOnlyMigration
{
    public override void Up()
    {
        // Сюжетные квесты: цель-зона (explore/авто-выдача), точка на карте (travel), авто-выдача при входе в зону
        Alter.Table("quests_def")
            .AddColumn("target_zone_id").AsString().WithDefaultValue("")
            .AddColumn("target_x").AsInt32().WithDefaultValue(0)
            .AddColumn("target_y").AsInt32().WithDefaultValue(0)
            .AddColumn("auto_grant").AsInt32().WithDefaultValue(0);
    }
}