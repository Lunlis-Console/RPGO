namespace LostAndDivine.Shared.Models;

public class QuestDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "kill";           // kill / collect / talk / travel / use / explore
    [System.Text.Json.Serialization.JsonIgnore]
    public QuestType TypeEnum { get => QuestTypeExtensions.Parse(Type); set => Type = value.ToDisplayString(); }
    public string TargetMonsterId { get; set; } = "";   // M0001...
    public string TargetItemId { get; set; } = "";       // I0015...
    public string TargetNpcId { get; set; } = "";        // N0001... (для talk/travel-квестов)
    public string GiverNpcId { get; set; } = "";         // N0001... (NPC, выдающий квест)
    public string TargetZoneId { get; set; } = "";       // Цель-зона (explore) или зона авто-выдачи
    public int TargetX { get; set; }                      // Точка на карте (travel)
    public int TargetY { get; set; }
    public bool AutoGrant { get; set; }                   // Выдавать автоматически при входе в зону
    public int Target { get; set; }

    // Мульти-цели: основной источник условий. Если список пуст — используется
    // legacy-поля (Type/Target*/Target), из которых цели выводятся автоматически.
    public List<QuestObjective> Objectives { get; set; } = new();

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

public class QuestObjective
{
    public string Type { get; set; } = "kill";            // kill / collect / talk / travel / use / explore
    [System.Text.Json.Serialization.JsonIgnore]
    public QuestType TypeEnum { get => QuestTypeExtensions.Parse(Type); set => Type = value.ToDisplayString(); }
    public string Target { get; set; } = "";              // Монстр / предмет / NPC / зона
    public int TargetX { get; set; }                      // Точка на карте (travel)
    public int TargetY { get; set; }
    // Инвариант (P2-2): цель требует хотя бы 1 выполнение.
    private int _count = 1;
    public int Count
    {
        get => _count;
        set => _count = Math.Max(1, value);
    }

    /// <summary>
    /// Стадия цели: цели одной стадии выполняются параллельно, цель стадии N
    /// открывается только после выполнения всех целей предыдущих стадий
    /// (0 = открыта сразу, как и раньше).
    /// </summary>
    public int Stage { get; set; }

    /// <summary>Текущий прогресс (не хранится в БД-определении квеста, живёт в прогресс-записи игрока).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Current { get; set; }
}

public class QuestProgress
{
    public string QuestId { get; set; } = "";
    /// <summary>Текущий прогресс по каждой цели (индекс совпадает с Objectives).</summary>
    public List<int> Currents { get; set; } = new();
    public bool Completed { get; set; }

    /// <summary>Удобный доступ к прогрессу первой цели (legacy-квесты с одной целью).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Current
    {
        get => Currents.Count > 0 ? Currents[0] : 0;
        set
        {
            if (Currents.Count == 0) Currents.Add(0);
            Currents[0] = value;
        }
    }
}

public class QuestBoardPosition
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Name { get; set; } = "Доска заданий";
    public string? QuestIndicator { get; set; }
}
