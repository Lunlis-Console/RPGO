using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Перчатки теперь надеваются отдельно на правую/левую руку (типы glove_r / glove_l).
/// Конвертируем существующие предметы типа "gloves" в раздельные.
/// </summary>
[Migration(13)]
public class SplitGloves : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("UPDATE items SET type='glove_r' WHERE type='gloves' AND id='I0209'");
        Execute.Sql("UPDATE items SET type='glove_l' WHERE type='gloves' AND id='I0210'");
        // Любые прочие перчатки (на всякий случай) — в правую руку
        Execute.Sql("UPDATE items SET type='glove_r' WHERE type='gloves'");
    }
}
