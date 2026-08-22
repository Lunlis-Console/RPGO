using System.Text.Json;
using System.Text.Json.Serialization;

namespace LostAndDivine.Shared.Network;

public enum GameMessageType
{
    Unknown = 0,
    AbandonQuest,
    AllocateAttribute,
    AllocateSkill,
    Attack,
    AttackCooldown,
    AuthResponse,
    BoardOpen,
    Buy,
    Buyback,
    CancelSkill,
    CancelTarget,
    Changelog,
    CharacterCreate,
    CharacterDelete,
    CharacterList,
    CharacterSelect,
    Chat,
    ClientLog,
    Collect,
    CombatState,
    CombatUpdate,
    CompleteQuest,
    Damage,
    DialogueChoice,
    DialogueClose,
    DialogueOpen,
    DropItem,
    EnhancementOpen,
    EntityState,
    Equip,
    EquipmentResponse,
    Error,
    Friend,
    FriendList,
    FriendResult,
    Heal,
    HotbarResponse,
    HotbarUpdate,
    Inspect,
    InspectResponse,
    InstanceEnterSolo,
    InstanceInvite,
    InstanceInviteReceived,
    InstanceInviteResponse,
    InstanceInviteUpdate,
    InstanceList,
    InstanceListRequest,
    InstanceStart,
    InstanceStarted,
    InstanceWindowOpen,
    InteractTarget,
    InventoryRequest,
    InventoryResponse,
    InventorySort,
    Kick,
    LeaveInstance,
    LoginAuth,
    Logout,
    LootCorpse,
    Mail,
    MailDetail,
    MailList,
    MailResult,
    MailUnread,
    ManaRegen,
    MapUpdate,
    MoveTo,
    OpenBoard,
    PartyAccept,
    PartyDecline,
    PartyDisbanded,
    PartyInvite,
    PartyInviteDeclined,
    PartyInviteReceived,
    PartyInviteSent,
    PartyKick,
    PartyLeave,
    PartyTransfer,
    PartyUpdate,
    Ping,
    PlayerAttack,
    PlayerDeath,
    PlayerFacing,
    PlayerHp,
    PlayerMove,
    Pong,
    ProjectileHit,
    ProjectileSpawn,
    QuestLog,
    QuestLogRequest,
    QuestUpdate,
    Reconnect,
    ReconnectFail,
    ReconnectOk,
    Register,
    ResetAttributes,
    ResetSkills,
    Revive,
    Say,
    SectorData,
    SectorRequest,
    SectorsReloaded,
    SelectTarget,
    Sell,
    SellAllTrophies,
    ShopRequest,
    ShopResponse,
    ShopUpdate,
    SkillCooldown,
    SkillQueue,
    SkillsRequest,
    SkillsResponse,
    SpellResponse,
    Status,
    StatusResponse,
    StorageDeposit,
    StorageOpen,
    StorageUpdate,
    StorageWithdraw,
    TakeChestLoot,
    TakeLoot,
    TakeQuest,
    TargetCleared,
    TargetDebuffUpdate,
    TileRequest,
    TradeAccept,
    TradeCancel,
    TradeClose,
    TradeComplete,
    TradeConfirm,
    TradeConfirmUpdate,
    TradeDecline,
    TradeDeclined,
    TradeOffer,
    TradeOfferUpdate,
    TradeOpen,
    TradeRequest,
    TradeRequestReceived,
    TradeRequestSent,
    Unequip,
    UnequipAll,
    UpdateCheck,
    UpdateFile,
    UpdateFileChunk,
    UpdateFileMissing,
    UpdateInfo,
    UpgradeItem,
    UseItem,
    UseSkill,
    Welcome,
    ZoneTransition,
}

public sealed class GameMessageTypeJsonConverter : JsonConverter<GameMessageType>
{
    private static readonly Dictionary<string, GameMessageType> _fromWire;
    private static readonly Dictionary<GameMessageType, string> _toWire;

    static GameMessageTypeJsonConverter()
    {
        _fromWire = new Dictionary<string, GameMessageType>();
        _toWire = new Dictionary<GameMessageType, string>();
        foreach (GameMessageType value in Enum.GetValues<GameMessageType>())
        {
            if (value == GameMessageType.Unknown) continue;
            string wire = ToSnake(value.ToString());
            _fromWire[wire] = value;
            _toWire[value] = wire;
        }
    }

    private static string ToSnake(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
        {
            if (char.IsUpper(c))
            {
                if (sb.Length > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static GameMessageType FromWire(string wire)
        => _fromWire.TryGetValue(wire, out var value) ? value : GameMessageType.Unknown;

    public static string ToWire(GameMessageType value)
        => _toWire.TryGetValue(value, out var wire) ? wire : ToSnake(value.ToString());

    public override GameMessageType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? wire = reader.GetString();
        if (wire != null && _fromWire.TryGetValue(wire, out var value))
            return value;
        return GameMessageType.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, GameMessageType value, JsonSerializerOptions options)
    {
        if (_toWire.TryGetValue(value, out var wire))
            writer.WriteStringValue(wire);
        else
            writer.WriteStringValue(ToSnake(value.ToString()));
    }
}
