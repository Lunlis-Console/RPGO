using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1001)]
public class AddMail : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("mail")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("sender_name").AsString().NotNullable()
            .WithColumn("recipient_name").AsString().NotNullable()
            .WithColumn("subject").AsString().WithDefaultValue("")
            .WithColumn("body").AsString().WithDefaultValue("")
            .WithColumn("gold_amount").AsInt32().WithDefaultValue(0)
            .WithColumn("item_id").AsString().WithDefaultValue("")
            .WithColumn("item_name").AsString().WithDefaultValue("")
            .WithColumn("item_type").AsString().WithDefaultValue("")
            .WithColumn("item_quantity").AsInt32().WithDefaultValue(0)
            .WithColumn("sent_at").AsString().NotNullable()
            .WithColumn("read_at").AsString().WithDefaultValue("")
            .WithColumn("taken_at").AsString().WithDefaultValue("")
            .WithColumn("is_deleted_sender").AsInt32().WithDefaultValue(0)
            .WithColumn("is_deleted_recipient").AsInt32().WithDefaultValue(0);

        Create.Index("ix_mail_recipient").OnTable("mail").OnColumn("recipient_name");
        Create.Index("ix_mail_sender").OnTable("mail").OnColumn("sender_name");
    }
}
