using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class EquipHandler : BaseHandler
{
    public EquipHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя надеть во время обмена!"); return; }
        if (message.Data is not JsonElement equipEl) return;

        string? equipItemId = equipEl.ValueKind == JsonValueKind.String
            ? equipEl.GetString()
            : equipEl.TryGetProperty("ItemId", out var eidProp) ? eidProp.GetString() : null;

        // Перенос надетого предмета между слотами (drag-n-drop в окне снаряжения)
        string? fromSlot = equipEl.ValueKind != JsonValueKind.String
            && equipEl.TryGetProperty("FromSlot", out var fsProp) ? fsProp.GetString() : null;

        Item? item;
        if (!string.IsNullOrEmpty(fromSlot))
        {
            item = player.Equipment[fromSlot];
            if (item == null)
            {
                await SendError(connection, ErrorCodes.SlotEmpty, "Слот пуст — нечего перемещать.");
                return;
            }
            // Освобождаем источник до проверок (целевой слот может зависеть от этого)
            player.Equipment[fromSlot] = null;
        }
        else
        {
            if (equipItemId == null) return;

            item = player.Inventory.FirstOrDefault(i => i.Id == equipItemId);
            if (item == null)
            {
                await SendError(connection, ErrorCodes.ItemNotFound, "Предмет не найден!");
                return;
            }
        }

        if (!EquipmentSlots.IsEquippableType(item.Type))
        {
            await SendError(connection, ErrorCodes.ItemNotEquippable, "Этот предмет нельзя надеть!");
            return;
        }

        if (item.RequiredLevel > player.Level)
        {
            await SendError(connection, ErrorCodes.ItemLevelTooLow, $"Требуется уровень {item.RequiredLevel}!");
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
            if (item.TypeEnum == ItemType.Weapon || item.TypeEnum == ItemType.Ring)
                slotsToFill = validSlots.Where(s => player.Equipment[s] == null).Take(1).ToList();
            else
                slotsToFill = validSlots.Take(1).ToList();

            if (slotsToFill.Count == 0)
                slotsToFill = validSlots.Take(1).ToList(); // все заняты — заменим первую
        }

        // Слот не должен быть заблокирован двуручным оружием.
        // Одноручное оружие при этом не вызывает ошибку, а заменяет двуручное в правой руке.
        for (int i = 0; i < slotsToFill.Count; i++)
        {
            if (EquipmentSlots.IsBlockedByTwoHanded(slotsToFill[i], player.Equipment))
            {
                if (!twoHanded && item.TypeEnum == ItemType.Weapon)
                    slotsToFill[i] = EquipmentSlots.RightHand;
                else
                {
                    await SendError(connection, ErrorCodes.InvalidRequest, "Слот заблокирован двуручным оружием.");
                    return;
                }
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
            Type = GameMessageType.Chat,
            Data = new { Name = "Система", Text = msg }
        });
        await SendInventoryAndStatus(connection, player);
    }
}


