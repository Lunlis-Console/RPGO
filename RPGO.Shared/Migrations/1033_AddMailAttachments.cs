using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Множественные вложения для писем: отдельная таблица mail_attachments.
/// Старые столбцы item_id/item_name/item_type/item_quantity в mail сохраняются
/// (для совместимости), но больше не используются; существующие одиночные
/// вложения переносятся в новую таблицу.
/// </summary>
[Migration(1033)]
public class AddMailAttachments : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("mail_attachments")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("mail_id").AsInt32().NotNullable()
            .WithColumn("template_id").AsString(200).NotNullable().WithDefaultValue("")
            .WithColumn("name").AsString(200).NotNullable().WithDefaultValue("")
            .WithColumn("type").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("quantity").AsInt32().NotNullable().WithDefaultValue(1);

        Create.Index("ix_mail_attachments_mail").OnTable("mail_attachments").OnColumn("mail_id");

        Execute.Sql(@"INSERT INTO mail_attachments (mail_id, template_id, name, type, quantity)
            SELECT id, item_id, item_name, item_type, item_quantity
            FROM mail WHERE item_id <> '' AND item_quantity > 0");
    }
}
