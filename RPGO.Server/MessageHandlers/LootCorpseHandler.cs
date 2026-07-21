using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class LootCorpseHandler : BaseHandler
{
    public LootCorpseHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement data) return;

        Guid corpseId;
        if (data.TryGetProperty("CorpseId", out var cidEl) && cidEl.ValueKind == JsonValueKind.String)
            corpseId = Guid.Parse(cidEl.GetString()!);
        else
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Неверный ID трупа");
            return;
        }

        var corpse = CorpseManager.FindCorpseById(corpseId);
        if (corpse == null)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Труп не найден или уже собран");
            return;
        }

        int dist = Math.Abs(player.X - corpse.X) + Math.Abs(player.Y - corpse.Y);
        if (dist > 1)
        {
            player.Movement.Path.Clear();

            // Выбираем лучшую клетку из 4 сторон рядом с трупом (cardinal, не по диагонали)
            int[] dx = { 0, 0, -1, 1 };
            int[] dy = { -1, 1, 0, 0 };
            int bestX = -1, bestY = -1;
            int bestDist = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                int nx = corpse.X + dx[i];
                int ny = corpse.Y + dy[i];
                if (nx < 0 || nx >= World.Map.Width || ny < 0 || ny >= World.Map.Height) continue;
                if (MonsterManager.FindMonsterAt(nx, ny) != null) continue;
                int d = Math.Abs(nx - player.X) + Math.Abs(ny - player.Y);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestX = nx;
                    bestY = ny;
                }
            }

            if (bestX < 0)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Нет свободной клетки рядом с трупом");
                return;
            }

            var path = Pathfinding.FindPath(player.X, player.Y, bestX, bestY);
            if (path.Count == 0 && (player.X != bestX || player.Y != bestY))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Невозможно подойти к трупу");
                return;
            }
            player.Movement.Path = path;
            player.Interaction.SetPending("loot_corpse");
            player.Interaction.CorpseId = corpseId;
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = "Вы подходите к трупу..." }
            });
            return;
        }

        // Проверка: только участники боя (или члены их пати) могут лутать
        if (corpse.Contributors.Count > 0 && !corpse.Contributors.ContainsKey(player.Id))
        {
            bool sameParty = false;
            foreach (var contribId in corpse.Contributors.Keys)
            {
                if (World.TryGetPlayer(contribId, out var contrib) && contrib != null
                    && player.PartyId.HasValue && contrib.PartyId == player.PartyId)
                {
                    sameParty = true;
                    break;
                }
            }
            if (!sameParty)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Вы не участвовали в этом бою!");
                return;
            }
        }

        await LootCorpseAsync(connection, player, corpse);
    }

    public static async Task LootCorpseAsync(ClientConnection connection, Player player, MonsterCorpse corpse)
    {
        if (!corpse.PlayerLoot.TryGetValue(player.Id, out var myLoot) || myLoot == null)
        {
            Log.Debug($"{player.Name} открыл труп {corpse.MonsterName} — нет персонального лута");
            await Program.Hub.SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = "У этого трупа нет лута для вас." }
            });
            await Program.Hub.SendInventoryAndStatus(connection, player);
            return;
        }

        if (myLoot.Gold == 0 && myLoot.Items.Count == 0)
        {
            TryRemoveCorpseIfEmpty(corpse);
            Log.Debug($"{player.Name} открыл труп {corpse.MonsterName} — лут уже забран");
            await Program.Hub.SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = "Вы уже забрали весь свой лут." }
            });
            await Program.Hub.SendInventoryAndStatus(connection, player);
            return;
        }

        if (myLoot.Items.Count == 0 && myLoot.Gold > 0)
        {
            int gold = myLoot.Gold;
            player.Gold += gold;
            myLoot.Gold = 0;
            TryRemoveCorpseIfEmpty(corpse);
            string pctText = myLoot.DamagePercent > 0 ? $" ({myLoot.DamagePercent}% урона)" : "";
            Log.Info($"{player.Name} забрал {gold} зол. с трупа {corpse.MonsterName}{pctText} (только золото, предметов нет)");
            await Program.Hub.SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = $"Вы забрали {gold} золота{pctText}." }
            });
            await Program.Hub.SendInventoryAndStatus(connection, player);
            return;
        }

        Log.Debug($"{player.Name} открыл труп {corpse.MonsterName}: {myLoot.Items.Count} предм., {myLoot.Gold} зол.");
        await Program.Hub.SendToClient(connection, new GameMessage
        {
            Type = "loot_corpse",
            Data = new
            {
                CorpseId = corpse.Id.ToString(),
                MonsterName = corpse.MonsterName,
                Gold = myLoot.Gold,
                DamagePercent = myLoot.DamagePercent,
                Items = myLoot.Items.Select(i => new
                {
                    i.Id, i.Name, i.Type, i.Value, i.Description
                }).ToList()
            }
        });
    }

    private static void TryRemoveCorpseIfEmpty(MonsterCorpse corpse)
    {
        bool allEmpty = corpse.PlayerLoot.Count > 0
            ? corpse.PlayerLoot.Values.All(v => v.Gold == 0 && v.Items.Count == 0)
            : corpse.Loot.Count == 0 && corpse.GoldReward == 0;
        if (allEmpty)
            CorpseManager.RemoveCorpse(corpse.Id);
    }
}
