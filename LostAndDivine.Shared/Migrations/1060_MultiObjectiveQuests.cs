using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Мульти-цели квестов: quests_def.objectives (JSON-список целей) и quests.progress
/// (JSON-список счётчиков по целям). Существующие квесты переносятся в новый формат
/// из legacy-колонок (type + target_* + target), прогресс игроков — из current.
/// Дополнительно засевается первый мульти-целевой квест Q0011.
/// </summary>
[Migration(1060)]
public class MultiObjectiveQuests : ForwardOnlyMigration
{
    public override void Up()
    {
        Alter.Table("quests_def").AddColumn("objectives").AsString().WithDefaultValue("[]").NotNullable();
        Alter.Table("quests").AddColumn("progress").AsString().WithDefaultValue("[]").NotNullable();

        // Бэкфилл objectives для legacy-квестов (у кого пусто).
        // Legacy travel без target_npc_id хранит координаты в target_x/target_y —
        // такие квесты не встречаются в текущем контенте, цели с координатами
        // создаются через колонку objectives напрямую.
        Execute.Sql(@"UPDATE quests_def SET objectives =
            '[{""type"":""' || type || '"",""target"":""' ||
            COALESCE(CASE type
                WHEN 'kill' THEN target_monster_id
                WHEN 'collect' THEN target_item_id
                WHEN 'use' THEN target_item_id
                WHEN 'talk' THEN target_npc_id
                WHEN 'travel' THEN target_npc_id
                WHEN 'explore' THEN target_zone_id
            END, '') ||
            '"",""count"":' || MAX(1, target) || '}]'
            WHERE objectives IS NULL OR objectives = '' OR objectives = '[]'");

        // Бэкфилл progress для существующих записей игроков (legacy — одна цель)
        Execute.Sql("UPDATE quests SET progress = json_array(current) WHERE progress IS NULL OR progress = '' OR progress = '[]'");

        // Сид первого мульти-целевого квеста: убить волков и собрать клыки
        Execute.Sql(@"INSERT OR IGNORE INTO quests_def
            (id, title, description, type, target, xp_reward, gold_reward, min_level,
             giver_npc_id, is_story, repeatable, objectives, item_reward_id, item_reward_count)
            VALUES ('Q0011', 'Охота на волков', 'Волки зачастили к дорогам. Убей волков и принеси их клыки старосте.',
                    'kill', 1, 80, 50, 2, 'N0003', 0, 0,
                    '[{""type"":""kill"",""target"":""M0006"",""count"":5},{""type"":""collect"",""target"":""T0002"",""count"":3}]',
                    'T0002', 3)");
    }
}