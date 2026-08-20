using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Своя иконка предмета: ключ PNG-файла из Content/Sprites/CustomIcons (без расширения).
/// Редактор копирует выбранный файл и пишет имя файла сюда.
/// </summary>
[Migration(1065)]
public class AddItemIcon : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Column("icon").OnTable("items").AsString(64).Nullable();
        Create.Column("icon").OnTable("inventory").AsString(64).Nullable();
        Create.Column("icon").OnTable("storage_items").AsString(64).Nullable();
    }
}