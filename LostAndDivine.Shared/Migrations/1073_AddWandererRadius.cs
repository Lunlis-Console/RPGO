using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1073)]
public class AddWandererRadius : ForwardOnlyMigration
{
    public override void Up()
    {
        // Радиус блуждания для NPC типа "wanderer": насколько клеток от точки
        // спавна он может отходить. 0 — использовать глобальный Balance.WandererWanderRadius.
        Alter.Table("npcs")
            .AddColumn("wander_radius").AsInt32().WithDefaultValue(0);
    }
}
