namespace LostAndDivine.Shared.Network;

/// <summary>
/// Типобезопасный код ошибки (P2-2). Заменяет строковые "ITEM_NOT_FOUND".
/// Проводной формат остаётся строковым через ErrorCodeConverter.
/// </summary>
public enum ErrorCode
{
    Unknown = 0,
    ItemNotFound,
    InsufficientGold,
    ItemNotInInventory,
    ItemNotEquippable,
    ItemLevelTooLow,
    SlotEmpty,
    NothingToCollect,
    NotAtBoard,
    QuestNotSpecified,
    QuestAlreadyTaken,
    QuestNotFound,
    QuestNotActive,
    QuestNotCompleted,
    QuestNotAvailable,
    InventoryFull,
    InvalidRequest,
    NotInCombat,
    TargetNotFound,
    TargetDead,
    NoFreeCell,
    PathNotFound,
    SkillNotFound,
    NoSpace,
    InvalidParameter,
}

public static class ErrorCodeExtensions
{
    private static readonly Dictionary<ErrorCode, string> _toWire = new()
    {
        [ErrorCode.ItemNotFound] = ErrorCodes.ItemNotFound,
        [ErrorCode.InsufficientGold] = ErrorCodes.InsufficientGold,
        [ErrorCode.ItemNotInInventory] = ErrorCodes.ItemNotInInventory,
        [ErrorCode.ItemNotEquippable] = ErrorCodes.ItemNotEquippable,
        [ErrorCode.ItemLevelTooLow] = ErrorCodes.ItemLevelTooLow,
        [ErrorCode.SlotEmpty] = ErrorCodes.SlotEmpty,
        [ErrorCode.NothingToCollect] = ErrorCodes.NothingToCollect,
        [ErrorCode.NotAtBoard] = ErrorCodes.NotAtBoard,
        [ErrorCode.QuestNotSpecified] = ErrorCodes.QuestNotSpecified,
        [ErrorCode.QuestAlreadyTaken] = ErrorCodes.QuestAlreadyTaken,
        [ErrorCode.QuestNotFound] = ErrorCodes.QuestNotFound,
        [ErrorCode.QuestNotActive] = ErrorCodes.QuestNotActive,
        [ErrorCode.QuestNotCompleted] = ErrorCodes.QuestNotCompleted,
        [ErrorCode.QuestNotAvailable] = ErrorCodes.QuestNotAvailable,
        [ErrorCode.InventoryFull] = ErrorCodes.InventoryFull,
        [ErrorCode.InvalidRequest] = ErrorCodes.InvalidRequest,
        [ErrorCode.NotInCombat] = ErrorCodes.NotInCombat,
        [ErrorCode.TargetNotFound] = ErrorCodes.TargetNotFound,
        [ErrorCode.TargetDead] = ErrorCodes.TargetDead,
        [ErrorCode.NoFreeCell] = ErrorCodes.NoFreeCell,
        [ErrorCode.PathNotFound] = ErrorCodes.PathNotFound,
        [ErrorCode.SkillNotFound] = ErrorCodes.SkillNotFound,
        [ErrorCode.NoSpace] = ErrorCodes.NoSpace,
        [ErrorCode.InvalidParameter] = ErrorCodes.InvalidParameter,
    };
    private static readonly Dictionary<string, ErrorCode> _fromWire = _toWire.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    public static string ToWire(this ErrorCode code) => _toWire.TryGetValue(code, out var s) ? s : code.ToString().ToUpperInvariant();
    public static ErrorCode FromWire(string wire) => _fromWire.TryGetValue(wire, out var c) ? c : ErrorCode.Unknown;
}
