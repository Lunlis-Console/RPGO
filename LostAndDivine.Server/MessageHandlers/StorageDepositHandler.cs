using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class StorageDepositHandler : BaseHandler
{
    public StorageDepositHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? itemId = el.TryGetProperty("ItemId", out var idProp) ? idProp.GetString() : null;
        int quantity = el.TryGetProperty("Quantity", out var qtyProp) && qtyProp.ValueKind == JsonValueKind.Number
            ? Math.Max(1, qtyProp.GetInt32()) : 1;

        if (string.IsNullOrEmpty(itemId))
        {
            await SendError(connection, ErrorCodes.InvalidParameter, "Item ID не указан!");
            return;
        }

        await Svc.Storage.DepositAsync(player, itemId, quantity);
    }
}
