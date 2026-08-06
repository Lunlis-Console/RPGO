using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class ReviveHandler : BaseHandler
{
    public ReviveHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (!player.IsDead) return;

        if (player.CurrentZoneId.StartsWith("instance:"))
        {
            var inst = Svc.Instances.FindInstanceByPlayer(player);
            if (inst != null)
            {
                int spawnX = inst._spawnX > 0 ? inst._spawnX : inst.Template.SpawnX + inst.OffsetX;
                int spawnY = inst._spawnY > 0 ? inst._spawnY : inst.Template.SpawnY + inst.OffsetY;
                await Svc.PlayerDeath.RespawnPlayer(player, spawnX, spawnY);
                return;
            }
        }
        await Svc.PlayerDeath.RespawnPlayer(player);
    }
}
