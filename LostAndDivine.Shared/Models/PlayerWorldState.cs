namespace LostAndDivine.Shared.Models;

/// <summary>
/// Мировое состояние игрока: позиция, зона, пати, торговля, админ, дебафы, очередь скиллов, квесты.
/// Вынесено из Player.cs для уменьшения God-Class.
/// </summary>
public sealed class PlayerWorldState
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Facing { get; set; } = "down";
    public string CurrentZoneId { get; set; } = BalanceStatic.MainZoneId;
    public Guid? PartyId { get; set; }
    public bool IsTrading { get; set; }
    public bool IsAdmin { get; set; }

    public MovementState Movement { get; set; } = new();
    public CombatState Combat { get; set; } = new();
    public InteractionState Interaction { get; set; } = new();
    public DialogueState Dialogue { get; set; } = new();

    public List<ActiveDebuff> ActiveDebuffs { get; set; } = new();
    public object DebuffsLock { get; } = new();
    public List<ActiveDebuff> GetDebuffsSnapshot()
    {
        lock (DebuffsLock) return new List<ActiveDebuff>(ActiveDebuffs);
    }

    public System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> LastSkillUse { get; set; } = new();
    public List<string> QueuedSkillIds { get; set; } = new();
    public object QueuedSkillIdsLock { get; } = new();

    public List<QuestProgress> ActiveQuests { get; set; } = new();
    public List<string> CompletedQuestIds { get; set; } = new();
    public Dictionary<string, string> CompletedQuestTimes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
