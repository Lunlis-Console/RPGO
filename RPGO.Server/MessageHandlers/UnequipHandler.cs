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

        var item = player.Equipment[slot];
        if (item == null)
        {
            await SendError(connection, ErrorCodes.SlotEmpty, "Слот пуст — нечего снимать.");
            return;
        }

        // Снимаем сам слот и все прочие слоты с тем же предметом (напр. перчатки на обеих руках)
        string itemId = item.Id;
        foreach (var s in EquipmentSlots.All)
            if (player.Equipment[s.Id]?.Id == itemId)
                player.Equipment[s.Id] = null;

        // Возвращаем предмет в инвентарь один раз
        InventoryHelper.AddItem(player, item);

        Log.Debug($"{player.Name} снял {item.Name}");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы сняли {item.Name}" }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
