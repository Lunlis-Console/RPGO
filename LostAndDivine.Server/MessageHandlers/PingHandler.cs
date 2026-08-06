using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class PingHandler : BaseHandler
{
    public PingHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (message.Data is not JsonElement el) return;
        var ping = JsonSerializer.Deserialize<PingMessage>(el.GetRawText());
        if (ping == null) return;

        connection.LastPongReceived = DateTime.UtcNow;

        await SendToClient(connection, new GameMessage
        {
            Type = "pong",
            Data = new PongMessage(ping.Seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        });
    }
}
