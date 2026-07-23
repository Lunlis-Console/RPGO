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

    protected Task BroadcastChatAsync(ChatChannel channel, string from, string text)
        => Hub.BroadcastChatAsync(channel, from, text);

    protected Task SendChatToAsync(ClientConnection connection, ChatChannel channel, string from, string text, string? to = null)
        => Hub.SendChatToAsync(connection, channel, from, text, to);

    protected async Task SendChatLocalAsync(Player sender, ChatChannel channel, string from, string text)
    {
        int view = World.Map.ViewRadius;
        foreach (var p in World.GetPlayersSnapshot())
        {
            if (Math.Abs(p.X - sender.X) > view || Math.Abs(p.Y - sender.Y) > view) continue;
            var conn = World.FindClientByPlayer(p);
            if (conn != null) await SendChatToAsync(conn, channel, from, text);
        }
    }

    protected async Task SendChatPartyAsync(Player sender, string from, string text)
    {
        var party = Program.Services.Party.GetPartyForPlayer(sender.Id);
        var targets = new List<Player>();
        if (party != null)
        {
            foreach (var memberId in party.Members)
            {
                var pl = World.GetPlayersSnapshot().FirstOrDefault(x => x.Id == memberId);
                if (pl != null) targets.Add(pl);
            }
        }
        if (targets.Count == 0) targets.Add(sender);

        foreach (var pl in targets)
        {
            var conn = World.FindClientByPlayer(pl);
            if (conn != null) await SendChatToAsync(conn, ChatChannel.Party, from, text);
        }
    }

    protected async Task SendWhisperAsync(Player from, string toName, string text)
    {
        if (!World.TryGetPlayerByName(toName, out var target))
        {
            var self = World.FindClientByPlayer(from);
            if (self != null)
                await SendChatToAsync(self, ChatChannel.System, "Система",
                    $"Игрок «{toName}» не найден или не в сети.");
            return;
        }

        var fromConn = World.FindClientByPlayer(from);
        var toConn = World.FindClientByPlayer(target);
        if (toConn != null)
            await SendChatToAsync(toConn, ChatChannel.Whisper, from.Name, text, to: target.Name);
        if (fromConn != null)
            await SendChatToAsync(fromConn, ChatChannel.Whisper, from.Name, text, to: target.Name);
    }

    protected Task ReloadContent(ClientConnection? connection = null)
        => Program.ReloadContent(connection);

    protected int GetAttackSpeed(Player player)
        => Balance.GetAttackSpeedWithWeapon(player.Agility, player.Equipment.GetWeaponSpeedModifier());

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
