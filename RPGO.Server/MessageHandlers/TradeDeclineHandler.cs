using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Shared.Commands;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class TradeDeclineHandler : BaseHandler
{
    public TradeDeclineHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? inviterName = el.TryGetProperty("InviterName", out var invN) ? invN.GetString() : null;
        if (string.IsNullOrEmpty(inviterName)) return;

        if (!World.TryGetPlayerByName(inviterName, out var inviter) || inviter == null) return;

        var inviterConn = World.FindClientByPlayer(inviter);
        if (inviterConn != null)
        {
            await SendToClient(inviterConn, new GameMessage
            {
                Type = "trade_declined",
                Data = new { TargetName = player.Name, Message = $"{player.Name} отказался от обмена." }
            });
        }

        Log.Info($"Трейд отклонён: {player.Name} отклонил(а) запрос от {inviterName}");
    }
}
