using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class UseItemHandler : BaseHandler
{
    public UseItemHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя использовать во время обмена!"); return; }
        if (message.Data is not JsonElement useEl) return;

        string? useItemId = useEl.ValueKind == JsonValueKind.String
            ? useEl.GetString()
            : useEl.TryGetProperty("ItemId", out var uidProp) ? uidProp.GetString() : null;

        if (useItemId == null) return;

        var item = player.Inventory.FirstOrDefault(i => i.Id == useItemId);
        if (item == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "Предмет не найден!");
            return;
        }

        if (item.Type == "consumable" && item.HealAmount > 0)
        {
            int healed = Math.Min(item.HealAmount, player.MaxHealth - player.Health);
            player.Health += healed;
            player.Inventory.Remove(item);
            Log.Debug($"{player.Name} использовал {item.Name}, восстановлено {healed} HP");
            var healMsg = new GameMessage
            {
                Type = "heal",
                Data = new { Target = "player", PlayerName = player.Name, X = player.X, Y = player.Y, Amount = healed }
            };
            await SendToClient(connection, healMsg);
            await Hub.SendDamageNearbyAsync(player.X, player.Y, healMsg, player);
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = $"Вы использовали {item.Name}. Восстановлено {healed} HP. ({player.Health}/{player.MaxHealth})" }
            });
            await SendInventoryAndStatus(connection, player);
        }
        else
        {
            await SendError(connection, ErrorCodes.ItemNotEquippable, "Этот предмет нельзя использовать!");
        }
    }
}
