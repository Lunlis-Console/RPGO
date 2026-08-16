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
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя использовать во время обмена!"); return; }
        if (message.Data is not JsonElement useEl) return;

        string? useItemId = useEl.ValueKind == JsonValueKind.String
            ? useEl.GetString()
            : useEl.TryGetProperty("ItemId", out var uidProp) ? uidProp.GetString() : null;

        if (useItemId == null) return;

        var item = player.Inventory.FirstOrDefault(i => i.Id == useItemId);
        if (item == null)
        {
            await SendError(connection, ErrorCodes.ItemNotFound, "Предмет не найден!");
            return;
        }

        if (item.Type == "consumable" && item.HealAmount > 0)
        {
            int effectiveMax = player.MaxHealth + player.Equipment.GetBonusMaxHealth();
            int healed = Math.Min(item.HealAmount, effectiveMax - player.Health);
            player.Health += healed;
            InventoryHelper.RemoveFromRecord(player, useItemId, 1);
            Log.Debug($"{player.Name} использовал {item.Name}, восстановлено {healed} HP");
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
                Data = new { Name = "Система", Text = $"Вы использовали {item.Name}. Восстановлено {healed} HP. ({player.Health}/{effectiveMax})" }
            });
            await SendInventoryAndStatus(connection, player);
            await ReportUseQuest(connection, player, item);
        }
        else if (item.Type == "consumable" && item.RestoreMana > 0)
        {
            if (player.Mana >= player.MaxMana)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Мана и так полная!");
                return;
            }
            int restored = Math.Min(item.RestoreMana, player.MaxMana - player.Mana);
            player.Mana += restored;
            InventoryHelper.RemoveFromRecord(player, useItemId, 1);
            Log.Debug($"{player.Name} использовал {item.Name}, восстановлено {restored} MP");
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = $"Вы использовали {item.Name}. Восстановлено {restored} MP. ({player.Mana}/{player.MaxMana})" }
            });
            await SendInventoryAndStatus(connection, player);
            await ReportUseQuest(connection, player, item);
        }
        else
        {
            await SendError(connection, ErrorCodes.ItemNotEquippable, "Этот предмет нельзя использовать!");
        }
    }

    /// <summary>Прогресс use-квестов после успешного использования предмета.</summary>
    private async Task ReportUseQuest(ClientConnection connection, Player player, Item item)
    {
        string itemId = string.IsNullOrEmpty(item.TemplateId) ? item.Id : item.TemplateId!;
        var results = Svc.Quests.IncrementUseProgress(player, itemId);
        if (results.Count == 0) return;
        foreach (var (title, current, target, completed) in results)
        {
            string msg = completed
                ? $"[Задание] {title}: {current}/{target} — задание выполнено!"
                : $"[Задание] {title}: {current}/{target}";
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = msg }
            });
        }
        await SendQuestLog(connection, player);
    }
}
