using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Ответ клиента на keep-alive ping сервера: подтверждает, что канал жив.
/// Обновляет время последнего pong, чтобы HeartbeatHandler не считал клиента отключившимся.
/// </summary>
public class PongHandler : BaseHandler
{
    public PongHandler(GameServices svc) : base(svc) { }

    public override Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        connection.LastPongReceived = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
