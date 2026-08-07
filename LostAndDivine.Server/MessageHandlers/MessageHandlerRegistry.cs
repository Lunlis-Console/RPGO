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
    private readonly Dictionary<string, IMessageHandler> _handlers = new();

    public void Register(string type, IMessageHandler handler)
    {
        _handlers[type] = handler;
    }

    public bool TryGet(string type, out IMessageHandler handler)
        => _handlers.TryGetValue(type, out handler!);

    public void RegisterAll(GameServices svc)
    {
        Register("ping", new PingHandler(svc));
        Register("reconnect", new ReconnectHandler(svc));
        Register("say", new ChatHandler(svc));
        Register("status", new StatusHandler(svc));
        Register("move_to", new MoveToHandler(svc));
        Register("inventory_request", new InventoryRequestHandler(svc));
        Register("equip", new EquipHandler(svc));
        Register("unequip", new UnequipHandler(svc));
        Register("use_item", new UseItemHandler(svc));
        Register("collect", new CollectHandler(svc));
        Register("inventory_sort", new InventorySortHandler(svc));
        Register("drop_item", new DropItemHandler(svc));
        Register("allocate_attribute", new AllocateAttributeHandler(svc));
        Register("allocate_skill", new AllocateSkillHandler(svc));
        Register("reset_skills", new ResetSkillsHandler(svc));
        Register("reset_attributes", new ResetAttributesHandler(svc));
        Register("quest_log_request", new QuestLogRequestHandler(svc));
        Register("take_quest", new TakeQuestHandler(svc));
        Register("hotbar_update", new HotbarUpdateHandler(svc));
        Register("complete_quest", new CompleteQuestHandler(svc));
        Register("abandon_quest", new AbandonQuestHandler(svc));
        Register("shop_request", new ShopRequestHandler(svc));
        Register("buy", new BuyHandler(svc));
        Register("sell", new SellHandler(svc));
        Register("sell_all_trophies", new SellAllTrophiesHandler(svc));
        Register("buyback", new BuybackHandler(svc));
        Register("attack", new AttackHandler(svc));
        Register("select_target", new SelectTargetHandler(svc));
        Register("cancel_target", new CancelTargetHandler(svc));
        Register("interact_target", new InteractTargetHandler(svc));
        Register("skills_request", new SkillsRequestHandler(svc));
        Register("use_skill", new UseSkillHandler(svc));
        Register("cancel_skill", new CancelSkillHandler(svc));
        Register("party_invite", new PartyHandler(svc));
        Register("party_accept", new PartyHandler(svc));
        Register("party_decline", new PartyHandler(svc));
        Register("party_leave", new PartyHandler(svc));
        Register("party_transfer", new PartyHandler(svc));
        Register("party_kick", new PartyHandler(svc));
        Register("loot_corpse", new LootCorpseHandler(svc));
        Register("take_loot", new TakeLootHandler(svc));
        Register("take_chest_loot", new TakeChestLootHandler(svc));
        Register("trade_request", new TradeRequestHandler(svc));
        Register("trade_accept", new TradeAcceptHandler(svc));
        Register("trade_decline", new TradeDeclineHandler(svc));
        Register("trade_offer", new TradeOfferHandler(svc));
        Register("trade_confirm", new TradeConfirmHandler(svc));
        Register("trade_cancel", new TradeCancelHandler(svc));
        Register("client_log", new ClientLogHandler(svc));
        Register("logout", new LogoutHandler(svc));
        Register("friend", new FriendHandler(svc));
        Register("revive", new ReviveHandler(svc));
        Register("dialogue_choice", new DialogueChoiceHandler(svc));
        Register("mail", new MailHandler(svc));
        Register("player_facing", new PlayerFacingHandler(svc));
        Register("storage_open", new StorageOpenHandler(svc));
        Register("storage_deposit", new StorageDepositHandler(svc));
        Register("storage_withdraw", new StorageWithdrawHandler(svc));
        Register("tile_request", new TileRequestHandler(svc));
        Register("leave_instance", new LeaveInstanceHandler(svc));
    }
}
