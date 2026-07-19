using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class UnequipHandler : BaseHandler
{
    public UnequipHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя снять во время обмена!"); return; }
        if (message.Data is not JsonElement unequipEl) return;

        string? slot = unequipEl.ValueKind == JsonValueKind.String
            ? unequipEl.GetString()
            : unequipEl.TryGetProperty("Slot", out var slotProp) ? slotProp.GetString() : null;

        if (slot == null) return;

        Item? unequipped = null;
        if (slot == "weapon" && player.Equipment.Weapon != null)
        { unequipped = player.Equipment.Weapon; player.Equipment.Weapon = null; }
        else if (slot == "armor" && player.Equipment.Armor != null)
        { unequipped = player.Equipment.Armor; player.Equipment.Armor = null; }
        else if (slot == "accessory" && player.Equipment.Accessory != null)
        { unequipped = player.Equipment.Accessory; player.Equipment.Accessory = null; }

        if (unequipped != null)
        {
            InventoryHelper.AddItem(player, unequipped);
            Log.Debug($"{player.Name} снял {unequipped.Name}");
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = $"Вы сняли {unequipped.Name}" }
            });
            await SendInventoryAndStatus(connection, player);
        }
        else
        {
            await SendError(connection, ErrorCodes.SlotEmpty, "Слот пуст — нечего снимать.");
        }
    }
}
