using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class SellAllTrophiesHandler : BaseHandler
{
    public SellAllTrophiesHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя продавать во время обмена!"); return; }

        var trophies = player.Inventory.Where(i => i.Type == "trophy").ToList();
        if (trophies.Count == 0)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "У вас нет трофеев для продажи!");
            return;
        }

        int totalQty = trophies.Sum(i => i.Quantity);
        int totalGain = 0;
        foreach (var item in trophies)
        {
            int sellPrice = Balance.SellPrice(item.Value);
            player.Gold += sellPrice * item.Quantity;
            totalGain += sellPrice * item.Quantity;
            player.Inventory.Remove(item);
        }

        var first = trophies.First();
        var buybackCopy = new Item
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = first.TemplateId,
            Name = first.Name,
            Type = first.Type,
            Value = first.Value,
            MaxHealthBonus = first.MaxHealthBonus,
            HealAmount = first.HealAmount,
            Description = first.Description,
            MaxStack = first.MaxStack,
            Quantity = totalQty,
            BonusStrength = first.BonusStrength,
            BonusEndurance = first.BonusEndurance,
            BonusAgility = first.BonusAgility,
            BonusCunning = first.BonusCunning,
            BonusIntellect = first.BonusIntellect,
            BonusWisdom = first.BonusWisdom,
            BonusPhysAttack = first.BonusPhysAttack,
            BonusMagAttack = first.BonusMagAttack,
            BonusDefense = first.BonusDefense,
            BonusResistance = first.BonusResistance,
            BonusCritChance = first.BonusCritChance,
            BonusCritDamage = first.BonusCritDamage,
            BonusEvadeChance = first.BonusEvadeChance,
            BonusAttackSpeed = first.BonusAttackSpeed,
            TwoHanded = first.TwoHanded,
            DamageType = first.DamageType,
            AttackSpeedModifier = first.AttackSpeedModifier
        };
        player.BuybackItems.Add(buybackCopy);

        Log.Info($"{player.Name} продал все трофеи x{totalQty} за {totalGain} золота");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы продали все трофеи x{totalQty} за {totalGain} золота" }
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
                    b.MaxHealthBonus, b.HealAmount, b.Description,
                    IsBuyback = true
                }).ToList()
            }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
