using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class LogoutHandler : BaseHandler
{
    public LogoutHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player != null)
        {
            try
            {
                DatabaseManager.SavePlayerProgress(player);
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
