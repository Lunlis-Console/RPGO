using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.Shared.Commands;

namespace LostAndDivine.Server.MessageHandlers;

public class TradeCancelHandler : BaseHandler
{
    public TradeCancelHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        var session = Svc.Trade.GetSession(player.Id);
        if (session == null) return;

        var other = session.GetOther(player);

        session.Initiator.IsTrading = false;
        session.Partner.IsTrading = false;

        var closeMsg = new GameMessage
        {
            Type = GameMessageType.TradeClose,
            Data = new { Message = "Обмен отменён." }
        };

        var initiatorConn = World.FindClientByPlayer(session.Initiator);
        var partnerConn = World.FindClientByPlayer(session.Partner);

        if (initiatorConn != null) await SendToClient(initiatorConn, closeMsg);
        if (partnerConn != null) await SendToClient(partnerConn, closeMsg);

        Svc.Trade.CancelSession(session, $"отменён игроком {player.Name}");
    }
}
