using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1028)]
public class RemoveDbPortalsForArena : ForwardOnlyMigration
{
    public override void Up()
    {
        // Портал main -> arena и обратно теперь размещаются в Tiled-картах
        // (wordlmap.tmj / arena_1.tmj, слой «Порталы»), поэтому DB-записи не нужны.
        Delete.FromTable("world_portals").Row(new { id = "p_main_arena" });
        Delete.FromTable("world_portals").Row(new { id = "p_arena_main" });
    }
}
