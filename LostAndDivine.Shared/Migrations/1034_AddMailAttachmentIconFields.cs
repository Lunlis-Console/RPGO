using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Поля для корректного отображения иконки вложения письма:
/// weapon_subtype (тип оружия), heal_amount/restore_mana (зелья).
/// Клиент использует их в SpriteCache.ForItem вместо generic-иконки.
/// </summary>
[Migration(1034)]
public class AddMailAttachmentIconFields : ForwardOnlyMigration
{
    public override void Up()
    {
        Alter.Table("mail_attachments")
            .AddColumn("weapon_subtype").AsString(64).NotNullable().WithDefaultValue("")
            .AddColumn("heal_amount").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("restore_mana").AsInt32().NotNullable().WithDefaultValue(0);
    }
}
