using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class LogoutHandler : BaseHandler
{
    public LogoutHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player != null)
        {
            try
            {
                Svc.Persistence.EnqueueSave(player);
                World.RemovePlayer(player);
                Log.Info($"{player.Name} вышел из игры (прогресс сохранён)");
            }
            catch (Exception ex)
            {
                Log.Error($"[Logout] Save failed for {player.Name}", ex);
            }

            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = "Ваш прогресс сохранён. До встречи!" }
            });
        }

        World.RemoveClient(connection);
        try { connection.Client.Close(); } catch { /* already closing */ }
    }
}
