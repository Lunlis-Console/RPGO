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
            if (je.ValueKind == JsonValueKind.String)
                text = je.GetString();
            else if (je.TryGetProperty("Text", out var tEl))
                text = tEl.GetString();
            else
                text = je.GetRawText();
        }
        else if (message.Data != null)
        {
            text = message.Data.ToString();
        }

        if (string.IsNullOrEmpty(text)) return;
        text = text.Trim();
        if (text.Length == 0) return;

        Log.Debug($"CHAT {player.Name}: {text}");

        if (text.StartsWith("/reload", StringComparison.OrdinalIgnoreCase))
        {
            await ReloadContent(connection);
            return;
        }

        await RouteMessage(player, text);
    }

    private async Task RouteMessage(Player player, string text)
    {
        if (text.StartsWith("/w", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/whisper", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/tell", StringComparison.OrdinalIgnoreCase))
        {
            var rest = text.IndexOf(' ');
            if (rest < 0)
            {
                await SystemToSelf(player, "Использование: /w <ник> <сообщение>");
                return;
            }
            var after = text.Substring(rest + 1).TrimStart();
            var sp = after.IndexOf(' ');
            if (sp < 0)
            {
                await SystemToSelf(player, "Использование: /w <ник> <сообщение>");
                return;
            }
            var target = after.Substring(0, sp).Trim();
            var msg = after.Substring(sp + 1).Trim();
            await SendWhisperAsync(player, target, msg);
            return;
        }

        if (text.StartsWith("/p", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/party", StringComparison.OrdinalIgnoreCase))
        {
            var msg = StripPrefix(text);
            await SendChatPartyAsync(player, player.Name, msg);
            return;
        }

        if (text.StartsWith("/g", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/guild", StringComparison.OrdinalIgnoreCase))
        {
            var msg = StripPrefix(text);
            await SystemToSelf(player, "Гильдии пока не реализованы.");
            return;
        }

        if (text.StartsWith("/trade", StringComparison.OrdinalIgnoreCase))
        {
            var msg = StripPrefix(text);
            await BroadcastChatAsync(ChatChannel.Trade, player.Name, msg);
            return;
        }

        if (text.StartsWith("/world", StringComparison.OrdinalIgnoreCase))
        {
            var msg = StripPrefix(text);
            await BroadcastChatAsync(ChatChannel.World, player.Name, msg);
            return;
        }

        if (text.StartsWith("/s", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/say", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/local", StringComparison.OrdinalIgnoreCase))
        {
            var msg = StripPrefix(text);
            await SendChatLocalAsync(player, ChatChannel.Local, player.Name, msg);
            return;
        }

        // По умолчанию — локальный канал (мир живее)
        await SendChatLocalAsync(player, ChatChannel.Local, player.Name, text);
    }

    private static string StripPrefix(string text)
    {
        var sp = text.IndexOf(' ');
        return sp < 0 ? "" : text.Substring(sp + 1).Trim();
    }

    private async Task SystemToSelf(Player player, string msg)
    {
        var conn = World.FindClientByPlayer(player);
        if (conn != null)
            await SendChatToAsync(conn, ChatChannel.System, "Система", msg);
    }
}
