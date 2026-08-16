using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1050)]
public class QuestEditorFields : ForwardOnlyMigration
{
    public override void Up()
    {
        // Поля для более удобного редактирования квестов в редакторе:
        // - NPC, который выдаёт квест
        // - флаг «сюжетный» (явный, вместо неявного по chain_id)
        // - локация
        // - диалоги квеста (JSON в формате DialogueParser)
        Alter.Table("quests_def")
            .AddColumn("giver_npc_id").AsString().WithDefaultValue("")
            .AddColumn("is_story").AsInt32().WithDefaultValue(0)
            .AddColumn("location").AsString().WithDefaultValue("")
            .AddColumn("dialogue").AsString().WithDefaultValue("");
    }
}
