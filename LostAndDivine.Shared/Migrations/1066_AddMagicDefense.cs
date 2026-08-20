using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Базовая магическая защита предмета (броня): показывается в «Характеристиках» предмета
/// и суммируется в сопротивление персонажа при надевании (аналог defense для физ. защиты).
/// </summary>
[Migration(1066)]
public class AddMagicDefense : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Column("magic_defense").OnTable("items").AsInt32().WithDefaultValue(0);
        Create.Column("magic_defense").OnTable("inventory").AsInt32().WithDefaultValue(0);
        Create.Column("magic_defense").OnTable("storage_items").AsInt32().WithDefaultValue(0);
    }
}