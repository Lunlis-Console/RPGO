using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1052)]
public class DropQuestDialogue : ForwardOnlyMigration
{
    public override void Up()
    {
        // Диалоги принадлежат NPC (npcs.data) и редактируются единым редактором.
        // Отдельное поле у квеста не используется игрой — убираем, чтобы не дублировать данные.
        Delete.Column("dialogue").FromTable("quests_def");
    }
}
