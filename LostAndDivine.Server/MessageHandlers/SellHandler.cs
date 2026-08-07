using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class SellHandler : BaseHandler
{
    public SellHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "������ ��������� �� ����� ������!"); return; }
        if (message.Data is not JsonElement sellEl) return;

        string? sellItemId = sellEl.ValueKind == JsonValueKind.String
            ? sellEl.GetString()
            : sellEl.TryGetProperty("ItemId", out var sidProp) ? sidProp.GetString() : null;
        int qty = 1;
        if (sellEl.TryGetProperty("Quantity", out var sqtyProp) && sqtyProp.ValueKind == JsonValueKind.Number)
            qty = Math.Max(1, sqtyProp.GetInt32());

        if (sellItemId == null) return;

        var first = player.Inventory.FirstOrDefault(i => i.Id == sellItemId);
        if (first == null)
        {
            await SendError(connection, ErrorCodes.ItemNotInInventory, "������� �� ������ � ����� ���������!");
            return;
        }

        // ������� ��������� ���������� �� ���� ���������� ������� ���������
        // (��������� �������� ����� ���� � ���������� �������, ���� � ��� ��� TemplateId
        //  ��� ���� ���������� ��������� MaxStack).
        string? tid = null;
        bool hasTemplate = !string.IsNullOrEmpty(first.TemplateId);
        if (hasTemplate)
            tid = first.TemplateId!;

        int available = player.Inventory
            .Where(i => hasTemplate ? i.TemplateId == tid : (i.Name == first.Name && i.Type == first.Type))
            .Sum(i => i.Quantity);

        int toSell = Math.Min(qty, available);
        if (toSell <= 0) return;

        int sellPrice = Balance.SellPrice(first.Value);
        int totalGain = toSell * sellPrice;
        player.Gold += totalGain;

        // ��������� ��������� ���������� �� ���� ���������� �������
        int remaining = toSell;
        foreach (var item in player.Inventory.ToList())
        {
            if (remaining <= 0) break;
            bool matches = hasTemplate ? item.TemplateId == tid
                : (item.Name == first.Name && item.Type == first.Type);
            if (!matches) continue;
            if (item.Quantity <= remaining)
            {
                remaining -= item.Quantity;
                player.Inventory.Remove(item);
            }
            else
            {
                item.Quantity -= remaining;
                remaining = 0;
            }
        }

        var buybackCopy = first.Clone();
        buybackCopy.Id = Guid.NewGuid().ToString();
        buybackCopy.Quantity = toSell;
        player.BuybackItems.Add(buybackCopy);
        Log.Info($"{player.Name} ������ {first.Name} x{toSell} �� {totalGain} ������");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "�������", Text = $"�� ������� {first.Name} x{toSell} �� {totalGain} ������" }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "shop_update",
            Data = new
            {
                PlayerGold = player.Gold,
                Buyback = player.BuybackItems.Select(b => new
                {
                    b.Id, b.Name, b.Type,
                    Value = Balance.BuybackPrice(b.Value),
                    OriginalValue = b.Value,
                    b.MaxHealthBonus, b.HealAmount, b.RestoreMana, b.Description,
                    b.Quantity, IsBuyback = true
                }).ToList()
            }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
