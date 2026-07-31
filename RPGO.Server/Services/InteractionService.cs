using RPGGame.Server.MessageHandlers;
using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server;

/// <summary>
/// Обработка взаимодействий: магазин, монстр, доска, NPC, сбор, лут, обмен.
/// Вынесена из GameServerHost.
/// </summary>
public class InteractionService
{
    private readonly Lazy<GameServices> _svcLazy;
    private GameServices _svc => _svcLazy.Value;

    public InteractionService(Lazy<GameServices> svc)
    {
        _svcLazy = svc;
    }

    private Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
    {
        if (conn == null) return Task.CompletedTask;
        return _svc.Hub.SendChatToAsync(conn, channel, name, text);
    }

    public async Task ProcessPendingInteraction(Player player, string interactionType)
    {
        var client = _svc.World.FindClientByPlayer(player);
        if (client == null) return;

        switch (interactionType)
        {
            case "monster":
                Monster? monster = null;
                if (player.Interaction.MonsterId != null)
                    monster = _svc.Monsters.FindMonsterById(player.Interaction.MonsterId.Value);
                if (monster == null)
                    monster = _svc.Monsters.FindMonsterAt(player.Interaction.X, player.Interaction.Y);
                if (monster != null && monster.Health > 0)
                {
                    player.Combat.Enter(monster.Id, player.Movement);
                    Log.Debug($"{player.Name} начал бой с {monster.Name}");
                    await _svc.Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Бой", Text = $"Бой: {monster.Name} [{monster.Level}] ({monster.Health}/{monster.MaxHealth})" }
                    });
                    await _svc.Hub.SendToClient(client, new GameMessage
                    {
                        Type = "combat_state",
                        Data = new
                        {
                            InCombat = true,
                            TargetId = monster.Id.ToString(),
                            TargetName = monster.Name,
                            TargetHp = monster.Health,
                            TargetMaxHp = monster.MaxHealth,
                            TargetX = monster.X,
                            TargetY = monster.Y
                        }
                    });
                }
                break;

            case "merchant":
                Log.Debug($"{player.Name} открыл магазин");
                await _svc.Hub.SendToClient(client, new GameMessage
                {
                    Type = "shop_response",
                    Data = new
                    {
                        MerchantX = _svc.Merchant.MerchantX,
                        MerchantY = _svc.Merchant.MerchantY,
                        MerchantName = "Торговец",
                        Discount = 0,
                        Items = _svc.Merchant.ShopItems.Select(i => new
                        {
                            i.Id, i.Name, i.Type, i.WeaponSubtype,
                            Value = Balance.BuyPrice(i.Value),
                            OriginalValue = i.Value,
                            i.MaxHealthBonus, i.HealAmount, i.RestoreMana, i.Description,
                            i.Stock,
                            IsBuyback = false
                        }).ToList(),
                        Buyback = player.BuybackItems.Select(b => new
                        {
                            b.Id, b.Name, b.Type, b.WeaponSubtype,
                            Value = Balance.BuybackPrice(b.Value),
                            OriginalValue = b.Value,
                        b.MaxHealthBonus, b.HealAmount, b.RestoreMana, b.Description,
                        b.Quantity, IsBuyback = true
                        }).ToList(),
                        PlayerGold = player.Gold
                    }
                });
                break;

