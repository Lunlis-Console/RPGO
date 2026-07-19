using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class TakeLootHandler : BaseHandler
{
    public TakeLootHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement data) return;

        Guid corpseId;
        if (!data.TryGetProperty("CorpseId", out var cidEl) || cidEl.ValueKind != JsonValueKind.String)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Неверный ID трупа");
            return;
        }
        corpseId = Guid.Parse(cidEl.GetString()!);

        var corpse = CorpseManager.FindCorpseById(corpseId);
        if (corpse == null)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Труп не найден или уже собран");
            return;
        }

        int dist = Math.Abs(player.X - corpse.X) + Math.Abs(player.Y - corpse.Y);
        if (dist > 1)
        {
            bool takeAllInner = data.TryGetProperty("TakeAll", out var taEl2) && taEl2.ValueKind == JsonValueKind.True;
            bool takeGoldInner = data.TryGetProperty("TakeGold", out var tgEl2) && tgEl2.ValueKind == JsonValueKind.True;
            var selectedIds = new List<string>();
            if (data.TryGetProperty("ItemIds", out var idsEl2) && idsEl2.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in idsEl2.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                        selectedIds.Add(el.GetString()!);
                }
            }

            player.Movement.Path.Clear();
            int tx = corpse.X - Math.Sign(corpse.X - player.X);
            int ty = corpse.Y - Math.Sign(corpse.Y - player.Y);
            tx = Math.Clamp(tx, 0, World.Map.Width - 1);
            ty = Math.Clamp(ty, 0, World.Map.Height - 1);
            var path = Pathfinding.FindPath(player.X, player.Y, tx, ty);
            if (path.Count == 0 && (player.X != tx || player.Y != ty))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Невозможно подойти к трупу");
                return;
            }
            player.Movement.Path = path;
            player.Interaction.SetPending("take_loot");
            player.Interaction.CorpseId = corpseId;
            player.Interaction.TakeAll = takeAllInner;
            player.Interaction.TakeGold = takeGoldInner;
            player.Interaction.ItemIds = selectedIds;
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = "Вы подходите к трупу..." }
            });
            return;
        }

        bool takeAll = false;
        if (data.TryGetProperty("TakeAll", out var taEl) && taEl.ValueKind == JsonValueKind.True)
            takeAll = true;

        bool takeGold = false;
        if (data.TryGetProperty("TakeGold", out var tgEl) && tgEl.ValueKind == JsonValueKind.True)
            takeGold = true;

        List<string> selectedIds2 = new();
        if (data.TryGetProperty("ItemIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in idsEl.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                    selectedIds2.Add(el.GetString()!);
            }
        }

        // Определяем персональный лут
        if (!corpse.PlayerLoot.TryGetValue(player.Id, out var myLoot) || myLoot == null)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "У этого трупа нет лута для вас");
            return;
        }

        List<string> takenNames = new();

        if (takeGold && myLoot.Gold > 0)
        {
            player.Gold += myLoot.Gold;
            takenNames.Add($"{myLoot.Gold} золота");
            myLoot.Gold = 0;
        }

        var itemsToTake = takeAll
            ? myLoot.Items.ToList()
            : myLoot.Items.Where(i => selectedIds2.Contains(i.Id)).ToList();

        foreach (var item in itemsToTake)
        {
            player.Inventory.Add(item);
            myLoot.Items.Remove(item);
            takenNames.Add(item.Name);
        }

        // Удаляем труп если у всех игроков лут пустой
        bool allEmpty = corpse.PlayerLoot.Count > 0
            ? corpse.PlayerLoot.Values.All(v => v.Gold == 0 && v.Items.Count == 0)
            : corpse.Loot.Count == 0 && corpse.GoldReward == 0;
        if (allEmpty)
            CorpseManager.RemoveCorpse(corpse.Id);

        string lootText = takenNames.Count > 0
            ? string.Join(", ", takenNames)
            : "Ничего не выбрано";

        string pctText = myLoot.DamagePercent > 0 ? $" ({myLoot.DamagePercent}% урона)" : "";
        Log.Info($"{player.Name} забрал с трупа {corpse.MonsterName}: {lootText} | Доля: {pctText} | Остаток: {myLoot.Gold} зол., {myLoot.Items.Count} предм. | Труп {(allEmpty ? "удалён" : "остался")}");

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы забрали: {lootText}" }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
