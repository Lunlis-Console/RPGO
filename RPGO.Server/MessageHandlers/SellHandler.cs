using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class SellHandler : BaseHandler
{
    public SellHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя продавать во время обмена!"); return; }
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
            await SendError(connection, ErrorCodes.ItemNotInInventory, "Предмет не найден в вашем инвентаре!");
            return;
        }

        int available = first.Quantity;
        int toSell = Math.Min(qty, available);
        if (toSell <= 0) return;

        int sellPrice = Balance.SellPrice(first.Value);
        int totalGain = 0;
        for (int i = 0; i < toSell; i++)
        {
            player.Gold += sellPrice;
            totalGain += sellPrice;
        }

        InventoryHelper.RemoveFromRecord(player, sellItemId, toSell);

        var buybackCopy = new Item
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = first.TemplateId,
            Name = first.Name,
            Type = first.Type,
            Value = first.Value,
            Attack = first.Attack,
            Defense = first.Defense,
            MaxHealthBonus = first.MaxHealthBonus,
            HealAmount = first.HealAmount,
            Description = first.Description,
            MaxStack = first.MaxStack,
            Quantity = toSell,
            BonusStrength = first.BonusStrength,
            BonusStamina = first.BonusStamina,
            BonusAgility = first.BonusAgility,
            BonusCunning = first.BonusCunning,
            BonusWisdom = first.BonusWisdom,
            BonusWill = first.BonusWill,
            BonusCritChance = first.BonusCritChance,
            BonusCritDamage = first.BonusCritDamage,
            BonusEvadeChance = first.BonusEvadeChance
        };
        player.BuybackItems.Add(buybackCopy);
        Log.Info($"{player.Name} продал {first.Name} x{toSell} за {totalGain} золота");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы продали {first.Name} x{toSell} за {totalGain} золота" }
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
                    b.Attack, b.Defense, b.MaxHealthBonus, b.HealAmount, b.Description,
                    IsBuyback = true
                }).ToList()
            }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
