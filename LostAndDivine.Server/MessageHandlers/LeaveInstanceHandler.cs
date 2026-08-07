using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class LeaveInstanceHandler : BaseHandler
{
    public LeaveInstanceHandler(GameServices svc) : base(svc) { }

    public override Task Handle(ClientConnection client, GameMessage msg, Player? player)
    {
        if (player != null && player.CurrentZoneId.StartsWith("instance:"))
            _ = Svc.Instances.KickPlayer(player, "Вы покинули подземелье.");
        return Task.CompletedTask;
    }
}
