namespace LostAndDivine.Shared.Models;

public class QuestDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "kill";           // kill / collect / talk / travel / use
    public string TargetMonsterId { get; set; } = "";   // M0001...
    public string TargetItemId { get; set; } = "";       // I0015...
    public string TargetNpcId { get; set; } = "";        // N0001... (для talk/travel-квестов)
    public string GiverNpcId { get; set; } = "";         // N0001... (NPC, выдающий квест)
    public string TargetZoneId { get; set; } = "";       // Цель-зона (explore) или зона авто-выдачи
    public int TargetX { get; set; }                      // Точка на карте (travel)
    public int TargetY { get; set; }
    public bool AutoGrant { get; set; }                   // Выдавать автоматически при входе в зону
    public int Target { get; set; }

    // Сюжетная цепочка
    public string ChainId { get; set; } = "";            // Идентификатор цепочки (напр. "STORY_1")
    public int Step { get; set; }                         // Порядок звена в цепочке
    public string PrerequisiteQuestId { get; set; } = ""; // Какой квест должен быть выполнен перед этим
    public int MinLevel { get; set; }                     // Минимальный уровень для взятия

    public int XpReward { get; set; }
    public int GoldReward { get; set; }
    public string ItemRewardId { get; set; } = "";        // Предмет-награда
    public int ItemRewardCount { get; set; }

    public bool IsStory { get; set; }                     // Сюжетный квест (флаг из редактора)
    public bool Repeatable { get; set; }                  // Повторяемый: после сдачи можно взять снова
    public string Location { get; set; } = "";            // Локация (из редактора)
}

public class QuestProgress
{
    public string QuestId { get; set; } = "";
    public int Current { get; set; }
    public bool Completed { get; set; }
}

public class QuestBoardPosition
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Name { get; set; } = "Доска заданий";
    public string? QuestIndicator { get; set; }
}
