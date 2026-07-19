using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

/// <summary>
/// Базовый класс хендлеров: даёт доступ к GameWorld и сетевому хабу,
/// чтобы переносимые обработчики не дублировали вызовы отправки.
/// </summary>
public abstract class BaseHandler : IMessageHandler
{
    protected GameWorld World { get; }
    protected INetworkHub Hub { get; }

    protected BaseHandler(GameWorld world, INetworkHub hub)
    {
        World = world;
        Hub = hub;
    }

    public abstract Task Handle(ClientConnection connection, GameMessage message, Player? player);

    protected Task SendToClient(ClientConnection connection, GameMessage message)
        => Hub.SendToClient(connection, message);

    protected Task BroadcastMapAsync()
        => Hub.BroadcastMapAsync();

    protected Task BroadcastChatAsync(string name, string text)
        => Hub.BroadcastChatAsync(name, text);

    protected Task ReloadContent(ClientConnection? connection = null)
        => Program.ReloadContent(connection);

    protected int GetAttackSpeed(Player player)
        => Program.GetAttackSpeed(player);

    protected StatsBreakdown BuildBreakdown(Player player)
        => Hub.BuildBreakdown(player);

    protected Task SendInventoryAndStatus(ClientConnection connection, Player player)
        => Hub.SendInventoryAndStatus(connection, player);

    protected Task SendQuestLog(ClientConnection connection, Player player)
        => Hub.SendQuestLog(connection, player);

    protected Task ProcessPendingInteraction(Player player, string interactionType)
        => Program.ProcessPendingInteraction(player, interactionType);

    protected Task SendError(ClientConnection connection, string code, string message)
        => Hub.SendError(connection, code, message);
}
