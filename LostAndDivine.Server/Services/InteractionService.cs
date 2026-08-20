using System.Linq;
using LostAndDivine.Server.MessageHandlers;
using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server;

/// <summary>
/// Обработка взаимодействий: магазин, монстр, доска, NPC, сбор, лут, обмен.
/// Вынесена из GameServerHost.
/// </summary>
public class InteractionService
{
    private readonly GameServices _svc;

    public InteractionService(IGameServices svc)
    {
        _svc = (GameServices)svc;
    }

    private Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
        => _svc.ChatTo(conn, channel, name, text);

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
                // Сначала пытаемся проиграть диалог торговца (если он есть, например
                // приветствие с action: open_shop). Нет диалога — открываем магазин сразу.
                if (await TryStartDialogue(player, client, player.CurrentZoneId, player.Interaction.X, player.Interaction.Y))
                    break;
                await OpenShop(player, client);
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
                    if (await TryStartDialogue(player, client, player.CurrentZoneId, player.Interaction.X, player.Interaction.Y))
                        break;

                    // Если у NPC нет диалога — пробуем портал инстанса
                    var portal = _svc.Instances.FindPortal(player.CurrentZoneId, player.Interaction.X, player.Interaction.Y);
                    if (portal != null)
                    {
                        await _svc.Instances.TryEnter(player, portal.InstanceTemplateId, client);
                        break;
                    }

