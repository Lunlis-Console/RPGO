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

        if (!EquipmentSlots.IsEquippableType(item.Type))
        {
            await SendError(connection, ErrorCodes.ItemNotEquippable, "Этот предмет нельзя надеть!");
            return;
        }

        bool twoHanded = EquipmentSlots.IsTwoHanded(item.Type, item.TwoHanded);

        // Целевой слот: явный (из клиента) или первый подходящий
        string? targetSlot = equipEl.TryGetProperty("TargetSlot", out var ts) ? ts.GetString() : null;

        var validSlots = EquipmentSlots.SlotsForItemType(item.Type);
        List<string> slotsToFill;
        if (targetSlot != null)
        {
            if (!validSlots.Contains(targetSlot))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Предмет нельзя надеть в этот слот.");
                return;
            }
            if (twoHanded && targetSlot != EquipmentSlots.RightHand)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Двуручное оружие можно надеть только в правую руку.");
                return;
            }
            slotsToFill = new List<string> { targetSlot };
        }
        else
        {
            // Для оружия/колец — первая свободная; иначе первая подходящая
            if (item.Type == "weapon" || item.Type == "ring")
                slotsToFill = validSlots.Where(s => player.Equipment[s] == null).Take(1).ToList();
            else
                slotsToFill = validSlots.Take(1).ToList();

            if (slotsToFill.Count == 0)
                slotsToFill = validSlots.Take(1).ToList(); // все заняты — заменим первую
        }

        // Слот не должен быть заблокирован двуручным оружием
        foreach (var s in slotsToFill)
        {
            if (EquipmentSlots.IsBlockedByTwoHanded(s, player.Equipment))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Слот заблокирован двуручным оружием.");
                return;
            }
        }

        // Из стека надеваем ровно одну штуку; остаток остаётся в инвентаре.
        Item equipped;
        if (item.Quantity > 1)
        {
            item.Quantity -= 1;
            equipped = item.Clone();
            equipped.Id = Guid.NewGuid().ToString();
            equipped.Quantity = 1;
        }
        else
        {
            player.Inventory.Remove(item);
            equipped = item;
        }

        var returned = new List<Item>();
        foreach (var slot in slotsToFill)
        {
            var old = player.Equipment[slot];
            player.Equipment[slot] = equipped;
            if (old != null && old.Id != equipped.Id) returned.Add(old);
        }

        // Двуручное оружие освобождает левую руку
        if (twoHanded)
        {
            var leftOld = player.Equipment[EquipmentSlots.LeftHand];
            if (leftOld != null && leftOld.Id != equipped.Id)
            {
                player.Equipment[EquipmentSlots.LeftHand] = null;
                returned.Add(leftOld);
            }
        }

        foreach (var r in returned)
            InventoryHelper.AddItem(player, r);

        Log.Debug($"{player.Name} надел {equipped.Name} (слоты: {string.Join(",", slotsToFill)})");
        string msg = returned.Count > 0
            ? $"Вы надели {equipped.Name}, сняв {string.Join(", ", returned.Select(r => r.Name))}"
            : $"Вы надели {equipped.Name}";
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = msg }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
