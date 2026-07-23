using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Shared.Commands;

namespace RPGGame.Server.MessageHandlers;

public class TradeCancelHandler : BaseHandler
{
    public TradeCancelHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        var session = Program.Services.Trade.GetSession(player.Id);
        if (session == null) return;

        var other = session.GetOther(player);

        session.Initiator.IsTrading = false;
        session.Partner.IsTrading = false;

        var closeMsg = new GameMessage
        {
            Type = "trade_close",
            Data = new { Message = "Обмен отменён." }
        };

        var initiatorConn = World.FindClientByPlayer(session.Initiator);
        var partnerConn = World.FindClientByPlayer(session.Partner);

        if (initiatorConn != null) await SendToClient(initiatorConn, closeMsg);
        if (partnerConn != null) await SendToClient(partnerConn, closeMsg);

        Program.Services.Trade.CancelSession(session, $"отменён игроком {player.Name}");
    }
}
