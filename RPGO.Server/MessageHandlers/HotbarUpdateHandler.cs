using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class HotbarUpdateHandler : BaseHandler
{
    public HotbarUpdateHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement hbEl) return;

        string? slotsJson = hbEl.TryGetProperty("Slots", out var slotsProp) ? slotsProp.GetRawText() : null;
        if (slotsJson == null) return;

        try
        {
            var slots = JsonSerializer.Deserialize<List<string?>>(slotsJson);
            if (slots != null)
            {
                while (slots.Count < Balance.HotbarSlots) slots.Add(null);
                player.HotbarSlots = slots.Take(Balance.HotbarSlots).ToList();
                // Сохраняем сразу, чтобы хотбар не потерялся при выходе без корректного logout
                DatabaseManager.SavePlayerProgress(player);
            }
        }
        catch (Exception ex) { Log.Warn($"Hotbar update parse error: {ex.Message}"); }
    }
}
