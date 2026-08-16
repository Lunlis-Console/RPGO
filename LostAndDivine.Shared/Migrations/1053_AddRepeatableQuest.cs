using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1053)]
public class AddRepeatableQuest : ForwardOnlyMigration
{
    public override void Up()
    {
        // Повторяемый квест: после сдачи его можно взять снова.
        // Для сюжетных квестов (is_story=1) параметр не используется (всегда 0).
        Alter.Table("quests_def")
            .AddColumn("repeatable").AsInt32().WithDefaultValue(0);
    }
}
