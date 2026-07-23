using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Shared.Commands;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class TradeRequestHandler : BaseHandler
{
    public TradeRequestHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? targetName = el.TryGetProperty("TargetName", out var tn) ? tn.GetString() : null;
        if (string.IsNullOrEmpty(targetName))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Укажите игрока.");
            return;
        }

        if (Program.Services.Trade.IsInTrade(player))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Вы уже в обмене.");
            return;
        }

        if (!World.TryGetPlayerByName(targetName, out var target) || target == null)
        {
            await SendError(connection, ErrorCodes.TargetNotFound, "Игрок не найден.");
            return;
        }

        if (Program.Services.Trade.IsInTrade(target))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, $"{targetName} уже в обмене.");
            return;
        }

        int dist = Math.Abs(player.X - target.X) + Math.Abs(player.Y - target.Y);
        if (dist > 1)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Игрок слишком далеко.");
            return;
        }

        var targetConn = World.FindClientByPlayer(target);
        if (targetConn == null) return;

        await SendToClient(targetConn, new GameMessage
        {
            Type = "trade_request_received",
            Data = new { InviterName = player.Name }
        });

        await SendToClient(connection, new GameMessage
        {
            Type = "trade_request_sent",
            Data = new { TargetName = target.Name }
        });

        Log.Info($"Трейд запрос: {player.Name} → {target.Name}");
    }
}
