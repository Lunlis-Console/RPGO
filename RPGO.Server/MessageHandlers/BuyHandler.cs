using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class BuyHandler : BaseHandler
{
    public BuyHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement buyEl) return;

        string? buyItemId = buyEl.ValueKind == JsonValueKind.String
            ? buyEl.GetString()
            : buyEl.TryGetProperty("ItemId", out var idProp) ? idProp.GetString() : null;
        int qty = 1;
        if (buyEl.TryGetProperty("Quantity", out var qtyProp) && qtyProp.ValueKind == JsonValueKind.Number)
            qty = Math.Max(1, qtyProp.GetInt32());

        if (buyItemId == null) return;

        var template = MerchantManager.FindItem(buyItemId);
        if (template == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "Предмет не найден!");
            return;
        }

        int stock = Math.Max(1, template.Stock);
        if (qty > stock) qty = stock;

        int buyDiscount = Balance.ShopDiscountPct(player.Cunning);
        int price = Balance.BuyPrice(template.Value, buyDiscount);
        int totalCost = price * qty;

        if (player.Gold < totalCost)
        {
            await SendError(connection, ErrorCodes.InsufficientGold, $"Недостаточно золота! Нужно: {totalCost}");
            return;
        }

        player.Gold -= totalCost;
        var newItem = MerchantManager.CreatePlayerCopy(template);
        newItem.Quantity = qty;
        InventoryHelper.AddItem(player, newItem);
        Log.Info($"{player.Name} купил {template.Name} x{qty} за {totalCost} золота");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы купили {template.Name} x{qty} за {totalCost} золота" }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "shop_update",
            Data = new { PlayerGold = player.Gold }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
