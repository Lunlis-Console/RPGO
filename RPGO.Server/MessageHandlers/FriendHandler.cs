using RPGGame.Server.Network;
using System.Text.Json;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

/// <summary>
/// Управление списком друзей: list / add / remove.
/// Сообщения типа "friend" с полем Action в Data.
/// </summary>
public class FriendHandler : BaseHandler
{
    public FriendHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        string action = "list";
        string targetName = "";
        if (message.Data is JsonElement el && el.ValueKind != JsonValueKind.Undefined)
        {
            if (el.TryGetProperty("Action", out var aEl))
                action = aEl.GetString() ?? "list";
            if (el.TryGetProperty("TargetName", out var tEl))
                targetName = tEl.GetString() ?? "";
        }

        switch (action.ToLowerInvariant())
        {
            case "list":
                await SendFriendListAsync(connection, player);
                break;

            case "add":
                await HandleAddAsync(connection, player, targetName);
                break;

            case "remove":
                await HandleRemoveAsync(connection, player, targetName);
                break;
        }
    }

    private async Task SendFriendListAsync(ClientConnection connection, Player player)
    {
        await Hub.SendFriendListToAsync(connection, player);
    }

    private async Task HandleAddAsync(ClientConnection connection, Player player, string targetName)
    {
        targetName = (targetName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(targetName))
        {
            await SendResult(connection, false, "Укажите имя игрока");
            return;
        }

        if (targetName.Equals(player.Name, StringComparison.OrdinalIgnoreCase))
        {
            await SendResult(connection, false, "Нельзя добавить себя");
            return;
        }

        // Персонаж должен существовать в БД (независимо от того, в сети он сейчас или нет)
        if (!DatabaseManager.PlayerNameExists(targetName))
        {
            await SendResult(connection, false, $"Персонаж «{targetName}» не найден");
            return;
        }

        if (DatabaseManager.FriendExists(player.Name, targetName))
        {
            await SendResult(connection, false, $"«{targetName}» уже в друзьях");
            return;
        }

        int currentCount = DatabaseManager.GetFriendNames(player.Name).Count;
        if (currentCount >= DatabaseManager.MaxFriends)
        {
            await SendResult(connection, false,
                $"Достигнут лимит друзей ({DatabaseManager.MaxFriends}). Сначала удалите кого-то.");
            return;
        }

        DatabaseManager.AddFriend(player.Name, targetName);
        await SendResult(connection, true, $"«{targetName}» добавлен(а) в друзья");

        // Обновляем список у себя
        await SendFriendListAsync(connection, player);
        // И у друга, если он сейчас в сети
        if (World.TryGetPlayerByName(targetName, out var target) && target != null)
        {
            var targetConn = World.FindClientByPlayer(target);
            if (targetConn != null)
                await SendFriendListAsync(targetConn, target);
        }
    }

    private async Task HandleRemoveAsync(ClientConnection connection, Player player, string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            await SendResult(connection, false, "Укажите имя игрока");
            return;
        }

        DatabaseManager.RemoveFriend(player.Name, targetName);
        await SendResult(connection, true, $"«{targetName}» удалён(а) из друзей");

        await SendFriendListAsync(connection, player);
        if (World.TryGetPlayerByName(targetName, out var target) && target != null)
        {
            var targetConn = World.FindClientByPlayer(target);
            if (targetConn != null)
                await SendFriendListAsync(targetConn, target);
        }
    }

    private async Task SendResult(ClientConnection connection, bool success, string message)
    {
        await SendToClient(connection, new GameMessage
        {
            Type = "friend_result",
            Data = new FriendResult { Success = success, Message = message }
        });
    }
}
