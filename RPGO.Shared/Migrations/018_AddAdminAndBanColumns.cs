using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Добавляем колонки is_admin, is_banned, ban_reason для системы администрирования.
/// </summary>
[Migration(18)]
public class AddAdminAndBanColumns : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE accounts ADD COLUMN is_admin INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE accounts ADD COLUMN is_banned INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE accounts ADD COLUMN ban_reason TEXT NOT NULL DEFAULT ''");
    }
}
