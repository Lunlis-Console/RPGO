using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class InventoryRequestHandler : BaseHandler
{
    public InventoryRequestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        await SendToClient(connection, new GameMessage
        {
            Type = "inventory_response",
            Data = new
            {
                Items = player.Inventory,
                Gold = player.Gold,
                Equipment = new
                {
                    Slots = player.Equipment.Slots
                        .Where(kv => kv.Value != null)
                        .ToDictionary(kv => kv.Key, kv => kv.Value!)
                },
                BonusPhysAttack = player.Equipment.GetBonusPhysAttack(),
                BonusMagAttack = player.Equipment.GetBonusMagAttack(),
                BonusDefense = player.Equipment.GetBonusDefense(),
                BonusMaxHealth = player.Equipment.GetBonusMaxHealth()
            }
        });
    }
}
