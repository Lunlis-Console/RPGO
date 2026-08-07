using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class BuyHandler : BaseHandler
{
    public BuyHandler(GameServices svc) : base(svc) { }

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

        var template = Svc.Merchant.FindItem(buyItemId);
        if (template == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "������� �� ������!");
            return;
        }

        int stock = Math.Max(1, template.Stock);
        if (qty > stock) qty = stock;

        int price = Balance.BuyPrice(template.Value);
        int totalCost = price * qty;

        if (player.Gold < totalCost)
        {
            await SendError(connection, ErrorCodes.InsufficientGold, $"������������ ������! �����: {totalCost}");
            return;
        }

        player.Gold -= totalCost;
        var newItem = Svc.Merchant.CreatePlayerCopy(template);
        newItem.Quantity = qty;
        InventoryHelper.AddItem(player, newItem);
        Log.Info($"{player.Name} ����� {template.Name} x{qty} �� {totalCost} ������");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "�������", Text = $"�� ������ {template.Name} x{qty} �� {totalCost} ������" }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "shop_update",
            Data = new { PlayerGold = player.Gold }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
