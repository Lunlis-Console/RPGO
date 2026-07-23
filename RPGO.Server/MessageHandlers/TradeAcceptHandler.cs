using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Shared.Commands;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class TradeAcceptHandler : BaseHandler
{
    public TradeAcceptHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? inviterName = el.TryGetProperty("InviterName", out var invN) ? invN.GetString() : null;
        if (string.IsNullOrEmpty(inviterName))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Нет запроса.");
            return;
        }

        if (Program.Services.Trade.IsInTrade(player))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Вы уже в обмене.");
            return;
        }

        if (!World.TryGetPlayerByName(inviterName, out var inviter) || inviter == null)
        {
            await SendError(connection, ErrorCodes.TargetNotFound, "Игрок не найден.");
            return;
        }

        if (Program.Services.Trade.IsInTrade(inviter))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, $"{inviterName} уже в обмене.");
            return;
        }

        int dist = Math.Abs(player.X - inviter.X) + Math.Abs(player.Y - inviter.Y);
        if (dist > 1)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Игрок слишком далеко.");
            return;
        }

        var session = Program.Services.Trade.CreateSession(inviter, player);

        inviter.IsTrading = true;
        player.IsTrading = true;

        var inviterConn = World.FindClientByPlayer(inviter);

        var (inviterItems, inviterGold) = BuildInventoryData(inviter);
        var (playerItems, playerGold) = BuildInventoryData(player);

        await SendToClient(connection, new GameMessage
        {
            Type = "trade_open",
            Data = new
            {
                SessionId = session.Id.ToString(),
                OtherName = inviter.Name,
                OtherLevel = inviter.Level,
                OtherHp = inviter.Health,
                OtherMaxHp = inviter.MaxHealth,
                YourInventory = playerItems,
                YourGold = playerGold,
                OtherInventory = inviterItems,
                OtherGold = inviterGold
            }
        });

        if (inviterConn != null)
        {
            await SendToClient(inviterConn, new GameMessage
            {
                Type = "trade_open",
                Data = new
                {
                    SessionId = session.Id.ToString(),
                    OtherName = player.Name,
                    OtherLevel = player.Level,
                    OtherHp = player.Health,
                    OtherMaxHp = player.MaxHealth,
                    YourInventory = inviterItems,
                    YourGold = inviterGold,
                    OtherInventory = playerItems,
                    OtherGold = playerGold
                }
            });
        }

        Log.Info($"Трейд начат: {inviter.Name} ↔ {player.Name}");
    }

    private static (List<object> items, int gold) BuildInventoryData(Player player)
    {
        var items = player.Inventory.Select(i => new
        {
            i.Id, i.Name, i.Type, i.Value, i.Description,
            i.MaxHealthBonus, i.HealAmount,
            i.MaxStack, i.Quantity
        }).Cast<object>().ToList();
        return (items, player.Gold);
    }
}
