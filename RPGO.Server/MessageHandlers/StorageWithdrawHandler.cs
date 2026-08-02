using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class StorageWithdrawHandler : BaseHandler
{
    public StorageWithdrawHandler(GameServices svc) : base(svc) { }

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

        await Svc.Storage.WithdrawAsync(player, itemId, quantity);
    }
}
