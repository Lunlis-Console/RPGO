using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class StorageOpenHandler : BaseHandler
{
    public StorageOpenHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        await Svc.Storage.OnPlayerInteractAsync(player);
    }
}
