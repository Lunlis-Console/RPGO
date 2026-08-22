using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Реестр обработчиков сообщений. Заменяет большой switch в Program.Handlers.cs.
/// Диспетчеризация по message.Type через Dictionary.
/// </summary>
public class MessageHandlerRegistry
{
    private readonly Dictionary<GameMessageType, IMessageHandler> _handlers = new();

    public void Register(GameMessageType type, IMessageHandler handler)
    {
        _handlers[type] = handler;
    }

    public bool TryGet(GameMessageType type, out IMessageHandler handler)
        => _handlers.TryGetValue(type, out handler!);

    public void RegisterAll(GameServices svc)
    {
        Register(GameMessageType.Ping, new PingHandler(svc));
        Register(GameMessageType.Pong, new PongHandler(svc));
        Register(GameMessageType.Reconnect, new ReconnectHandler(svc));
        Register(GameMessageType.Say, new ChatHandler(svc));
        Register(GameMessageType.Status, new StatusHandler(svc));
        Register(GameMessageType.MoveTo, new MoveToHandler(svc));
        Register(GameMessageType.InventoryRequest, new InventoryRequestHandler(svc));
        Register(GameMessageType.Equip, new EquipHandler(svc));
        Register(GameMessageType.Unequip, new UnequipHandler(svc));
        Register(GameMessageType.UnequipAll, new UnequipAllHandler(svc));
        Register(GameMessageType.InstanceListRequest, new InstanceListRequestHandler(svc));
        Register(GameMessageType.InstanceEnterSolo, new InstanceEnterSoloHandler(svc));
        Register(GameMessageType.InstanceInvite, new InstanceInviteHandler(svc));
        Register(GameMessageType.InstanceInviteResponse, new InstanceInviteResponseHandler(svc));
        Register(GameMessageType.InstanceStart, new InstanceStartHandler(svc));
        Register(GameMessageType.UseItem, new UseItemHandler(svc));
        Register(GameMessageType.Collect, new CollectHandler(svc));
        Register(GameMessageType.InventorySort, new InventorySortHandler(svc));
        Register(GameMessageType.DropItem, new DropItemHandler(svc));
        Register(GameMessageType.AllocateAttribute, new AllocateAttributeHandler(svc));
        Register(GameMessageType.AllocateSkill, new AllocateSkillHandler(svc));
        Register(GameMessageType.ResetSkills, new ResetSkillsHandler(svc));
        Register(GameMessageType.ResetAttributes, new ResetAttributesHandler(svc));
        Register(GameMessageType.QuestLogRequest, new QuestLogRequestHandler(svc));
        Register(GameMessageType.TakeQuest, new TakeQuestHandler(svc));
        Register(GameMessageType.HotbarUpdate, new HotbarUpdateHandler(svc));
        Register(GameMessageType.CompleteQuest, new CompleteQuestHandler(svc));
        Register(GameMessageType.AbandonQuest, new AbandonQuestHandler(svc));
        Register(GameMessageType.ShopRequest, new ShopRequestHandler(svc));
        Register(GameMessageType.Buy, new BuyHandler(svc));
        Register(GameMessageType.Sell, new SellHandler(svc));
        Register(GameMessageType.SellAllTrophies, new SellAllTrophiesHandler(svc));
        Register(GameMessageType.Buyback, new BuybackHandler(svc));
        Register(GameMessageType.Attack, new AttackHandler(svc));
        Register(GameMessageType.SelectTarget, new SelectTargetHandler(svc));
        Register(GameMessageType.CancelTarget, new CancelTargetHandler(svc));
        Register(GameMessageType.InteractTarget, new InteractTargetHandler(svc));
        Register(GameMessageType.SkillsRequest, new SkillsRequestHandler(svc));
        Register(GameMessageType.UseSkill, new UseSkillHandler(svc));
        Register(GameMessageType.CancelSkill, new CancelSkillHandler(svc));
        Register(GameMessageType.PartyInvite, new PartyHandler(svc));
        Register(GameMessageType.PartyAccept, new PartyHandler(svc));
        Register(GameMessageType.PartyDecline, new PartyHandler(svc));
        Register(GameMessageType.PartyLeave, new PartyHandler(svc));
        Register(GameMessageType.PartyTransfer, new PartyHandler(svc));
        Register(GameMessageType.PartyKick, new PartyHandler(svc));
        Register(GameMessageType.LootCorpse, new LootCorpseHandler(svc));
        Register(GameMessageType.TakeLoot, new TakeLootHandler(svc));
        Register(GameMessageType.TakeChestLoot, new TakeChestLootHandler(svc));
        Register(GameMessageType.TradeRequest, new TradeRequestHandler(svc));
        Register(GameMessageType.TradeAccept, new TradeAcceptHandler(svc));
        Register(GameMessageType.TradeDecline, new TradeDeclineHandler(svc));
        Register(GameMessageType.TradeOffer, new TradeOfferHandler(svc));
        Register(GameMessageType.TradeConfirm, new TradeConfirmHandler(svc));
        Register(GameMessageType.TradeCancel, new TradeCancelHandler(svc));
        Register(GameMessageType.ClientLog, new ClientLogHandler(svc));
        Register(GameMessageType.Logout, new LogoutHandler(svc));
        Register(GameMessageType.Friend, new FriendHandler(svc));
        Register(GameMessageType.Revive, new ReviveHandler(svc));
        Register(GameMessageType.DialogueChoice, new DialogueChoiceHandler(svc));
        Register(GameMessageType.Mail, new MailHandler(svc));
        Register(GameMessageType.PlayerFacing, new PlayerFacingHandler(svc));
        Register(GameMessageType.StorageOpen, new StorageOpenHandler(svc));
        Register(GameMessageType.StorageDeposit, new StorageDepositHandler(svc));
        Register(GameMessageType.StorageWithdraw, new StorageWithdrawHandler(svc));
        Register(GameMessageType.Inspect, new InspectHandler(svc));
        Register(GameMessageType.TileRequest, new TileRequestHandler(svc));
        Register(GameMessageType.SectorRequest, new SectorRequestHandler(svc));
        Register(GameMessageType.LeaveInstance, new LeaveInstanceHandler(svc));
        Register(GameMessageType.UpgradeItem, new UpgradeItemHandler(svc));
    }
}
