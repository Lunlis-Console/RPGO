using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Персистентное хранилище reconnect-токенов: после перезапуска сервера токены
/// переживают перезапуск, и игрок может вернуться в игру по reconnect.
/// </summary>
[Migration(1031)]
public class AddSessionTokens : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("session_tokens")
            .WithColumn("token").AsString(200).PrimaryKey()
            .WithColumn("player_name").AsString(64).NotNullable()
            .WithColumn("expiry").AsInt64().NotNullable();

        Create.Index("IX_session_tokens_player_name")
            .OnTable("session_tokens")
            .OnColumn("player_name");
    }
}
