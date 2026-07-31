using System.Text.Json;
using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class PlayerFacingHandler : BaseHandler
{
    public PlayerFacingHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        string facing = "down";
        if (message.Data is JsonElement el && el.TryGetProperty("Facing", out var fEl))
            facing = fEl.GetString() ?? "down";

        player.Facing = facing;

        await Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_facing",
            Data = new { PlayerName = player.Name, Facing = facing }
        });
    }
}