                    await ChatTo(client, ChatChannel.System, "Система", "Нечего сказать.");
                }
                break;

            case "chest":
                await _svc.Instances.TryOpenChest(player, client);
                break;

            case "storage_chest":
                await _svc.Storage.OnPlayerInteractAsync(player);
                break;

            case "door":
                {
                    var door = _svc.Zones.FindDoor(player.CurrentZoneId, player.Interaction.X, player.Interaction.Y);
                    if (door == null)
                    {
                        await ChatTo(client, ChatChannel.System, "Система", "Здесь нет двери.");
                        break;
                    }

                    int dx = Math.Sign(player.Interaction.X - player.X);
                    int dy = Math.Sign(player.Interaction.Y - player.Y);

                    if (dx == 0 && dy == 0)
                    {
                        await ChatTo(client, ChatChannel.System, "Система", "Вы стоите прямо на двери.");
                        break;
                    }
                    if (dx != 0 && dy != 0)
                    {
                        await ChatTo(client, ChatChannel.System, "Система", "Подойдите к двери вплотную.");
                        break;
                    }

                    int destX = player.Interaction.X + dx;
                    int destY = player.Interaction.Y + dy;
                    var zoneMap = _svc.Zones.GetOrCreateMap(player.CurrentZoneId);
                    if (destX < 0 || destY < 0 || destX >= zoneMap.Width || destY >= zoneMap.Height || zoneMap.IsObstacle(destX, destY))
                    {
                        await ChatTo(client, ChatChannel.System, "Система", "За дверью нет прохода.");
                        break;
                    }

                    player.Movement.Stop();
                    player.X = destX;
                    player.Y = destY;
                    if (dx == 1) player.Facing = "right";
                    else if (dx == -1) player.Facing = "left";
                    else if (dy == 1) player.Facing = "down";
                    else if (dy == -1) player.Facing = "up";
                    Log.Debug($"{player.Name} прошёл через дверь ({player.Interaction.X},{player.Interaction.Y}) -> ({destX},{destY})");
                    await _svc.Hub.BroadcastMapAsync();
                    break;
                }

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
    /// Если у NPC на указанных координатах есть диалог — начать его и отправить стартовую
    /// ноду клиенту. Возвращает true, когда диалог начат (или уже активен).
    /// </summary>
    private async Task<bool> TryStartDialogue(Player player, ClientConnection client, string zoneId, int x, int y)
    {
        if (player.Dialogue.IsActive) return true;
        var npc = _svc.Hub.FindNpcAt(zoneId, x, y);
        if (npc == null) return false;
        var startNode = _svc.Dialogue.GetStartNodeId(npc.Id);
        if (startNode == null) return false;
        player.Dialogue.Start(npc.Id, startNode);
        // Разговор с NPC считается состоявшимся ДО отправки ноды — условия узлов
        // (например, quest_ready) должны видеть обновлённое состояние квеста.
        _svc.Quests.IncrementTalkProgress(player, npc.Id);
        var tree = _svc.Dialogue.GetTree(npc.Id);
        if (tree == null) return false;
        await _svc.Dialogue.SendNode(client, player, tree, startNode);
        await _svc.Hub.SendQuestLog(client, player);
        _svc.Hub.MarkZoneDirty(player.CurrentZoneId);
        await _svc.Hub.BroadcastMapAsync();
        return true;
    }

    /// <summary>
    /// Открыть окно магазина торговца (без диалога).
    /// </summary>
    internal async Task OpenShop(Player player, ClientConnection client)
    {
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
                    i.Id, i.TemplateId, i.Name, i.Type, i.WeaponSubtype, i.Quantity,
                    Value = Balance.BuyPrice(i.Value),
                    OriginalValue = i.Value,
                    i.MaxHealthBonus, i.MaxManaBonus, i.HealAmount, i.RestoreMana, i.Description,
                    Stock = _svc.Merchant.GetStock(i.Id), i.MaxStack, IsBuyback = false,
                    BonusStrength = i.BonusStrength, BonusEndurance = i.BonusEndurance,
                    BonusAgility = i.BonusAgility, BonusCunning = i.BonusCunning,
                    BonusIntellect = i.BonusIntellect, BonusWisdom = i.BonusWisdom,
                    BonusPhysAttack = i.BonusPhysAttack, BonusMagAttack = i.BonusMagAttack,
                    BonusDefense = i.BonusDefense, BonusResistance = i.BonusResistance,
                    i.Defense, i.MagicDefense,
                    BonusCritChance = i.BonusCritChance, BonusCritDamage = i.BonusCritDamage,
                    BonusEvadeChance = i.BonusEvadeChance, BonusAttackSpeed = i.BonusAttackSpeed,
                    BonusBlockChance = i.BonusBlockChance, BonusParryChance = i.BonusParryChance,
                    BonusAccuracy = i.BonusAccuracy, BonusTenacity = i.BonusTenacity,
                    BonusArmorPenetration = i.BonusArmorPenetration, BonusCooldownReduction = i.BonusCooldownReduction,
                    BonusHpRegen = i.BonusHpRegen, BonusMpRegen = i.BonusMpRegen,
                    i.DamageType, i.RequiredLevel, i.DamageMin, i.DamageMax,
                    i.AttackSpeedModifier, i.TwoHanded, i.AttackRange, i.Icon
                }).ToList(),
                Buyback = player.BuybackItems.Select(b => new
                {
                    b.Id, b.TemplateId, b.Name, b.Type, b.WeaponSubtype, b.Quantity,
                    Value = Balance.BuybackPrice(b.Value),
                    OriginalValue = b.Value,
                    b.MaxHealthBonus, b.MaxManaBonus, b.HealAmount, b.RestoreMana, b.Description,
                    b.MaxStack, IsBuyback = true, Stock = 0,
                    BonusStrength = b.BonusStrength, BonusEndurance = b.BonusEndurance,
                    BonusAgility = b.BonusAgility, BonusCunning = b.BonusCunning,
                    BonusIntellect = b.BonusIntellect, BonusWisdom = b.BonusWisdom,
                    BonusPhysAttack = b.BonusPhysAttack, BonusMagAttack = b.BonusMagAttack,
                    BonusDefense = b.BonusDefense, BonusResistance = b.BonusResistance,
                    b.Defense, b.MagicDefense,
                    BonusCritChance = b.BonusCritChance, BonusCritDamage = b.BonusCritDamage,
                    BonusEvadeChance = b.BonusEvadeChance, BonusAttackSpeed = b.BonusAttackSpeed,
                    BonusBlockChance = b.BonusBlockChance, BonusParryChance = b.BonusParryChance,
                    BonusAccuracy = b.BonusAccuracy, BonusTenacity = b.BonusTenacity,
                    BonusArmorPenetration = b.BonusArmorPenetration, BonusCooldownReduction = b.BonusCooldownReduction,
                    BonusHpRegen = b.BonusHpRegen, BonusMpRegen = b.BonusMpRegen,
                    b.DamageType, b.RequiredLevel, b.DamageMin, b.DamageMax,
                    b.AttackSpeedModifier, b.TwoHanded, b.AttackRange, b.Icon
                }).ToList(),
                PlayerGold = player.Gold
            }
        });
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
                // Преследование цели через A* (PvE и PvP)
                if (pl.Combat.HasTarget && pl.Combat.TargetMonsterId != null)
                {
                    var monster = _svc.Monsters.FindMonsterById(pl.Combat.TargetMonsterId.Value);
                    if (monster != null && monster.Health > 0 && monster.ZoneId == pl.CurrentZoneId)
                    {
                        if (_svc.Combat.ChaseTarget(pl, monster))
                        {
                            _svc.Hub.MarkZoneDirty(pl.CurrentZoneId);
                            await CheckTravelProgress(pl);
                        }
                    }
                    else if (monster == null || monster.Health <= 0 || monster.ZoneId != pl.CurrentZoneId)
                    {
                        pl.Combat.Cancel();
                    }
                }
                else if (pl.Combat.IsPvPTarget && pl.Combat.TargetPlayerId != null)
                {
                    var target = _svc.World.GetPlayersSnapshot()
                        .FirstOrDefault(p => p.Id == pl.Combat.TargetPlayerId.Value && p.CurrentZoneId == pl.CurrentZoneId);
                    if (target != null && !target.IsDead && target.Health > 0)
                    {
                        if (_svc.PvP.ChasePlayerTarget(pl, target))
                            _svc.Hub.MarkZoneDirty(pl.CurrentZoneId);
                    }
                    else
                    {
                        pl.Combat.Cancel();
                    }
                }

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
                || zoneMap.IsObstacle(next.X, next.Y))
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
            await CheckTravelProgress(pl);

            if (pl.CurrentZoneId.StartsWith("instance:"))
            {
                var inst = _svc.Instances.FindInstanceByPlayer(pl);
                if (inst != null && pl.X == inst.EffectiveExitX && pl.Y == inst.EffectiveExitY)
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

    /// <summary>Прогресс travel-квестов после перемещения игрока.</summary>
    private async Task CheckTravelProgress(Player pl)
    {
        var conn = _svc.World.FindClientByPlayer(pl);
        if (conn == null) return;
        var results = _svc.Quests.IncrementTravelProgress(pl, pl.CurrentZoneId, pl.X, pl.Y);
        if (results.Count == 0) return;
        foreach (var (title, current, target, completed) in results)
        {
            await ChatTo(conn, ChatChannel.System, "Система",
                completed ? $"[Задание] {title}: цель достигнута!" : $"[Задание] {title}: {current}/{target}");
        }
        await _svc.Hub.SendQuestLog(conn, pl);
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
            await _svc.Hub.SendZoneTransition(conn, player);
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система",
                $"Вы вошли в зону: {zoneName}{(targetZone?.PvpEnabled == true ? " [PvP]" : "")}");
        }
        await _svc.Hub.BroadcastMapAsync();

        // Квесты: explore-цели, авто-выдача и travel в точке прибытия
        if (conn != null)
        {
            var exploreResults = _svc.Quests.IncrementExploreProgress(player, portal.ToZone);
            foreach (var (title, current, target, completed) in exploreResults)
            {
                await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система",
                    completed ? $"[Задание] {title}: зона исследована!" : $"[Задание] {title}: {current}/{target}");
            }

            var granted = _svc.Quests.TryAutoGrant(player, portal.ToZone);
            foreach (var d in granted)
                await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", $"Новое задание: {d.Title}");

            var travelResults = _svc.Quests.IncrementTravelProgress(player, portal.ToZone, player.X, player.Y);
            foreach (var (title, current, target, completed) in travelResults)
            {
                await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система",
                    completed ? $"[Задание] {title}: цель достигнута!" : $"[Задание] {title}: {current}/{target}");
            }

            if (exploreResults.Count + granted.Count + travelResults.Count > 0)
                await _svc.Hub.SendQuestLog(conn, player);
        }
    }
}
