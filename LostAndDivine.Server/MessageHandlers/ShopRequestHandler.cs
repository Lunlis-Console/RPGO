using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class ShopRequestHandler : BaseHandler
{
    public ShopRequestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        await SendToClient(connection, new GameMessage
        {
            Type = GameMessageType.ShopResponse,
            Data = new
            {
                MerchantX = Svc.Merchant.MerchantX,
                MerchantY = Svc.Merchant.MerchantY,
                MerchantName = "Торговец",
                Discount = 0,
                Items = Svc.Merchant.ShopItems.Select(i => new
                {
                    i.Id, i.Name, i.Type, i.WeaponSubtype,
                    Value = Balance.BuyPrice(i.Value),
                    OriginalValue = i.Value,
                    i.MaxHealthBonus, i.HealAmount, i.RestoreMana, i.Description,
                    Stock = Svc.Merchant.GetStock(i.Id),
                    IsBuyback = false
                }).ToList(),
                Buyback = player.BuybackItems.Select(i => new
                {
                    i.Id, i.Name, i.Type, i.WeaponSubtype,
                    Value = Balance.BuybackPrice(i.Value),
                    OriginalValue = i.Value,
                    i.MaxHealthBonus, i.HealAmount, i.RestoreMana, i.Description,
                    i.Quantity, IsBuyback = true
                }).ToList(),
                PlayerGold = player.Gold
            }
        });
    }
}
