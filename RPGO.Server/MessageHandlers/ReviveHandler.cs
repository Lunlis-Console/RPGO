using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class ReviveHandler : BaseHandler
{
    public ReviveHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (!player.IsDead) return;
        await Program.RespawnPlayer(player);
    }
}
