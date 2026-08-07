using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.Shared.Commands;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class TradeDeclineHandler : BaseHandler
{
    public TradeDeclineHandler(GameServices svc) : base(svc) { }

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
                Data = new { TargetName = player.Name, Message = $"{player.Name} ��������� �� ������." }
            });
        }

        Log.Info($"����� �������: {player.Name} ��������(�) ������ �� {inviterName}");
    }
}
