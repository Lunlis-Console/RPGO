using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1027)]
public class UpdateArenaZone : ForwardOnlyMigration
{
    public override void Up()
    {
        // Зона arena теперь рендерится из Tiled-карты arena_1.tmj (50x50, 64px).
        // Размер и точка спавна берутся из карты, чтобы игрок не появлялся в стене.
        Update.Table("zones")
            .Set(new { width = 50, height = 50, spawn_x = 25, spawn_y = 25 })
            .Where(new { id = "arena" });

        // Портал main -> arena ведёт на свободную клетку арены (не в колонну).
        Update.Table("world_portals")
            .Set(new { to_x = 25, to_y = 24 })
            .Where(new { id = "p_main_arena" });

        // Обратный портал arena -> main тоже стоит на свободной клетке.
        Update.Table("world_portals")
            .Set(new { from_x = 25, from_y = 24 })
            .Where(new { id = "p_arena_main" });
    }
}
