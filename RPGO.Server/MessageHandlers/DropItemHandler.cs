using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class DropItemHandler : BaseHandler
{
    public DropItemHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

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
