using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1003)]
public class FixManaPotionRestoreMana : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("UPDATE items SET restore_mana = 40 WHERE id = 'I0020'");
    }
}