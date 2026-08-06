using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class DropItemHandler : BaseHandler
{
    public DropItemHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? dropId = el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : el.TryGetProperty("ItemId", out var idProp) ? idProp.GetString() : null;
        if (dropId == null) return;

        int quantity = 1;
        if (el.TryGetProperty("Quantity", out var qProp) && qProp.ValueKind == JsonValueKind.Number)
            quantity = Math.Max(1, qProp.GetInt32());

        var proto = player.Inventory.FirstOrDefault(i => i.Id == dropId);
        if (proto == null)
        {
            await SendError(connection, ErrorCodes.ItemNotInInventory, "Предмет не найден в вашем инвентаре!");
            return;
        }

        int available = proto.Quantity;
        int removed = Math.Min(quantity, available);
        InventoryHelper.RemoveFromRecord(player, dropId, removed);

        Log.Info($"{player.Name} выбросил {proto.Name} x{removed}");
        await SendInventoryAndStatus(connection, player);
    }
}
