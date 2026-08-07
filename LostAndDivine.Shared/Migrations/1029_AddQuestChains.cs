using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1029)]
public class AddQuestChains : ForwardOnlyMigration
{
    public override void Up()
    {
        // Поля сюжетных цепочек для квестов
        Alter.Table("quests_def")
            .AddColumn("chain_id").AsString().WithDefaultValue("")
            .AddColumn("step").AsInt32().WithDefaultValue(0)
            .AddColumn("prerequisite_quest_id").AsString().WithDefaultValue("")
            .AddColumn("min_level").AsInt32().WithDefaultValue(1)
            .AddColumn("item_reward_id").AsString().WithDefaultValue("")
            .AddColumn("item_reward_count").AsInt32().WithDefaultValue(0);

        // История выполненных квестов игрока
        Create.Table("player_completed_quests")
            .WithColumn("player_name").AsString().NotNullable()
            .WithColumn("quest_id").AsString().NotNullable()
            .WithColumn("completed_at").AsString().NotNullable();
        Create.Index("ix_completed_quests_player")
            .OnTable("player_completed_quests")
            .OnColumn("player_name");

        // Демо-цепочка: Q0009 (старт) -> Q0010 (только после сдачи Q0009)
        Execute.Sql("INSERT OR IGNORE INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target, xp_reward, gold_reward, chain_id, step, prerequisite_quest_id, min_level) " +
            "VALUES ('Q0009','Первая охота','Убейте 3 крысы у окраины деревни.','kill','M0001','','',3,40,25,'STORY_1',1,'',1)");
        Execute.Sql("INSERT OR IGNORE INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target, xp_reward, gold_reward, chain_id, step, prerequisite_quest_id, min_level) " +
            "VALUES ('Q0010','Настоящий охотник','Стая волков вышла к деревне. Убейте 3 волков.','kill','M0006','','',3,80,50,'STORY_1',2,'Q0009',2)");
    }
}
