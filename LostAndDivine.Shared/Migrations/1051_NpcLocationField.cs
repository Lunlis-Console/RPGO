using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1051)]
public class NpcLocationField : ForwardOnlyMigration
{
    public override void Up()
    {
        // Локация NPC — используется для авто-заполнения поля «Локация» у квестов.
        Alter.Table("npcs")
            .AddColumn("location").AsString().WithDefaultValue("");
    }
}
