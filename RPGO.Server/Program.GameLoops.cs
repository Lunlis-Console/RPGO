using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Server.MessageHandlers;

namespace RPGGame.Server;

public partial class Program
{
    private static Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
    {
        if (conn == null) return Task.CompletedTask;
        return Hub.SendChatToAsync(conn, channel, name, text);
    }

    private static Task ChatToPlayer(Player? player, ChatChannel channel, string name, string text)
    {
        if (player == null) return Task.CompletedTask;
        return ChatTo(World.FindClientByPlayer(player), channel, name, text);
    }

    private static async Task RunMonsterWanderLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(Balance.LoopMonsterWanderMs);
                MonsterManager.WanderStep();
                await Hub.BroadcastMapAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла блуждания монстров", ex);
            }
        }
    }

    private static async Task RunMovePathLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(Balance.LoopMovePathMs);
                bool moved = false;
                List<Player> playersCopy;
                playersCopy = World.GetPlayersSnapshot();
                foreach (var pl in playersCopy)
                {
                    if (pl.IsDead) continue;
                    if (pl.Movement.Path.Count == 0)
                    {
                        // Путь завершён — проверяем отложенное взаимодействие
                        if (pl.Interaction.IsPending)
                        {
                            var interaction = pl.Interaction.Type!;
                            await ProcessPendingInteraction(pl, interaction);
                            pl.Interaction.Clear();
                            moved = true;
                        }
                        continue;
                    }
                    int moveIntervalMs = Balance.MoveIntervalMs(pl.Speed);
                    if ((DateTime.UtcNow - pl.Movement.LastMoveTime).TotalMilliseconds < moveIntervalMs) continue;

                    var next = pl.Movement.Path[0];
                    pl.Movement.Path.RemoveAt(0);

                    // Защита последнего рубежа: никогда не ставим игрока в стену/препятствие.
                    if (next.X < 0 || next.X >= World.Map.Width
                        || next.Y < 0 || next.Y >= World.Map.Height
                        || World.Map.IsObstacle(next.X, next.Y))
                    {
                        pl.Movement.Stop();
                        continue;
                    }

                    // Обновляем направление взгляда
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

                    if (TradeManager.IsInTrade(pl))
                    {
                        var session = TradeManager.GetSession(pl.Id);
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
                                    TradeManager.CancelSession(session, "слишком далеко");
                                    var plConn = World.FindClientByPlayer(pl);
                                    if (plConn != null)
                                        await Hub.SendToClient(plConn, new GameMessage
                                        {
                                            Type = "trade_close",
                                            Data = new { Message = "Обмен отменён: слишком далеко." }
                                        });
                                    var otherConn = World.FindClientByPlayer(other);
                                    if (otherConn != null)
                                        await Hub.SendToClient(otherConn, new GameMessage
                                        {
                                            Type = "trade_close",
                                            Data = new { Message = $"Обмен отменён: {pl.Name} слишком далеко." }
                                        });
                                }
                            }
                        }
                    }
                }
                if (moved) await Hub.BroadcastMapAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла перемещения", ex);
            }
        }
    }

    internal static async Task ProcessPendingInteraction(Player player, string interactionType)
    {
        var client = World.FindClientByPlayer(player);
        if (client == null) return;

        switch (interactionType)
        {
            case "monster":
                Monster? monster = null;
                if (player.Interaction.MonsterId != null)
                    monster = MonsterManager.FindMonsterById(player.Interaction.MonsterId.Value);
                if (monster == null)
                    monster = MonsterManager.FindMonsterAt(player.Interaction.X, player.Interaction.Y);
                if (monster != null && monster.Health > 0)
                {
                    player.Combat.Enter(monster.Id, player.Movement);
                    Log.Debug($"{player.Name} начал бой с {monster.Name}");
                    await Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Бой", Text = $"Бой: {monster.Name} [{monster.Level}] ({monster.Health}/{monster.MaxHealth})" }
                    });
                    await Hub.SendToClient(client, new GameMessage
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
                await Hub.SendToClient(client, new GameMessage
                {
                    Type = "shop_response",
                    Data = new
                    {
                        MerchantX = MerchantManager.MerchantX,
                        MerchantY = MerchantManager.MerchantY,
                        MerchantName = "Торговец",
                        Discount = 0,
                        Items = MerchantManager.ShopItems.Select(i => new
                        {
                            i.Id, i.Name, i.Type,
                            Value = Balance.BuyPrice(i.Value),
                            OriginalValue = i.Value,
                            i.MaxHealthBonus, i.HealAmount, i.Description,
                            i.Stock,
                            IsBuyback = false
                        }).ToList(),
                        Buyback = player.BuybackItems.Select(b => new
                        {
                            b.Id, b.Name, b.Type,
                            Value = Balance.BuybackPrice(b.Value),
                            OriginalValue = b.Value,
                            b.MaxHealthBonus, b.HealAmount, b.Description,
                            IsBuyback = true
                        }).ToList(),
                        PlayerGold = player.Gold
                    }
                });
                break;

            case "board":
                Log.Debug($"{player.Name} открыл доску заданий");
                await Hub.SendQuestLog(client, player);
                await Hub.SendToClient(client, new GameMessage
                {
                    Type = "open_board",
                    Data = null
                });
                await Hub.SendToClient(client, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Доска заданий открыта." }
                });
                break;

            case "collectible":
                var lootItem = CollectibleManager.TryCollect(player.Interaction.X, player.Interaction.Y);
                if (lootItem != null)
                {
                    InventoryHelper.AddItem(player, lootItem);
                    Log.Debug($"{player.Name} собрал {lootItem.Name}");
                    await Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Система", Text = $"[Сбор] Вы собрали: {lootItem.Name}!" }
                    });
                    var collectResults = QuestManager.IncrementCollectProgress(player, lootItem.Id);
                    foreach (var (title, current, target, completed) in collectResults)
                    {
                        string msg = completed
                            ? $"[Задание] {title}: {current}/{target} — задание выполнено!"
                            : $"[Задание] {title}: {current}/{target}";
                        await Hub.SendToClient(client, new GameMessage
                        {
                            Type = "chat",
                            Data = new { Name = "Система", Text = msg }
                        });
                    }
                    await Hub.SendQuestLog(client, player);
                    await Hub.SendInventoryAndStatus(client, player);
                }
                else
                {
                    await Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Система", Text = "Здесь нечего собирать." }
                    });
                }
                break;

            case "loot_corpse":
                if (player.Interaction.CorpseId.HasValue)
                {
                    var corpse = CorpseManager.FindCorpseById(player.Interaction.CorpseId.Value);
                    if (corpse != null)
                        await LootCorpseHandler.LootCorpseAsync(client, player, corpse);
                    else
                        await Hub.SendToClient(client, new GameMessage
                        {
                            Type = "chat",
                            Data = new { Name = "Система", Text = "Труп исчез или уже собран." }
                        });
                }
                break;

            case "player":
                var nearPlayer = World.FindPlayerAt(player.Interaction.X, player.Interaction.Y);
                if (nearPlayer != null)
                {
                    Log.Debug($"{player.Name} подошёл к {nearPlayer.Name}");
                    await Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Система", Text = $"Вы подошли к {nearPlayer.Name}. Используйте кнопки группы или обмена." }
                    });
                }
                else
                {
                    await Hub.SendToClient(client, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Система", Text = "Игрок не найден." }
                    });
                }
                break;

            case "take_loot":
                if (player.Interaction.CorpseId.HasValue)
                {
                    var corpse = CorpseManager.FindCorpseById(player.Interaction.CorpseId.Value);
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
                        await new TakeLootHandler(World, Hub).Handle(client, msg, player);
                    }
                    else
                        await Hub.SendToClient(client, new GameMessage
                        {
                            Type = "chat",
                            Data = new { Name = "Система", Text = "Труп исчез или уже собран." }
                        });
                }
                break;
        }
    }

    private static async Task RunMonsterAttackLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(Balance.LoopMonsterAttackMs);
                var attacks = MonsterManager.DrainPendingAttacks();
                foreach (var (monster, player, damage) in attacks)
                {
                    if (player.IsDead) continue;
                    player.Health -= damage;
                    player.LastDamagedTime = DateTime.UtcNow;
        var client = World.FindClientByPlayer(player);
                    if (client == null) continue;

                    var hitMsg = GameMessage.Damage("player", null, player.X, player.Y, damage, false, player.Name);
                    await Hub.SendToClient(client, hitMsg);
                    await Hub.SendDamageNearbyAsync(player.X, player.Y, hitMsg, player);

                    await Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                    await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} нанёс вам {damage} урона. ({player.Health}/{player.MaxHealth + player.Equipment.GetBonusMaxHealth()}) HP");

                    await PartyManager.SendUpdateForAsync(player);

                    if (player.Health <= 0)
                    {
                        int lostGold = Balance.ComputeDeathGoldLoss(player.Gold);
                        player.Gold -= lostGold;
                        player.Combat.Cancel();
                        player.Interaction.Clear();
                        player.Movement.Stop();
                        player.IsDead = true;
                        player.DeathTime = DateTime.UtcNow;
                        Log.Info($"{player.Name} погиб от {monster.Name}! Потеряно {lostGold} золота. Таймер 5с.");
                        await ChatTo(client, ChatChannel.System, "Система", $"Вы погибли от {monster.Name}! Потеряно {lostGold} золота. Возрождение через 5 сек...");
                        await Hub.SendToClient(client, GameMessage.ResetCombat());
                        await Hub.SendToClient(client, GameMessage.PlayerDeath(lostGold));

                        await PartyManager.SendUpdateForAsync(player);
                    }

                    await Hub.SendStatusAsync(client, player);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка боевого цикла монстров", ex);
            }
        }
    }

    private static async Task RunRegenLoop()
    {
        const int inCombatDelayMs = Balance.PlayerRegenInCombatDelayMs;    // считаем "в бою", пока били недавно
        const int outOfCombatHeal = Balance.PlayerRegenOutOfCombatHeal;          // 5 HP за тик вне боя
        const int outOfCombatTickMs = Balance.PlayerRegenOutOfCombatTickMs;      // раз в 5 секунд вне боя
        const double inCombatFraction = Balance.PlayerRegenInCombatFraction;   // в 2 раза меньше в бою
        const int inCombatTickMs = Balance.PlayerRegenInCombatTickMs;         // в 2 раза реже в бою

        while (true)
        {
            try
            {
                await Task.Delay(outOfCombatTickMs);

                var now = DateTime.UtcNow;

                // Игроки
                List<Player> playersCopy;
                playersCopy = World.GetPlayersSnapshot();
                foreach (var pl in playersCopy)
                {
                    if (pl.IsDead) continue;
                    if (pl.Health >= pl.MaxHealth + pl.Equipment.GetBonusMaxHealth()) continue;

                    bool plInCombat = (now - pl.LastDamagedTime).TotalMilliseconds < inCombatDelayMs;
                    int tick = plInCombat ? inCombatTickMs : outOfCombatTickMs;

                    if ((now - pl.LastRegenTime).TotalMilliseconds >= tick)
                    {
                        int maxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth();
                        int heal = plInCombat
                            ? Math.Max(Balance.PlayerRegenMinHeal, (int)(maxHp * inCombatFraction))
                            : outOfCombatHeal;
                        pl.Health = Math.Min(maxHp, pl.Health + heal);
                        pl.LastRegenTime = now;

                        // Реген маны
                        int maxMana = pl.MaxMana;
                        if (pl.Mana < maxMana)
                        {
                            int manaTick = plInCombat
                                ? Math.Max(Balance.ManaRegenMin, (int)(maxMana * Balance.ManaRegenInCombatFraction))
                                : Balance.ManaRegenOutOfCombat;
                            pl.Mana = Math.Min(maxMana, pl.Mana + manaTick);
                        }

                        ClientConnection? conn = World.FindClientByPlayer(pl);
                        if (conn != null)
                        {
                            if (heal > 0)
                            {
                                var healMsg = new GameMessage
                                {
                                    Type = "heal",
                                    Data = new { Target = "player", PlayerName = pl.Name, X = pl.X, Y = pl.Y, Amount = heal }
                                };
                                await Hub.SendToClient(conn, healMsg);
                                await Hub.SendDamageNearbyAsync(pl.X, pl.Y, healMsg, pl);
                            }
                            await Hub.SendStatusAsync(conn, pl);
                        }

                        await PartyManager.SendUpdateForAsync(pl);
                    }
                }

                // Монстры
                MonsterManager.RegenStep();

                await Hub.BroadcastMapAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла регенерации", ex);
            }
        }
    }

    private static async Task RunDebuffTickLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(Balance.DebuffTickMs);
                foreach (var pl in World.GetPlayersSnapshot())
                {
                    if (pl.IsDead) continue;
                    if (pl.ActiveDebuffs.Count > 0)
                    {
                        DebuffManager.TickDebuffs(pl);
                        var conn = World.FindClientByPlayer(pl);
                        if (conn != null) await Hub.SendStatusAsync(conn, pl);
                    }
                }
                foreach (var mon in World.GetMonstersSnapshot())
                {
                    if (mon.ActiveDebuffs.Count > 0)
                    {
                        DebuffManager.TickDebuffs(mon);
                        await SendTargetDebuffUpdateAsync(mon);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла дебаффов", ex);
            }
        }
    }

    private static async Task RunCorpseCleanupLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(30_000);
                CorpseManager.CleanupExpired();
                MonsterManager.SpawnOneMonsterPublic();
                await Hub.BroadcastMapAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла очистки трупов", ex);
            }
        }
    }

    private static async Task RunDeathTimerLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(500);
                foreach (var pl in World.GetPlayersSnapshot())
                {
                    if (pl.IsDead && (DateTime.UtcNow - pl.DeathTime).TotalMilliseconds >= Balance.DeathDelayMs)
                    {
                        await Program.RespawnPlayer(pl);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла таймера смерти", ex);
            }
        }
    }

    private static async Task SendTargetDebuffUpdateAsync(Monster mon)
    {
        var debuffData = mon.ActiveDebuffs.Select(d => new
        {
            Type = d.Type.ToString(),
            d.DisplayName,
            d.Description,
            Value = Math.Round(d.Value, 2),
            d.RemainingMs,
            DurationMs = d.DurationMs
        }).ToList();
        var msg = GameMessage.TargetDebuffUpdate(debuffData);
        foreach (var pl in World.GetPlayersSnapshot())
        {
            if (pl.Combat.HasTarget && pl.Combat.TargetMonsterId == mon.Id)
            {
                var conn = World.FindClientByPlayer(pl);
                if (conn != null) await Hub.SendToClient(conn, msg);
            }
        }
    }
}
