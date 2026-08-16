using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Золото за убийство монстра становится диапазоном: добавляется колонка gold_max
/// (максимум выпадения). gold_reward — минимум. gold_max = 0 означает "ровно gold_reward"
/// (без случайности).
/// </summary>
[Migration(1055)]
public class AddGoldMax : ForwardOnlyMigration
{
    public override void Up()
    {
        Alter.Table("monsters")
            .AddColumn("gold_max").AsInt32().WithDefaultValue(0);
    }
}