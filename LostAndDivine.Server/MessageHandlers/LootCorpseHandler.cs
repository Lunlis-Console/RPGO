using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class LootCorpseHandler : BaseHandler
{
    public LootCorpseHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement data) return;

        Guid corpseId;
        if (data.TryGetProperty("CorpseId", out var cidEl) && cidEl.ValueKind == JsonValueKind.String)
            corpseId = Guid.Parse(cidEl.GetString()!);
        else
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "�������� ID �����");
            return;
        }

        var corpse = Svc.Corpses.FindCorpseById(corpseId);
        if (corpse == null)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "���� �� ������ ��� ��� ������");
            return;
        }

        int dist = Math.Abs(player.X - corpse.X) + Math.Abs(player.Y - corpse.Y);
        if (dist > 1)
        {
            player.Movement.Path.Clear();

            // �������� ������ ������ �� 4 ������ ����� � ������ (cardinal, �� �� ���������)
            int[] dx = { 0, 0, -1, 1 };
            int[] dy = { -1, 1, 0, 0 };
            int bestX = -1, bestY = -1;
            int bestDist = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                int nx = corpse.X + dx[i];
                int ny = corpse.Y + dy[i];
                if (nx < 0 || nx >= World.Map.Width || ny < 0 || ny >= World.Map.Height) continue;
                if (Svc.Monsters.FindMonsterAt(nx, ny) != null) continue;
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
                await SendError(connection, ErrorCodes.InvalidRequest, "��� ��������� ������ ����� � ������");
                return;
            }

            var path = Svc.Pathfinding.FindPath(player.X, player.Y, bestX, bestY, player.CurrentZoneId);
            if (path.Count == 0 && (player.X != bestX || player.Y != bestY))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "���������� ������� � �����");
                return;
            }
            player.Movement.Path = path;
            player.Interaction.SetPending("loot_corpse");
            player.Interaction.CorpseId = corpseId;
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = "�� ��������� � �����..." }
            });
            return;
        }

        // ��������: ������ ��������� ��� (��� ����� �� ����) ����� ������
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
                await SendError(connection, ErrorCodes.InvalidRequest, "�� �� ����������� � ���� ���!");
                return;
            }
        }

        await LootCorpseAsync(connection, player, corpse, Hub, Svc.Corpses);
    }

    public static async Task LootCorpseAsync(ClientConnection connection, Player player, MonsterCorpse corpse, INetworkHub hub, CorpseManager corpses)
    {
        if (!corpse.PlayerLoot.TryGetValue(player.Id, out var myLoot) || myLoot == null)
        {
            Log.Debug($"{player.Name} ������ ���� {corpse.MonsterName} � ��� ������������� ����");
            await hub.SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = "� ����� ����� ��� ���� ��� ���." }
            });
            await hub.SendInventoryAndStatus(connection, player);
            return;
        }

        if (myLoot.Gold == 0 && myLoot.Items.Count == 0)
        {
            TryRemoveCorpseIfEmpty(corpse, corpses);
            Log.Debug($"{player.Name} ������ ���� {corpse.MonsterName} � ��� ��� ������");
            await hub.SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = "�� ��� ������� ���� ���� ���." }
            });
            await hub.SendInventoryAndStatus(connection, player);
            return;
        }

        if (myLoot.Items.Count == 0 && myLoot.Gold > 0)
        {
            int gold = myLoot.Gold;
            player.Gold += gold;
            myLoot.Gold = 0;
            TryRemoveCorpseIfEmpty(corpse, corpses);
            string pctText = myLoot.DamagePercent > 0 ? $" ({myLoot.DamagePercent}% �����)" : "";
            Log.Info($"{player.Name} ������ {gold} ���. � ����� {corpse.MonsterName}{pctText} (������ ������, ��������� ���)");
            await hub.SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = $"�� ������� {gold} ������{pctText}." }
            });
            await hub.SendInventoryAndStatus(connection, player);
            return;
        }

        Log.Debug($"{player.Name} ������ ���� {corpse.MonsterName}: {myLoot.Items.Count} �����., {myLoot.Gold} ���.");
        await hub.SendToClient(connection, new GameMessage
        {
            Type = "loot_corpse",
            Data = new
            {
                CorpseId = corpse.Id.ToString(),
                MonsterName = corpse.MonsterName,
                Gold = myLoot.Gold,
                DamagePercent = myLoot.DamagePercent,
                Items = myLoot.Items.Select(i => MakeItemPayload(i)).ToList()
            }
        });
    }

    private static void TryRemoveCorpseIfEmpty(MonsterCorpse corpse, CorpseManager corpses)
    {
        bool allEmpty = corpse.PlayerLoot.Count > 0
            ? corpse.PlayerLoot.Values.All(v => v.Gold == 0 && v.Items.Count == 0)
            : corpse.Loot.Count == 0 && corpse.GoldReward == 0;
        if (allEmpty)
            corpses.RemoveCorpse(corpse.Id);
    }
}
