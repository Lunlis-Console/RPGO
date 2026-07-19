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