            case "board":
                Log.Debug($"{player.Name} открыл доску заданий");
                await _svc.Hub.SendQuestLog(client, player);
                await _svc.Hub.SendToClient(client, new GameMessage
                {
                    Type = "open_board",
                    Data = null
                });
                await _svc.Hub.SendToClient(client, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Доска заданий открыта." }
                });
                break;

            case "npc":
                {
                    // Проверка: может это стражник инстанса?
                    var npcList = DatabaseManager.LoadNpcs();
                    var portalNpc = npcList.FirstOrDefault(n => n.X == player.Interaction.X && n.Y == player.Interaction.Y && n.Type == "instance_portal");
                    if (portalNpc != null)
                    {
                        var portal = _svc.Instances.FindPortal(player.CurrentZoneId, player.Interaction.X, player.Interaction.Y);
                        if (portal != null)
                        {
                            await _svc.Instances.TryEnter(player, portal.InstanceTemplateId, client);
                            break;
                        }
                    }

                    if (player.Dialogue.IsActive) break;
                    string? npcId = null;
                    foreach (var n in npcList)
                    {
                        if (n.X == player.Interaction.X && n.Y == player.Interaction.Y)
                        {
                            npcId = n.Id;
                            break;
                        }
                    }
                    if (npcId != null)
                    {
                        var startNode = _svc.Dialogue.GetStartNodeId(npcId);
                        if (startNode != null)
                        {
                            player.Dialogue.Start(npcId, startNode);
                            var tree = _svc.Dialogue.GetTree(npcId);
                            if (tree != null)
                                await _svc.Dialogue.SendNode(client, player, tree, startNode);
                        }
                        else
                        {
                            await ChatTo(client, ChatChannel.System, "Система", "Нечего сказать.");
                        }
                    }
                }
                break;

            case "chest":
                await _svc.Instances.TryOpenChest(player, client);
                break;

            case "collectible":
                var lootItem = _svc.Collectibles.TryCollect(player.Interaction.X, player.Interaction.Y, player.CurrentZoneId);
                if (lootItem != null)
                {
                    InventoryHelper.AddItem(player, lootItem);
                    Log.Debug($"{player.Name} собрал {lootItem.Name}");
                    await _svc.Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Система", Text = $"[Сбор] Вы собрали: {lootItem.Name}!" }
                    });
                    var collectResults = _svc.Quests.IncrementCollectProgress(player, lootItem.Id);
                    foreach (var (title, current, target, completed) in collectResults)
                    {
                        string msg = completed
                            ? $"[Задание] {title}: {current}/{target} — задание выполнено!"
                            : $"[Задание] {title}: {current}/{target}";
                        await _svc.Hub.SendToClient(client, new GameMessage
                        {
                            Type = "chat",
                            Data = new { Name = "Система", Text = msg }
                        });
                    }
                    await _svc.Hub.SendQuestLog(client, player);
                    await _svc.Hub.SendInventoryAndStatus(client, player);
                }
                else
                {
                    await _svc.Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Система", Text = "Здесь нечего собирать." }
                    });
                }
                break;

            case "loot_corpse":
                if (player.Interaction.CorpseId.HasValue)
                {
                    var corpse = _svc.Corpses.FindCorpseById(player.Interaction.CorpseId.Value);
                    if (corpse != null)
                        await LootCorpseHandler.LootCorpseAsync(client, player, corpse, _svc.Hub, _svc.Corpses);
                    else
                        await _svc.Hub.SendToClient(client, new GameMessage
                        {
                            Type = "chat",
                            Data = new { Name = "Система", Text = "Труп исчез или уже собран." }
                        });
                }
                break;

            case "player":
                var nearPlayer = _svc.World.FindPlayerAt(player.Interaction.X, player.Interaction.Y);
                if (nearPlayer != null)
                {
                    Log.Debug($"{player.Name} подошёл к {nearPlayer.Name}");
                    await _svc.Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Система", Text = $"Вы подошли к {nearPlayer.Name}. Используйте кнопки группы или обмена." }
                    });
                }
                else
                {
                    await _svc.Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Система", Text = "Игрок не найден." }
                    });
                }
                break;

            case "take_loot":
                if (player.Interaction.CorpseId.HasValue)
                {
                    var corpse = _svc.Corpses.FindCorpseById(player.Interaction.CorpseId.Value);
                    if (corpse != null)
                    {
                        var msg = new GameMessage
                        {
                            Type = "take_loot",
                            Data = new
                            {
                                CorpseId = corpse.Id.ToString(),
                                TakeAll = player.Interaction.TakeAll,
                                TakeGold = player.Interaction.TakeGold,
                                ItemIds = player.Interaction.ItemIds
                            }
                        };
                        await new TakeLootHandler(_svc).Handle(client, msg, player);
                    }
                    else
                        await _svc.Hub.SendToClient(client, new GameMessage
                        {
                            Type = "chat",
                            Data = new { Name = "Система", Text = "Труп исчез или уже собран." }
                        });
                }
                break;
        }
    }

    /// <summary>
    /// Цикл перемещения игроков по путям + обработка отмены обмена при удалении.
    /// </summary>
    public async Task RunMovePathLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Balance.LoopMovePathMs, ct);
                await Tick();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла перемещения", ex);
            }
        }
    }

    public async Task Tick()
    {
        bool moved = false;
        List<Player> playersCopy = _svc.World.GetPlayersSnapshot();
        foreach (var pl in playersCopy)
        {
            if (pl.IsDead) continue;
            if (pl.Movement.Path.Count == 0)
            {
                if (pl.Interaction.IsPending)
                {
                    var interaction = pl.Interaction.Type!;
                    await ProcessPendingInteraction(pl, interaction);
                    pl.Interaction.Clear();
                    moved = true;
                }
                continue;
            }
            double slow = 1.0 + pl.GetDebuffsSnapshot().Where(d => d.Type == DebuffType.Slow).Sum(d => d.Value);
            int moveIntervalMs = (int)(Balance.MoveIntervalMs(pl.Speed) * Math.Max(1.0, slow));
            if ((DateTime.UtcNow - pl.Movement.LastMoveTime).TotalMilliseconds < moveIntervalMs) continue;

            var next = pl.Movement.Path[0];
            pl.Movement.Path.RemoveAt(0);

            var zoneMap = _svc.Zones.GetOrCreateMap(pl.CurrentZoneId);
            if (next.X < 0 || next.X >= zoneMap.Width
                || next.Y < 0 || next.Y >= zoneMap.Height
                || _svc.World.Map.IsObstacle(next.X, next.Y))
            {
                pl.Movement.Stop();
                continue;
            }

            int dx = next.X - pl.X;
            int dy = next.Y - pl.Y;
            if (dx == 1) pl.Facing = "right";
            else if (dx == -1) pl.Facing = "left";
            else if (dy == 1) pl.Facing = "down";
            else if (dy == -1) pl.Facing = "up";

            pl.X = next.X;
            pl.Y = next.Y;
            pl.Movement.LastMoveTime = DateTime.UtcNow;
            moved = true;
            _svc.Hub.MarkZoneDirty(pl.CurrentZoneId);

            if (pl.CurrentZoneId.StartsWith("instance:"))
            {
                var inst = _svc.Instances.FindInstanceByPlayer(pl);
                if (inst != null && pl.X == inst.Template.ExitX + inst.OffsetX && pl.Y == inst.Template.ExitY + inst.OffsetY)
                {
                    pl.Movement.Stop();
                    pl.Interaction.Clear();
                    await _svc.Instances.KickPlayer(pl, "Вы вышли из подземелья.");
                    continue;
                }
            }

            var portal = _svc.Zones.FindPortal(pl.CurrentZoneId, pl.X, pl.Y);
            if (portal != null)
            {
                pl.Movement.Stop();
                pl.Interaction.Clear();
                await HandleZoneTransition(pl, portal);
                continue;
            }

            if (_svc.Trade.IsInTrade(pl))
            {
                var session = _svc.Trade.GetSession(pl.Id);
                if (session != null)
                {
                    var other = session.GetOther(pl);
                    if (other != null)
                    {
                        int dist = Math.Abs(pl.X - other.X) + Math.Abs(pl.Y - other.Y);
                        if (dist > 1)
                        {
                            pl.IsTrading = false;
                            other.IsTrading = false;
                            _svc.Trade.CancelSession(session, "слишком далеко");
                            var plConn = _svc.World.FindClientByPlayer(pl);
                            if (plConn != null)
                                await _svc.Hub.SendToClient(plConn, new GameMessage
                                {
                                    Type = "trade_close",
                                    Data = new { Message = "Обмен отменён: слишком далеко." }
                                });
                            var otherConn = _svc.World.FindClientByPlayer(other);
                            if (otherConn != null)
                                await _svc.Hub.SendToClient(otherConn, new GameMessage
                                {
                                    Type = "trade_close",
                                    Data = new { Message = $"Обмен отменён: {pl.Name} слишком далеко." }
                                });
                        }
                    }
                }
            }
        }
        if (moved) await _svc.Hub.BroadcastMapAsync();
    }

    private async Task HandleZoneTransition(Player player, WorldPortal portal)
    {
        string fromZone = player.CurrentZoneId;
        player.CurrentZoneId = portal.ToZone;
        player.X = portal.ToX;
        player.Y = portal.ToY;

        var targetZone = _svc.Zones.GetZone(portal.ToZone);
        string zoneName = targetZone?.Name ?? portal.ToZone;
        Log.Info($"{player.Name} перешёл из зоны '{fromZone}' в '{portal.ToZone}' ({portal.ToX},{portal.ToY})");

        var conn = _svc.World.FindClientByPlayer(player);
        if (conn != null)
        {
            await _svc.Hub.SendToClient(conn, new GameMessage
            {
                Type = "zone_transition",
                Data = new { ZoneId = portal.ToZone, ZoneName = zoneName, X = portal.ToX, Y = portal.ToY, PvPEnabled = targetZone?.PvpEnabled ?? false }
            });
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система",
                $"Вы вошли в зону: {zoneName}{(targetZone?.PvpEnabled == true ? " [PvP]" : "")}");
        }
        await _svc.Hub.BroadcastMapAsync();
    }
}
