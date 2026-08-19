using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Последовательные стадии целей квестов: поле stage в quests_def.objectives.
/// Q0007 «Волчья стая»: сначала убить 5 волков, затем рассказать старосте.
/// Q0004 «Змеиная угроза»: сначала поговорить со старостой, затем убить 3 змей.
/// Q0011 удаляется — это дубль Q0007, нигде не выдаётся (нет на доске и в диалогах).
/// </summary>
[Migration(1061)]
public class SequentialQuestStages : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql(@"UPDATE quests_def SET objectives =
            '[{""type"":""kill"",""target"":""M0006"",""count"":5,""stage"":0},{""type"":""talk"",""target"":""N0003"",""count"":1,""stage"":1}]'
            WHERE id = 'Q0007'");

        Execute.Sql(@"UPDATE quests_def SET objectives =
            '[{""type"":""talk"",""target"":""N0003"",""count"":1,""stage"":0},{""type"":""kill"",""target"":""M0013"",""count"":3,""stage"":1}]'
            WHERE id = 'Q0004'");

        Execute.Sql("DELETE FROM quests_def WHERE id = 'Q0011'");
        Execute.Sql("DELETE FROM quests WHERE quest_id = 'Q0011'");
        Execute.Sql("DELETE FROM player_completed_quests WHERE quest_id = 'Q0011'");
    }
}
