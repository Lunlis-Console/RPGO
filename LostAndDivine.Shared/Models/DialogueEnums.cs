using System.Text.Json.Serialization;

namespace LostAndDivine.Shared.Models;

public enum DialogueActionType
{
    Unknown = 0,
    AcceptQuest = 1,
    CompleteQuest = 2,
    OpenShop = 3,
    Close = 4,
    EnterInstance = 5,
    OpenInstances = 6,
    GiveItem = 7,
    GiveGold = 8,
    TakeItem = 9,
    Teleport = 10
}

public static class DialogueActionTypeExtensions
{
    public static DialogueActionType Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DialogueActionType.Unknown;
        string t = s.Trim().ToLowerInvariant();
        if (t.StartsWith("accept_quest:")) return DialogueActionType.AcceptQuest;
        if (t.StartsWith("complete_quest:")) return DialogueActionType.CompleteQuest;
        if (t == "open_shop") return DialogueActionType.OpenShop;
        if (t == "close") return DialogueActionType.Close;
        if (t.StartsWith("enter_instance:")) return DialogueActionType.EnterInstance;
        if (t == "open_instances") return DialogueActionType.OpenInstances;
        if (t.StartsWith("give_item:")) return DialogueActionType.GiveItem;
        if (t.StartsWith("give_gold:")) return DialogueActionType.GiveGold;
        if (t.StartsWith("take_item:")) return DialogueActionType.TakeItem;
        if (t.StartsWith("teleport:")) return DialogueActionType.Teleport;
        return DialogueActionType.Unknown;
    }
    public static string GetParam(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        int idx = s.IndexOf(':');
        return idx >= 0 && idx + 1 < s.Length ? s[(idx + 1)..] : "";
    }
}

public enum DialogueConditionType
{
    Unknown = 0,
    QuestActive = 1,
    QuestComplete = 2,
    QuestNotActive = 3,
    QuestNotStarted = 4,
    QuestReady = 5,
    HasItem = 6,
    Level = 7,
    Gold = 8
}

public static class DialogueConditionTypeExtensions
{
    public static DialogueConditionType Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DialogueConditionType.Unknown;
        string t = s.Trim().ToLowerInvariant();
        if (t.StartsWith("quest_active:")) return DialogueConditionType.QuestActive;
        if (t.StartsWith("quest_complete:")) return DialogueConditionType.QuestComplete;
        if (t.StartsWith("quest_not_active:")) return DialogueConditionType.QuestNotActive;
        if (t.StartsWith("quest_not_started:")) return DialogueConditionType.QuestNotStarted;
        if (t.StartsWith("quest_ready:")) return DialogueConditionType.QuestReady;
        if (t.StartsWith("has_item:")) return DialogueConditionType.HasItem;
        if (t.StartsWith("level:")) return DialogueConditionType.Level;
        if (t.StartsWith("gold:")) return DialogueConditionType.Gold;
        return DialogueConditionType.Unknown;
    }
    public static string GetParam(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        int idx = s.IndexOf(':');
        return idx >= 0 && idx + 1 < s.Length ? s[(idx + 1)..] : "";
    }
}
