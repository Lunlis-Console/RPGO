using System.Text.Json;
using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class PlayerFacingHandler : BaseHandler
{
    public PlayerFacingHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        Facing facing = Facing.Down;
        if (message.Data is JsonElement el && el.TryGetProperty("Facing", out var fEl))
        {
            var raw = fEl.GetString();
            if (!string.IsNullOrEmpty(raw) && Enum.TryParse<Facing>(raw, true, out var parsed))
                facing = parsed;
        }

        player.Facing = facing;

        await Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_facing",
            Data = new { PlayerName = player.Name, Facing = facing }
        });
    }
}
