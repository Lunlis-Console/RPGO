using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

/// <summary>
/// Реестр обработчиков сообщений. Заменяет большой switch в Program.Handlers.cs.
/// Диспетчеризация по message.Type через Dictionary.
/// </summary>
public static class MessageHandlerRegistry
{
    private static readonly Dictionary<string, IMessageHandler> _handlers = new();

    public static void Register(string type, IMessageHandler handler)
    {
        _handlers[type] = handler;
    }

    public static bool TryGet(string type, out IMessageHandler handler)
        => _handlers.TryGetValue(type, out handler!);

    public static void RegisterAll(GameWorld world, INetworkHub hub)
    {
        // Каждый хендлер получает GameWorld и сетевой хаб через конструктор.
        // Регистрируем по мере переноса из Program.Handlers.cs.
        Register("ping", new PingHandler(world, hub));
        Register("reconnect", new ReconnectHandler(world, hub));
        Register("say", new ChatHandler(world, hub));
        Register("status", new StatusHandler(world, hub));
        Register("move_direction", new MoveDirectionHandler(world, hub));
        Register("move_to", new MoveToHandler(world, hub));
        Register("inventory_request", new InventoryRequestHandler(world, hub));
        Register("equip", new EquipHandler(world, hub));
        Register("unequip", new UnequipHandler(world, hub));
        Register("use_item", new UseItemHandler(world, hub));
        Register("collect", new CollectHandler(world, hub));
        Register("inventory_sort", new InventorySortHandler(world, hub));
        Register("drop_item", new DropItemHandler(world, hub));
        Register("allocate_attribute", new AllocateAttributeHandler(world, hub));
        Register("quest_log_request", new QuestLogRequestHandler(world, hub));
        Register("take_quest", new TakeQuestHandler(world, hub));
        Register("hotbar_update", new HotbarUpdateHandler(world, hub));
        Register("complete_quest", new CompleteQuestHandler(world, hub));
        Register("abandon_quest", new AbandonQuestHandler(world, hub));
        Register("shop_request", new ShopRequestHandler(world, hub));
        Register("buy", new BuyHandler(world, hub));
        Register("sell", new SellHandler(world, hub));
        Register("sell_all_trophies", new SellAllTrophiesHandler(world, hub));
        Register("buyback", new BuybackHandler(world, hub));
        Register("attack", new AttackHandler(world, hub));
        Register("select_target", new SelectTargetHandler(world, hub));
        Register("cancel_target", new CancelTargetHandler(world, hub));
        Register("interact_target", new InteractTargetHandler(world, hub));
        Register("skills_request", new SkillsRequestHandler(world, hub));
        Register("use_skill", new UseSkillHandler(world, hub));
        Register("cancel_skill", new CancelSkillHandler(world, hub));
        Register("party_invite", new PartyHandler(world, hub));
        Register("party_accept", new PartyHandler(world, hub));
        Register("party_decline", new PartyHandler(world, hub));
        Register("party_leave", new PartyHandler(world, hub));
        Register("party_transfer", new PartyHandler(world, hub));
        Register("party_kick", new PartyHandler(world, hub));
        Register("loot_corpse", new LootCorpseHandler(world, hub));
        Register("take_loot", new TakeLootHandler(world, hub));
        Register("trade_request", new TradeRequestHandler(world, hub));
        Register("trade_accept", new TradeAcceptHandler(world, hub));
        Register("trade_decline", new TradeDeclineHandler(world, hub));
        Register("trade_offer", new TradeOfferHandler(world, hub));
        Register("trade_confirm", new TradeConfirmHandler(world, hub));
        Register("trade_cancel", new TradeCancelHandler(world, hub));
        Register("client_log", new ClientLogHandler(world, hub));
        Register("logout", new LogoutHandler(world, hub));
        Register("friend", new FriendHandler(world, hub));
        Register("revive", new ReviveHandler(world, hub));
    }
}
