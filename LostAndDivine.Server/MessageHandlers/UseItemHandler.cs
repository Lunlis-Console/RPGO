using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class UseItemHandler : BaseHandler
{
    public UseItemHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "������ ������������ �� ����� ������!"); return; }
        if (message.Data is not JsonElement useEl) return;

        string? useItemId = useEl.ValueKind == JsonValueKind.String
            ? useEl.GetString()
            : useEl.TryGetProperty("ItemId", out var uidProp) ? uidProp.GetString() : null;

        if (useItemId == null) return;

        var item = player.Inventory.FirstOrDefault(i => i.Id == useItemId);
        if (item == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "������� �� ������!");
            return;
        }

        if (item.Type == "consumable" && item.HealAmount > 0)
        {
            int effectiveMax = player.MaxHealth + player.Equipment.GetBonusMaxHealth();
            int healed = Math.Min(item.HealAmount, effectiveMax - player.Health);
            player.Health += healed;
            InventoryHelper.RemoveFromRecord(player, useItemId, 1);
            Log.Debug($"{player.Name} ����������� {item.Name}, ������������� {healed} HP");
            var healMsg = new GameMessage
            {
                Type = "heal",
                Data = new { Target = "player", PlayerName = player.Name, X = player.X, Y = player.Y, Amount = healed }
            };
            await SendToClient(connection, healMsg);
            await Hub.SendDamageNearbyAsync(player.X, player.Y, healMsg, player);
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = $"�� ������������ {item.Name}. ������������� {healed} HP. ({player.Health}/{effectiveMax})" }
            });
            await SendInventoryAndStatus(connection, player);
        }
        else if (item.Type == "consumable" && item.RestoreMana > 0)
        {
            if (player.Mana >= player.MaxMana)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "���� � ��� ������!");
                return;
            }
            int restored = Math.Min(item.RestoreMana, player.MaxMana - player.Mana);
            player.Mana += restored;
            InventoryHelper.RemoveFromRecord(player, useItemId, 1);
            Log.Debug($"{player.Name} ����������� {item.Name}, ������������� {restored} MP");
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = $"�� ������������ {item.Name}. ������������� {restored} MP. ({player.Mana}/{player.MaxMana})" }
            });
            await SendInventoryAndStatus(connection, player);
        }
        else
        {
            await SendError(connection, ErrorCodes.ItemNotEquippable, "���� ������� ������ ������������!");
        }
    }
}
