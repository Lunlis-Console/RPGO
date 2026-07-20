using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(8)]
public class AddFriends : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("friends")
            .WithColumn("owner_name").AsString().NotNullable()
            .WithColumn("friend_name").AsString().NotNullable()
            .WithColumn("created_at").AsString().NotNullable()
            .WithColumn("note").AsString().WithDefaultValue("");

        // SQLite не поддерживает ALTER ADD CONSTRAINT, поэтому составной PK
        // задаём через UNIQUE-индекс (гарантирует уникальность пары owner+friend).
        Execute.Sql(@"
            CREATE UNIQUE INDEX IF NOT EXISTS uq_friends_pair
            ON friends (owner_name, friend_name);");

        Create.Index("ix_friends_owner").OnTable("friends").OnColumn("owner_name");
    }
}
