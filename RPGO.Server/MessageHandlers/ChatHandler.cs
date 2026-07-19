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
        // Команды проверяем по точному первому слову (с пробелом или концу строки),
        // чтобы "/world" не ловился как "/w", а "/party" не как "/p".
        if (HasPrefix(text, "/w") || HasPrefix(text, "/whisper") || HasPrefix(text, "/tell"))
        {
            var after = StripPrefix(text).TrimStart();
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

        if (HasPrefix(text, "/p") || HasPrefix(text, "/party"))
        {
            var msg = StripPrefix(text);
            await SendChatPartyAsync(player, player.Name, msg);
            return;
        }

        if (HasPrefix(text, "/g") || HasPrefix(text, "/guild"))
        {
            await SystemToSelf(player, "Гильдии пока не реализованы.");
            return;
        }

        if (HasPrefix(text, "/trade"))
        {
            var msg = StripPrefix(text);
            await BroadcastChatAsync(ChatChannel.Trade, player.Name, msg);
            return;
        }

        if (HasPrefix(text, "/world"))
        {
            var msg = StripPrefix(text);
            await BroadcastChatAsync(ChatChannel.World, player.Name, msg);
            return;
        }

        if (HasPrefix(text, "/s") || HasPrefix(text, "/say") || HasPrefix(text, "/local"))
        {
            var msg = StripPrefix(text);
            await SendChatLocalAsync(player, ChatChannel.Local, player.Name, msg);
            return;
        }

        // По умолчанию — локальный канал (мир живее)
        await SendChatLocalAsync(player, ChatChannel.Local, player.Name, text);
    }

    private static bool HasPrefix(string text, string cmd)
    {
        if (!text.StartsWith(cmd, StringComparison.OrdinalIgnoreCase))
            return false;
        // Точное совпадение команды или команда + пробел
        return text.Length == cmd.Length || text[cmd.Length] == ' ';
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
