namespace RPGGame.Shared.Network;

/// <summary>
/// Коды структурированных ошибок, отправляемых клиенту в сообщении типа "error".
/// Используются как контракт между сервером и клиентом.
/// </summary>
public static class ErrorCodes
{
    public const string ItemNotFound = "ITEM_NOT_FOUND";
    public const string InsufficientGold = "INSUFFICIENT_GOLD";
    public const string ItemNotInInventory = "ITEM_NOT_IN_INVENTORY";
    public const string ItemNotEquippable = "ITEM_NOT_EQUIPPABLE";
    public const string SlotEmpty = "SLOT_EMPTY";
    public const string NothingToCollect = "NOTHING_TO_COLLECT";
    public const string NotAtBoard = "NOT_AT_BOARD";
    public const string QuestNotSpecified = "QUEST_NOT_SPECIFIED";
    public const string QuestAlreadyTaken = "QUEST_ALREADY_TAKEN";
    public const string QuestNotFound = "QUEST_NOT_FOUND";
    public const string QuestNotActive = "QUEST_NOT_ACTIVE";
    public const string QuestNotCompleted = "QUEST_NOT_COMPLETED";
    public const string InventoryFull = "INVENTORY_FULL";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string NotInCombat = "NOT_IN_COMBAT";
    public const string TargetNotFound = "TARGET_NOT_FOUND";
    public const string TargetDead = "TARGET_DEAD";
    public const string NoFreeCell = "NO_FREE_CELL";
    public const string PathNotFound = "PATH_NOT_FOUND";
    public const string SkillNotFound = "SKILL_NOT_FOUND";
}
