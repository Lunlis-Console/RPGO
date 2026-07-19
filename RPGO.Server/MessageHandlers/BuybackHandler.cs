using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class BuybackHandler : BaseHandler
{
    public BuybackHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement bbEl) return;

        string? bbItemId = bbEl.ValueKind == JsonValueKind.String
            ? bbEl.GetString()
            : bbEl.TryGetProperty("ItemId", out var bidProp) ? bidProp.GetString() : null;
        int qty = 1;
        if (bbEl.TryGetProperty("Quantity", out var bqtyProp) && bqtyProp.ValueKind == JsonValueKind.Number)
            qty = Math.Max(1, bqtyProp.GetInt32());

        if (bbItemId == null) return;

        var first = player.BuybackItems.FirstOrDefault(i => i.Id == bbItemId);
        if (first == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "Предмет не найден для выкупа!");
            return;
        }

        var matches = player.BuybackItems.Where(i =>
            i.Name == first.Name && i.Type == first.Type &&
            i.Attack == first.Attack && i.Defense == first.Defense &&
            i.MaxHealthBonus == first.MaxHealthBonus && i.HealAmount == first.HealAmount &&
            i.Value == first.Value && i.Description == first.Description).ToList();
        int toBuy = Math.Min(qty, matches.Count);
        int price = Balance.BuybackPrice(first.Value);
        int totalCost = price * toBuy;

        if (player.Gold < totalCost)
        {
            await SendError(connection, ErrorCodes.InsufficientGold, $"Недостаточно золота! Нужно: {totalCost}");
            return;
        }

        player.Gold -= totalCost;
        for (int i = 0; i < toBuy; i++)
        {
            player.BuybackItems.Remove(matches[i]);
            player.Inventory.Add(matches[i]);
        }
        Log.Info($"{player.Name} выкупил {first.Name} x{toBuy} за {totalCost} золота");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы выкупили {first.Name} x{toBuy} за {totalCost} золота" }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "shop_update",
            Data = new { PlayerGold = player.Gold, Buyback = player.BuybackItems.Select(b => new
            {
                b.Id, b.Name, b.Type,
                Value = Balance.BuybackPrice(b.Value),
                OriginalValue = b.Value,
                b.Attack, b.Defense, b.MaxHealthBonus, b.HealAmount, b.Description,
                IsBuyback = true
            }).ToList() }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
