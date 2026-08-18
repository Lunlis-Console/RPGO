using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Новые бонусы экипировки к характеристикам: точность, стойкость, пробивание брони,
/// сокращение отката, регенерация ХП и МП (в процентах, складываются с вложенными атрибутами
/// в кривой убывающей отдачи, капы те же, что у атрибутных частей).
/// </summary>
[Migration(1057)]
public class AddNewStatBonuses : ForwardOnlyMigration
{
    public override void Up()
    {
        foreach (var table in new[] { "items", "inventory" })
        {
            Create.Column("bonus_accuracy").OnTable(table).AsDouble().WithDefaultValue(0);
            Create.Column("bonus_tenacity").OnTable(table).AsDouble().WithDefaultValue(0);
            Create.Column("bonus_armor_penetration").OnTable(table).AsDouble().WithDefaultValue(0);
            Create.Column("bonus_cooldown_reduction").OnTable(table).AsDouble().WithDefaultValue(0);
            Create.Column("bonus_hp_regen").OnTable(table).AsDouble().WithDefaultValue(0);
            Create.Column("bonus_mp_regen").OnTable(table).AsDouble().WithDefaultValue(0);
        }
    }
}