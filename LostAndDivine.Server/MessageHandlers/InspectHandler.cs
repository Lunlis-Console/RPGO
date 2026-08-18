using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Осмотр другого игрока: характеристики и снаряжение (без денег, опыта и личных данных).
/// </summary>
public class InspectHandler : BaseHandler
{
    public InspectHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? targetName = el.TryGetProperty("TargetName", out var tn) ? tn.GetString() : null;
        if (string.IsNullOrEmpty(targetName))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Укажите игрока.");
            return;
        }

        if (!World.TryGetPlayerByName(targetName, out var target) || target == null)
        {
            await SendError(connection, ErrorCodes.TargetNotFound, "Игрок не найден.");
            return;
        }

        await SendToClient(connection, new GameMessage
        {
            Type = "inspect_response",
            Data = new
            {
                target.Name,
                ClassName = target.Class.DisplayName(),
                target.Level,
                target.Health,
                MaxHealth = target.MaxHealth + target.Equipment.GetBonusMaxHealth(),
                target.Mana,
                target.MaxMana,
                PhysAttack = GameServer.GetBuffedPhysAttack(target, Svc.Debuffs),
                MagAttack = GameServer.GetBuffedMagAttack(target, Svc.Debuffs),
                Defense = target.GetDefense(),
                Resistance = target.GetResistance(),
                CritChance = Math.Round(target.GetCritChance(), 2),
                CritDamage = Math.Round(target.GetCritDamage(), 2),
                EvadeChance = Math.Round(target.GetEvadeChance(), 2),
                BlockChance = Math.Round(target.GetBlockChance(), 2),
                ParryChance = Math.Round(target.GetParryChance(), 2),
                Accuracy = Math.Round(target.GetAccuracy(), 2),
                EquippedItems = target.Equipment.Slots
                    .Where(kv => kv.Value != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!),
                target.Strength,
                Endurance = target.Endurance,
                target.Agility,
                target.Cunning,
                Intellect = target.Intellect,
                target.Wisdom,
                AttackSpeed = GameServer.GetAttackSpeed(target, Svc.Debuffs),
                AttackIntervalMs = GameServer.GetAttackIntervalMs(target, Svc.Debuffs),
                WeaponSpeedModifier = target.Equipment.GetWeaponSpeedModifier()
            }
        });
    }
}
