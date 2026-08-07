using LostAndDivine.Server.Network;
using System.Text.Json;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// ���������� ������� ������: list / add / remove.
/// ��������� ���� "friend" � ����� Action � Data.
/// </summary>
public class FriendHandler : BaseHandler
{
    public FriendHandler(GameServices svc) : base(svc) { }

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
            await SendResult(connection, false, "������� ��� ������");
            return;
        }

        if (targetName.Equals(player.Name, StringComparison.OrdinalIgnoreCase))
        {
            await SendResult(connection, false, "������ �������� ����");
            return;
        }

        // �������� ������ ������������ � �� (���������� �� ����, � ���� �� ������ ��� ���)
        if (!DatabaseManager.PlayerNameExists(targetName))
        {
            await SendResult(connection, false, $"�������� �{targetName}� �� ������");
            return;
        }

        if (DatabaseManager.FriendExists(player.Name, targetName))
        {
            await SendResult(connection, false, $"�{targetName}� ��� � �������");
            return;
        }

        int currentCount = DatabaseManager.GetFriendNames(player.Name).Count;
        if (currentCount >= DatabaseManager.MaxFriends)
        {
            await SendResult(connection, false,
                $"��������� ����� ������ ({DatabaseManager.MaxFriends}). ������� ������� ����-��.");
            return;
        }

        DatabaseManager.AddFriend(player.Name, targetName);
        await SendResult(connection, true, $"�{targetName}� ��������(�) � ������");

        // ��������� ������ � ����
        await SendFriendListAsync(connection, player);
        // � � �����, ���� �� ������ � ����
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
            await SendResult(connection, false, "������� ��� ������");
            return;
        }

        DatabaseManager.RemoveFriend(player.Name, targetName);
        await SendResult(connection, true, $"�{targetName}� �����(�) �� ������");

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
