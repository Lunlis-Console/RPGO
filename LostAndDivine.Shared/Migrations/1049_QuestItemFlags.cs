using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1049)]
public class QuestItemFlags : ForwardOnlyMigration
{
    public override void Up()
    {
        // Флаг «квестовый предмет»: такие нельзя продавать
        // (обычные собираемые предметы — ягоды и т.п. — продаются всегда)
        Alter.Table("items")
            .AddColumn("quest_item").AsInt32().WithDefaultValue(0);
        Alter.Table("loot_tables")
            .AddColumn("quest_item").AsInt32().WithDefaultValue(0);
    }
}