using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class InventorySortHandler : BaseHandler
{
    public InventorySortHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement sortEl) return;

        var orderJson = sortEl.TryGetProperty("Order", out var orderProp) ? orderProp.GetRawText() : null;
        if (orderJson == null) return;

        try
        {
            var order = JsonSerializer.Deserialize<List<string>>(orderJson);
            if (order != null && order.Count > 0)
            {
                var remaining = new Queue<Item>(player.Inventory);
                var byId = player.Inventory
                    .GroupBy(i => i.Id)
                    .ToDictionary(g => g.Key, g => new Queue<Item>(g));
                var sorted = new List<Item>();
                foreach (var id in order)
                {
                    if (byId.TryGetValue(id, out var q) && q.Count > 0)
                        sorted.Add(q.Dequeue());
                }
                // Добавляем оставшиеся (те, что не попали в Order, либо дубликаты сверх списка)
                foreach (var it in remaining)
                    if (!sorted.Contains(it)) sorted.Add(it);
                player.Inventory = sorted;
                DatabaseManager.SavePlayerProgress(player);
                await SendInventoryAndStatus(connection, player);
            }
        }
        catch (Exception ex) { Log.Warn($"Inventory sort parse error: {ex.Message}"); }
    }
}
