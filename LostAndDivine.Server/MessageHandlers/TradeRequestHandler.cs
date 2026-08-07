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
            await SendError(connection, ErrorCodes.InvalidRequest, "������� ������.");
            return;
        }

        if (Svc.Trade.IsInTrade(player))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "�� ��� � ������.");
            return;
        }

        if (!World.TryGetPlayerByName(targetName, out var target) || target == null)
        {
            await SendError(connection, ErrorCodes.TargetNotFound, "����� �� ������.");
            return;
        }

        if (Svc.Trade.IsInTrade(target))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, $"{targetName} ��� � ������.");
            return;
        }

        int dist = Math.Abs(player.X - target.X) + Math.Abs(player.Y - target.Y);
        if (dist > 1)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "����� ������� ������.");
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

        Log.Info($"����� ������: {player.Name} > {target.Name}");
    }
}
