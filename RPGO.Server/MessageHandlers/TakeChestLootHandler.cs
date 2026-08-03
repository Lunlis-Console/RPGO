using System.Linq;
using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class TakeChestLootHandler : BaseHandler
{
    public TakeChestLootHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement data) return;

        if (!data.TryGetProperty("InstanceId", out var instIdEl) || instIdEl.ValueKind != JsonValueKind.String)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Неверный ID сундука");
            return;
        }
        var instanceId = instIdEl.GetString()!;
        if (!Guid.TryParse(instanceId, out var instGuid))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Неверный формат ID сундука");
            return;
        }

        var inst = Svc.Instances.FindInstanceById(instGuid);
        if (inst == null)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Сундук не найден");
            return;
        }

        if (!inst.Players.Contains(player))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Вы не в этом инстансе");
            return;
        }

        bool takeGold = data.TryGetProperty("TakeGold", out var tgEl) && tgEl.ValueKind == JsonValueKind.True;
        var itemIds = new List<string>();
        if (data.TryGetProperty("ItemIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in idsEl.EnumerateArray())
                if (el.ValueKind == JsonValueKind.String)
                    itemIds.Add(el.GetString()!);
        }

        var takenNames = new List<string>();

        if (takeGold && inst.ChestGold > 0)
        {
            player.Gold += inst.ChestGold;
            takenNames.Add($"{inst.ChestGold} золота");
            inst.ChestGold = 0;
        }

        foreach (var itemId in itemIds)
        {
            var item = inst.ChestLootItems.FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                InventoryHelper.AddItem(player, item);
                inst.ChestLootItems.Remove(item);
                takenNames.Add(item.Name);
            }
        }

        string lootText = takenNames.Count > 0 ? string.Join(", ", takenNames) : "Ничего не выбрано";
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы забрали: {lootText}" }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
