using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class StatusHandler : BaseHandler
{
    public StatusHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        await SendToClient(connection, new GameMessage
        {
            Type = "status_response",
            Data = new
            {
                player.Name,
                player.Level,
                player.Health,
                MaxHealth = player.MaxHealth + player.Equipment.GetBonusMaxHealth(),
                Mana = player.Mana,
                MaxMana = player.MaxMana,
                BaseAttack = player.GetBaseDamage(),
                BaseDefense = player.GetBaseDefense(),
                TotalAttack = player.GetTotalAttack(),
                TotalDefense = player.GetTotalDefense(),
                CritChance = Math.Round(player.GetCritChance(), 2),
                CritDamage = Math.Round(player.GetCritDamage(), 2),
                EvadeChance = Math.Round(player.GetEvadeChance(), 2),
                player.Gold,
                player.X,
                player.Y,
                player.Experience,
                WeaponName = player.Equipment.Weapon?.Name ?? "нет",
                ArmorName = player.Equipment.Armor?.Name ?? "нет",
                AccessoryName = player.Equipment.Accessory?.Name ?? "нет",
                player.Strength,
                player.Stamina,
                player.Agility,
                player.Cunning,
                player.Wisdom,
                player.Will,
                player.AttributePoints,
                player.Speed,
                MoveIntervalMs = Balance.MoveIntervalMs(player.Speed),
                AttackSpeed = Balance.GetAttackSpeed(player.Agility),
                AttackIntervalMs = Balance.AttackIntervalMs(Balance.GetAttackSpeed(player.Agility)),
                Breakdown = BuildBreakdown(player)
            }
        });
    }
}
