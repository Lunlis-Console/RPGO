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
                PhysAttack = player.GetPhysAttack(),
                MagAttack = player.GetMagAttack(),
                Defense = player.GetDefense(),
                Resistance = player.GetResistance(),
                CritChance = Math.Round(player.GetCritChance(), 2),
                CritDamage = Math.Round(player.GetCritDamage(), 2),
                EvadeChance = Math.Round(player.GetEvadeChance(), 2),
                player.Gold,
                player.X,
                player.Y,
                player.Experience,
                Equipped = player.Equipment.Slots
                    .Where(kv => kv.Value != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!.Name),
                player.Strength,
                Endurance = player.Endurance,
                player.Agility,
                player.Cunning,
                Intellect = player.Intellect,
                player.Wisdom,
                player.AttributePoints,
                player.Speed,
                MoveIntervalMs = Balance.MoveIntervalMs(player.Speed),
                AttackSpeed = Program.GetAttackSpeed(player),
                AttackIntervalMs = Balance.AttackIntervalMs(Balance.GetAttackSpeed(player.Agility), player.Equipment.GetWeaponSpeedModifier()),
                WeaponDamageType = player.Equipment.GetWeaponDamageType(),
                WeaponSpeedModifier = player.Equipment.GetWeaponSpeedModifier(),
                IsDualWielding = player.Equipment.IsDualWielding(),
                Breakdown = BuildBreakdown(player),
                ActiveDebuffs = player.ActiveDebuffs.Select(d => new
                {
                    Type = d.Type.ToString(),
                    d.DisplayName,
                    Value = Math.Round(d.Value, 2),
                    d.RemainingMs,
                    DurationMs = d.DurationMs
                }).ToList()
            }
        });
    }
}
