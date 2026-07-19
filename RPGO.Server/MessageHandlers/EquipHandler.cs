using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class EquipHandler : BaseHandler
{
    public EquipHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя надеть во время обмена!"); return; }
        if (message.Data is not JsonElement equipEl) return;

        string? equipItemId = equipEl.ValueKind == JsonValueKind.String
            ? equipEl.GetString()
            : equipEl.TryGetProperty("ItemId", out var eidProp) ? eidProp.GetString() : null;

        if (equipItemId == null) return;

        var item = player.Inventory.FirstOrDefault(i => i.Id == equipItemId);
        if (item == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "Предмет не найден!");
            return;
        }

        if (item.Type != "weapon" && item.Type != "armor" && item.Type != "accessory")
        {
            await SendError(connection, ErrorCodes.ItemNotEquippable, "Этот предмет нельзя надеть!");
            return;
        }

        Item? oldEquipped = null;
        if (item.Type == "weapon") { oldEquipped = player.Equipment.Weapon; player.Equipment.Weapon = item; }
        else if (item.Type == "armor") { oldEquipped = player.Equipment.Armor; player.Equipment.Armor = item; }
        else if (item.Type == "accessory") { oldEquipped = player.Equipment.Accessory; player.Equipment.Accessory = item; }

        player.Inventory.Remove(item);
        if (oldEquipped != null) player.Inventory.Add(oldEquipped);

        Log.Debug($"{player.Name} надел {item.Name}");
        string msg = oldEquipped != null
            ? $"Вы надели {item.Name}, сняв {oldEquipped.Name}"
            : $"Вы надели {item.Name}";
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = msg }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
