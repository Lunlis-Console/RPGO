using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1046)]
public class SeedDungeonTiersDialogue : ForwardOnlyMigration
{
    public override void Up()
    {
        // Update guard dialogue with 10 tier choices
        var dialogue = @"{""greeting"":{""speaker"":""Страж подземелья"",""text"":""Приветствую, путник. Я охраняю вход в подземелье. Какое испытание ты хочешь пройти?"",""choices"":[{""text"":""Подземелье (ур. 1-5)"",""action"":""enter_instance:trial_dungeon_1""},{""text"":""Подземелье (ур. 6-10)"",""action"":""enter_instance:trial_dungeon_6""},{""text"":""Подземелье (ур. 11-15)"",""action"":""enter_instance:trial_dungeon_11""},{""text"":""Подземелье (ур. 16-20)"",""action"":""enter_instance:trial_dungeon_16""},{""text"":""Подземелье (ур. 21-25)"",""action"":""enter_instance:trial_dungeon_21""},{""text"":""Подземелье (ур. 26-30)"",""action"":""enter_instance:trial_dungeon_26""},{""text"":""Подземелье (ур. 31-35)"",""action"":""enter_instance:trial_dungeon_31""},{""text"":""Подземелье (ур. 36-40)"",""action"":""enter_instance:trial_dungeon_36""},{""text"":""Подземелье (ур. 41-45)"",""action"":""enter_instance:trial_dungeon_41""},{""text"":""Подземелье (ур. 46-50)"",""action"":""enter_instance:trial_dungeon_46""},{""text"":""Я передумал."",""action"":""close""}]}}";
        Execute.Sql($"UPDATE npcs SET data = '{dialogue.Replace("'", "''")}' WHERE id = 'N0010'");

        // Clean old single-tier template
        Execute.Sql("DELETE FROM instance_portals WHERE instance_template_id = 'trial_dungeon'");
        Execute.Sql("DELETE FROM instance_spawns WHERE instance_template_id = 'trial_dungeon'");
        Execute.Sql("DELETE FROM instance_templates WHERE id = 'trial_dungeon'");
        Execute.Sql("DELETE FROM instance_portals WHERE from_x = 48 AND from_y = 55");

        // Create 10 tier templates (level brackets 1-5, 6-10, ... 46-50)
        int[] levels = { 1, 6, 11, 16, 21, 26, 31, 36, 41, 46 };
        foreach (int lvl in levels)
        {
            string tid = $"trial_dungeon_{lvl}";
            // Remove existing tier entry if it was seeded by a previous migration
            Execute.Sql($"DELETE FROM instance_portals WHERE instance_template_id = '{tid}'");
            Execute.Sql($"DELETE FROM instance_templates WHERE id = '{tid}'");

            Insert.IntoTable("instance_templates").Row(new
            {
                id = tid,
                name = $"Подземелье (ур. {lvl}-{lvl + 4})",
                zone_id = "main",
                time_limit_seconds = 600,
                spawn_x = 1, spawn_y = 1,
                boss_monster_id = "M0020",
                chest_x = 2, chest_y = 18,
                exit_x = 2, exit_y = 19,
                corridor_length = 20, corridor_width = 5
            });
            Insert.IntoTable("instance_portals").Row(new
            {
                id = $"portal_dungeon_{lvl}",
                instance_template_id = tid,
                from_zone = "main", from_x = 48, from_y = 55
            });
        }
    }
}
