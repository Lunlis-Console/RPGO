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
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "������ ������ �� ����� ������!"); return; }
        if (message.Data is not JsonElement equipEl) return;

        string? equipItemId = equipEl.ValueKind == JsonValueKind.String
            ? equipEl.GetString()
            : equipEl.TryGetProperty("ItemId", out var eidProp) ? eidProp.GetString() : null;

        if (equipItemId == null) return;

        var item = player.Inventory.FirstOrDefault(i => i.Id == equipItemId);
        if (item == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "������� �� ������!");
            return;
        }

        if (!EquipmentSlots.IsEquippableType(item.Type))
        {
            await SendError(connection, ErrorCodes.ItemNotEquippable, "���� ������� ������ ������!");
            return;
        }

        if (item.RequiredLevel > player.Level)
        {
            await SendError(connection, ErrorCodes.ItemLevelTooLow, $"��������� ������� {item.RequiredLevel}!");
            return;
        }

        bool twoHanded = EquipmentSlots.IsTwoHanded(item.Type, item.TwoHanded);

        // ������� ����: ����� (�� �������) ��� ������ ����������
        string? targetSlot = equipEl.TryGetProperty("TargetSlot", out var ts) ? ts.GetString() : null;

        var validSlots = EquipmentSlots.SlotsForItemType(item.Type);
        List<string> slotsToFill;
        if (targetSlot != null)
        {
            if (!validSlots.Contains(targetSlot))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������� ������ ������ � ���� ����.");
                return;
            }
            if (twoHanded && targetSlot != EquipmentSlots.RightHand)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "��������� ������ ����� ������ ������ � ������ ����.");
                return;
            }
            slotsToFill = new List<string> { targetSlot };
        }
        else
        {
            // ��� ������/����� � ������ ���������; ����� ������ ����������
            if (item.Type == "weapon" || item.Type == "ring")
                slotsToFill = validSlots.Where(s => player.Equipment[s] == null).Take(1).ToList();
            else
                slotsToFill = validSlots.Take(1).ToList();

            if (slotsToFill.Count == 0)
                slotsToFill = validSlots.Take(1).ToList(); // ��� ������ � ������� ������
        }

        // ���� �� ������ ���� ������������ ��������� �������
        foreach (var s in slotsToFill)
        {
            if (EquipmentSlots.IsBlockedByTwoHanded(s, player.Equipment))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "���� ������������ ��������� �������.");
                return;
            }
        }

        // �� ����� �������� ����� ���� �����; ������� ������� � ���������.
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

        // ��������� ������ ����������� ����� ����
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

        Log.Debug($"{player.Name} ����� {equipped.Name} (�����: {string.Join(",", slotsToFill)})");
        string msg = returned.Count > 0
            ? $"�� ������ {equipped.Name}, ���� {string.Join(", ", returned.Select(r => r.Name))}"
            : $"�� ������ {equipped.Name}";
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "�������", Text = msg }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
