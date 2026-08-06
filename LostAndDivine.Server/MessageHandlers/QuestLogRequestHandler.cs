using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class QuestLogRequestHandler : BaseHandler
{
    public QuestLogRequestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player != null)
            await SendQuestLog(connection, player);
    }
}
