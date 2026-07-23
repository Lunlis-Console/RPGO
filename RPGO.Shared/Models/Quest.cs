namespace RPGGame.Shared.Models;

public class QuestDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "kill";
    public string TargetMonsterId { get; set; } = "";   // M0001...
    public string TargetItemId { get; set; } = "";       // I0015...
    public string TargetNpcId { get; set; } = "";        // N0001... (для talk-квестов)
    public int Target { get; set; }
    public int XpReward { get; set; }
    public int GoldReward { get; set; }
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
}
