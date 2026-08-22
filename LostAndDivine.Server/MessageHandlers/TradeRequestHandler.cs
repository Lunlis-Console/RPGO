using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.Shared.Commands;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class TradeRequestHandler : BaseHandler
{
    public TradeRequestHandler(GameServices svc) : base(svc) { }

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

        if (Svc.Trade.IsInTrade(player))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Вы уже в обмене.");
            return;
        }

        if (!World.TryGetPlayerByName(targetName, out var target) || target == null)
        {
            await SendError(connection, ErrorCodes.TargetNotFound, "Игрок не найден.");
            return;
        }

        if (Svc.Trade.IsInTrade(target))
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
            Type = GameMessageType.TradeRequestReceived,
            Data = new { InviterName = player.Name }
        });

        await SendToClient(connection, new GameMessage
        {
            Type = GameMessageType.TradeRequestSent,
            Data = new { TargetName = target.Name }
        });

        Log.Info($"Трейд запрос: {player.Name} > {target.Name}");
    }
}
