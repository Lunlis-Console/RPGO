using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class ChatHandler : BaseHandler
{
    public ChatHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        string? text = null;
        if (message.Data is string str)
        {
            text = str;
        }
        else if (message.Data is JsonElement je)
        {
            text = je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText();
        }
        else if (message.Data != null)
        {
            text = message.Data.ToString();
        }

        if (string.IsNullOrEmpty(text)) return;

        Log.Debug($"{player.Name}: {text}");

        if (text.StartsWith("/reload", StringComparison.OrdinalIgnoreCase))
            await ReloadContent(connection);
        else
            await BroadcastChatAsync(player.Name, text);
    }
}
