using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Заточка/усиление предмета: клиент шлёт ItemId (снаряжение) и StoneId (камень усиления).
/// Камень тратится в любом случае; при успехе растёт EnhancementLevel предмета.
/// Шанс успеха берётся из EnhancementHelper.SuccessChance(текущий+1).
/// </summary>
public class UpgradeItemHandler : BaseHandler
{
    public UpgradeItemHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? itemId = el.TryGetProperty("ItemId", out var iEl) ? iEl.GetString() : null;
        string? stoneId = el.TryGetProperty("StoneId", out var sEl) ? sEl.GetString() : null;
        if (itemId == null || stoneId == null) return;

        var item = player.Inventory.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "Предмет не найден в инвентаре!");
            return;
        }

        var stone = player.Inventory.FirstOrDefault(i => i.Id == stoneId);
        if (stone == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "Камень усиления не найден!");
            return;
        }

        if (stone.TypeEnum != ItemType.Material)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Это не камень усиления!");
            return;
        }

        bool isWeapon = !string.IsNullOrEmpty(item.WeaponSubtype) || item.TypeEnum == ItemType.Weapon;
        bool isGear = item.TypeEnum != ItemType.Material && item.TypeEnum != ItemType.Consumable
            && item.TypeEnum != ItemType.Collectible && item.TypeEnum != ItemType.Trophy;
        if (!isGear)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Можно усиливать только снаряжение!");
            return;
        }

        string requiredIcon = isWeapon ? "cristal_weapon" : "cristal_armor";
        if (stone.Icon != requiredIcon)
        {
            string need = isWeapon ? "оружия" : "брони";
            await SendError(connection, ErrorCodes.InvalidRequest, $"Нужен камень усиления {need}!");
            return;
        }

        if (!EnhancementHelper.CanEnhance(item))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Достигнут максимальный уровень заточки!");
            return;
        }

        int target = item.EnhancementLevel + 1;
        double chance = EnhancementHelper.SuccessChance(target);
        bool success = Random.Shared.NextDouble() * 100.0 < chance;

        // Камень тратится всегда (одна попытка).
        InventoryHelper.RemoveFromRecord(player, stoneId, 1);

        if (success)
        {
            item.EnhancementLevel = target;
            await SendChatToAsync(connection, ChatChannel.System, "Кузнец",
                $"Успех! «{item.Name}» заточен до +{item.EnhancementLevel} (шанс {chance:0.##}%).");
        }
        else
        {
            await SendChatToAsync(connection, ChatChannel.System, "Кузнец",
                $"Неудача. Камень усиления разрушен (шанс {chance:0.##}%).");
        }

        await SendInventoryAndStatus(connection, player);
    }
}


