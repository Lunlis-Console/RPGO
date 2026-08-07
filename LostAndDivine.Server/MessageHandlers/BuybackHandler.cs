using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class BuybackHandler : BaseHandler
{
    public BuybackHandler(GameServices svc) : base(svc) { }

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

        // Группируем все записи выкупа того же типа
        var matches = player.BuybackItems.Where(i =>
            i.Name == first.Name && i.Type == first.Type &&
            i.BonusPhysAttack == first.BonusPhysAttack && i.BonusDefense == first.BonusDefense &&
            i.MaxHealthBonus == first.MaxHealthBonus && i.HealAmount == first.HealAmount && i.RestoreMana == first.RestoreMana &&
            i.Value == first.Value && i.Description == first.Description).ToList();

        int totalAvailable = matches.Sum(i => i.Quantity);
        int toBuy = Math.Min(qty, totalAvailable);
        int price = Balance.BuybackPrice(first.Value);
        int totalCost = price * toBuy;

        if (player.Gold < totalCost)
        {
            await SendError(connection, ErrorCodes.InsufficientGold, $"Недостаточно золота! Нужно: {totalCost}");
            return;
        }

        player.Gold -= totalCost;

        int remaining = toBuy;
        foreach (var m in matches)
        {
            if (remaining <= 0) break;
            int take = Math.Min(m.Quantity, remaining);
            var clone = m.Clone();
            clone.Id = Guid.NewGuid().ToString();
            clone.Quantity = take;
            InventoryHelper.AddItem(player, clone);

            if (m.Quantity <= take)
                player.BuybackItems.Remove(m);
            else
                m.Quantity -= take;
            remaining -= take;
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
                b.MaxHealthBonus, b.HealAmount, b.RestoreMana, b.Description,
                b.Quantity, IsBuyback = true
            }).ToList() }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
