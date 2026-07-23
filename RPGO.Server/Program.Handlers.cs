using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Server.MessageHandlers;
using System.Text.Json;

namespace RPGGame.Server;

public partial class Program
{
    private static async Task<Player?> ProcessMessage(ClientConnection connection, GameMessage message, Player? player)
    {
        try
        {
            if (message.Type is "register" or "login_auth")
            {
                await HandleAuthMessage(connection, message, Services.Hub);
                return player;
            }

            if (MessageHandlerRegistry.TryGet(message.Type, out var handler))
            {
                await handler.Handle(connection, message, player);
                return player;
            }

            Log.Warn($"Неизвестный тип сообщения: {message.Type}");
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка обработки {message.Type}", ex);
        }

        return player;
    }
}
