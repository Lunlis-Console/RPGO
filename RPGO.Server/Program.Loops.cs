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

    private static async Task RunCombatLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(200);
                bool changed = false;
                List<Player> playersCopy;
                playersCopy = World.GetPlayersSnapshot();
                foreach (var pl in playersCopy)
                {
                    if (!pl.Combat.HasTarget) continue;

                    var monster = MonsterManager.FindMonsterById(pl.Combat.TargetMonsterId!.Value);
                    if (monster == null || monster.Health <= 0)
                    {
                        // Манекен: при 0 HP — мгновенное полное лечение, продолжаем бой
                        if (monster != null && monster.IsMannequin && monster.Health <= 0)
                        {
                            monster.Health = monster.MaxHealth;
                            monster.LastDamagedTime = DateTime.UtcNow;
                            var mClient = World.FindClientByPlayer(pl);
                            if (mClient != null)
                            {
                                await Hub.SendToClient(mClient, new GameMessage
                                {
                                    Type = "combat_update",
                                    Data = new { MonsterName = monster.Name, MonsterHealth = monster.Health, MonsterMaxHealth = monster.MaxHealth }
                                });
                                await ChatTo(mClient, ChatChannel.Combat, "Бой", $"{monster.Name} восстановил все HP!");
                            }
                            continue;
                        }
                        pl.Combat.Cancel();
                        var client = World.FindClientByPlayer(pl);
                        if (client != null)
                        {
                            await Hub.SendToClient(client, new GameMessage
                            {
                                Type = "combat_state",
                                Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
                            });
                        }
                        changed = true;
                        continue;
                    }

                    int dist = Math.Abs(pl.X - monster.X) + Math.Abs(pl.Y - monster.Y);
                    if (dist > Balance.AttackRange)
                    {
                        // Подойти к цели на расстояние 1 клетки (манхэттен).
                        // Двигаемся СТРОГО по 4 сторонам: при диагонали делаем шаг
                        // только по одной оси, чтобы не пытаться встать в клетку цели
                        // (иначе игрок и монстр упираются друг в друга по диагонали).
                        int stepX = Math.Sign(monster.X - pl.X);
                        int stepY = Math.Sign(monster.Y - pl.Y);

                        int moveIntervalMs = Balance.MoveIntervalMs(pl.Speed);
                        if ((DateTime.UtcNow - pl.Movement.LastMoveTime).TotalMilliseconds < moveIntervalMs) continue;

                        // Выбираем одну ось для шага (4-направленное движение)
                        int mx = 0, my = 0;
                        if (stepX != 0 && stepY != 0)
                        {
                            // Диагональ: пробуем сначала ось X, если там свободно, иначе Y
                            if (pl.X + stepX >= 0 && pl.X + stepX < World.Map.Width
                                && MonsterManager.FindMonsterAt(pl.X + stepX, pl.Y) == null)
                                mx = stepX;
                            else
                                my = stepY;
                        }
                        else if (stepX != 0)
                            mx = stepX;
                        else if (stepY != 0)
                            my = stepY;

                        int nx = pl.X + mx;
                        int ny = pl.Y + my;

                        // Не двигаться на клетку с другим монстром
                        if (mx != 0 || my != 0)
                        {
                            if (nx >= 0 && nx < World.Map.Width && ny >= 0 && ny < World.Map.Height
                                && MonsterManager.FindMonsterAt(nx, ny) == null)
                            {
                                if (mx == 1) pl.Facing = "right";
                                else if (mx == -1) pl.Facing = "left";
                                else if (my == 1) pl.Facing = "down";
                                else if (my == -1) pl.Facing = "up";

                                pl.X = nx;
                                pl.Y = ny;
                                pl.Movement.LastMoveTime = DateTime.UtcNow;
                                changed = true;
                            }
                        }
                    }
                    else
                    {
                        var client = World.FindClientByPlayer(pl);
                        if (client == null) continue;

                        // На расстоянии 1 — автоатака (возможно с навыком из очереди)
                        int attackIntervalMs = Balance.AttackIntervalMs(Balance.GetAttackSpeed(pl.Agility), pl.Equipment.GetWeaponSpeedModifier());
                        bool mainAttackReady = (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= attackIntervalMs;
                        // Off-hand бьёт один раз за цикл атаки, с задержкой 250ms после основного удара.
                        // Таймер: основной удар → +250ms → удар второй рукой → ждём следующего цикла.
                        bool offHandReady = pl.Equipment.IsDualWielding()
                            && pl.Combat.LastAttackTime > pl.Combat.OffHandLastAttackTime
                            && (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= Balance.OffHandDelayMs;

                        if (!mainAttackReady && !offHandReady) continue;

                        Skill? queuedSkill = null;
                        if (pl.QueuedSkillIds.Count > 0)
                        {
                            string sid = pl.QueuedSkillIds[0];
                            var cand = DatabaseManager.GetSkill(sid);
                            if (cand != null)
                            {
                                bool onCd = pl.LastSkillUse.TryGetValue(sid, out var last)
                                    && (DateTime.UtcNow - last).TotalMilliseconds < cand.CooldownMs;
                                bool noMp = pl.Mana < cand.MpCost;
                                if (onCd || noMp)
                                {
                                    pl.QueuedSkillIds.RemoveAt(0);
                                    var skillClient = World.FindClientByPlayer(pl);
                                    if (skillClient != null)
                                    {
                                        await UseSkillHandler.SendSkillQueue(skillClient, pl);
                                        await Hub.SendToClient(skillClient, new GameMessage
                                        {
                                            Type = "chat",
                                            Data = new { Name = "Бой", Text = onCd
                                                ? $"«{cand.Name}» ещё на перезарядке — пропускаем."
                                                : $"«{cand.Name}»: недостаточно маны ({pl.Mana}/{cand.MpCost}) — пропускаем." }
                                        });
                                    }
                                }
                                else
                                {
                                    queuedSkill = cand;
                                }
                            }
                            else
                            {
                                pl.QueuedSkillIds.RemoveAt(0);
                            }
                        }

                        // === ОСНОВНОЙ УДАР ===
                        bool mainFired = false;
                        if (mainAttackReady)
                        {
                            pl.Combat.LastAttackTime = DateTime.UtcNow;
                            mainFired = true;

                            var (dmgToMonster, dmgToPlayer, monsterDead, isCrit, isEvaded) =
                                MonsterManager.CalculateCombat(pl, monster, queuedSkill == null);

                            // === Оружейный прок ===
                            if (!isEvaded)
                            {
                                string subtype = pl.Equipment.GetWeaponSubtype();
                                if (!string.IsNullOrEmpty(subtype))
                                {
                                    DebuffManager.OnWeaponProc(pl, monster, subtype);

                                    // Клив: наносим урон 50% по 3 клеткам
                                    if (DebuffManager.HasDebuff(pl, DebuffType.CleaveReady))
                                    {
                                        DebuffManager.ClearDebuffs(pl);
                                        MonsterManager.CalculateCleave(pl, monster);
                                    }
                                }
                            }

                            if (queuedSkill != null)
                            {
                                int baseDamage = (int)Math.Max(Balance.MinDamage, pl.GetTotalAttack() - monster.GetTotalDefense());
                                int skillDamage = (int)Math.Max(Balance.MinDamage, baseDamage * queuedSkill.DamageMultiplier);
                                dmgToMonster = skillDamage;
                                monster.Health -= skillDamage;
                                monster.LastDamagedTime = DateTime.UtcNow;
                                monster.DamageTracker[pl.Id] = monster.DamageTracker.GetValueOrDefault(pl.Id) + skillDamage;
                                monsterDead = monster.Health <= 0;
                                pl.Mana = Math.Max(0, pl.Mana - queuedSkill.MpCost);
                                pl.LastSkillUse[queuedSkill.Id] = DateTime.UtcNow;
                                pl.QueuedSkillIds.RemoveAt(0);
                                await UseSkillHandler.SendSkillQueue(client, pl);
                                await Hub.SendToClient(client, new GameMessage
                                {
                                    Type = "skill_cooldown",
                                    Data = new { SkillId = queuedSkill.Id, RemainingMs = queuedSkill.CooldownMs, TotalMs = queuedSkill.CooldownMs }
                                });
                                await ChatTo(client, ChatChannel.Combat, "Бой", $"Применён навык «{queuedSkill.Name}»! Урон x{queuedSkill.DamageMultiplier}.");
                            }

                            if (monsterDead)
                            {
                                // Манекен: мгновенное полное лечение вместо смерти
                                if (monster.IsMannequin)
                                {
                                    monster.Health = monster.MaxHealth;
                                    monster.LastDamagedTime = DateTime.UtcNow;
                                    await ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{(isCrit ? " (КРИТ!)" : "")}");
                                    await Hub.SendToClient(client, new GameMessage
                                    {
                                        Type = "combat_update",
                                        Data = new { MonsterName = monster.Name, MonsterHealth = monster.Health, MonsterMaxHealth = monster.MaxHealth }
                                    });
                                    continue;
                                }

                                pl.Combat.Cancel();
                                pl.Combat.OffHandLastAttackTime = DateTime.MinValue;
                                int shownDmg = Math.Max(0, monster.Health + dmgToMonster);
                                string critText2 = isCrit ? " (КРИТ!)" : "";
                                Log.Info($"{pl.Name} убил {monster.Name}!{critText2}");
                                await ChatTo(client, ChatChannel.Combat, "Бой", $"Вы нанесли {shownDmg} урона{critText2} и убили {monster.Name}!");

                                if (!isEvaded)
                                {
                                    var killDmgMsg = new GameMessage
                                    {
                                        Type = "damage",
                                        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = shownDmg, IsCrit = isCrit }
                                    };
                                    await Hub.SendToClient(client, killDmgMsg);
                                    await Hub.SendDamageNearbyAsync(monster.X, monster.Y, killDmgMsg, pl);
                                }

                                await Hub.SendToClient(client, new GameMessage
                                {
                                    Type = "combat_state",
                                    Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
                                });

                                // === Мультиплеер: определяем пати и участников ===
                                var damageTracker = monster.DamageTracker;
                                var partyContributors = new List<(Player Player, int Damage)>();
                                bool isPartyMode = false;

                                if (pl.PartyId.HasValue)
                                {
                                    var party = PartyManager.GetParty(pl.PartyId.Value);
                                    if (party != null)
                                    {
                                        foreach (var kvp in damageTracker)
                                        {
                                            if (World.TryGetPlayer(kvp.Key, out var contributor) && contributor != null
                                                && contributor.PartyId == pl.PartyId)
                                            {
                                                partyContributors.Add((contributor, kvp.Value));
                                            }
                                        }
                                        if (partyContributors.Count > 1)
                                            isPartyMode = true;
                                    }
                                }

                                int totalDamage = damageTracker.Values.Sum();

                                if (isPartyMode)
                                {
                                    var playerLootDict = new Dictionary<Guid, CorpsePlayerLoot>();

                                    foreach (var (contributor, dmg) in partyContributors)
                                    {
                                        double dmgShare = totalDamage > 0 ? (double)dmg / totalDamage : 0;
                                        int xpReward = (int)(monster.XpReward * dmgShare);
                                        int goldReward = (int)(monster.GoldReward * dmgShare);

                                        contributor.Experience += xpReward;

                                        int xpNeeded = Balance.XpNeededForNextLevel(contributor.Level);
                                        if (contributor.Experience >= xpNeeded)
                                        {
                                            contributor.Level++;
                                            contributor.Experience -= xpNeeded;
                                            contributor.MaxHealth += Balance.MaxHealthPerLevel;
                                            contributor.Health = contributor.MaxHealth;
                                            contributor.AttributePoints += Balance.AttributePointsPerLevel;
                                            Log.Info($"{contributor.Name} повысил уровень до {contributor.Level}!");
                                        }

                                        var contributorLoot = LootManager.RollLoot(monster.TemplateId);
                                        playerLootDict[contributor.Id] = new CorpsePlayerLoot
                                        {
                                            PlayerName = contributor.Name,
                                            Gold = goldReward,
                                            Items = contributorLoot,
                                            DamagePercent = (int)(dmgShare * 100)
                                        };

                                        var contribClient = World.FindClientByPlayer(contributor);
                                        if (contribClient != null)
                                        {
                                            if (xpReward > 0)
                                                await ChatTo(contribClient, ChatChannel.System, "Система", $"[Группа] Вы получили {xpReward} опыта за {monster.Name} ({(int)(dmgShare * 100)}% урона).");

                                            int personalItems = contributorLoot.Count;
                                            if (personalItems > 0 || goldReward > 0)
                                                await ChatTo(contribClient, ChatChannel.System, "Система", $"Тело {monster.Name} осталось на земле. Нажмите, чтобы забрать дроп ({personalItems} предм., {goldReward} зол.).");
                                            else
                                                await ChatTo(contribClient, ChatChannel.System, "Система", $"Тело {monster.Name} осталось на земле. Дропа нет.");

                                            var questResults = QuestManager.IncrementKillProgress(contributor, monster.TemplateId);
                                            foreach (var (title, current, target, completed) in questResults)
                                            {
                                                string msg = completed
                                                    ? $"[Задание] {title}: {current}/{target} — задание выполнено! Вернитесь на доску заданий, чтобы сдать."
                                                    : $"[Задание] {title}: {current}/{target}";
                                                await ChatTo(contribClient, ChatChannel.System, "Система", msg);
                                            }
                                            await Hub.SendQuestLog(contribClient, contributor);
                                        }
                                    }

                                    CorpseManager.CreateCorpse(monster, new List<Item>(), playerLootDict);
                                    MonsterManager.RemoveMonster(monster);
                                }
                                else
                                {
                                    var topContributor = damageTracker.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
                                    Player soloRecipient = topContributor.Key != Guid.Empty && World.TryGetPlayer(topContributor.Key, out var topP) && topP != null
                                        ? topP : pl;

                                    soloRecipient.Experience += monster.XpReward;

                                    int xpNeeded = Balance.XpNeededForNextLevel(soloRecipient.Level);
                                    if (soloRecipient.Experience >= xpNeeded)
                                    {
                                        soloRecipient.Level++;
                                        soloRecipient.Experience -= xpNeeded;
                                        soloRecipient.MaxHealth += Balance.MaxHealthPerLevel;
                                        soloRecipient.Health = soloRecipient.MaxHealth;
                                        soloRecipient.AttributePoints += Balance.AttributePointsPerLevel;
                                        Log.Info($"{soloRecipient.Name} повысил уровень до {soloRecipient.Level}!");
                                    }

                                    var soloLoot = LootManager.RollLoot(monster.TemplateId);
                                    var soloPlayerLoot = new Dictionary<Guid, CorpsePlayerLoot>
                                    {
                                        [soloRecipient.Id] = new CorpsePlayerLoot
                                        {
                                            PlayerName = soloRecipient.Name,
                                            Gold = monster.GoldReward,
                                            Items = soloLoot,
                                            DamagePercent = 100
                                        }
                                    };

                                    CorpseManager.CreateCorpse(monster, new List<Item>(), soloPlayerLoot);
                                    MonsterManager.RemoveMonster(monster);

                                    var soloClient = World.FindClientByPlayer(soloRecipient);
                                    if (soloClient != null)
                                    {
                                        int totalItems = soloLoot.Count;
                                        if (totalItems > 0 || monster.GoldReward > 0)
                                            await Hub.SendToClient(soloClient, new GameMessage
                                            {
                                                Type = "chat",
                                                Data = new { Name = "Система", Text = $"Тело {monster.Name} осталось на земле. Нажмите, чтобы забрать дроп ({totalItems} предм., {monster.GoldReward} зол.)." }
                                            });
                                        else
                                            await Hub.SendToClient(soloClient, new GameMessage
                                            {
                                                Type = "chat",
                                                Data = new { Name = "Система", Text = $"Тело {monster.Name} осталось на земле. Дропа нет." }
                                            });

                                        var questResults = QuestManager.IncrementKillProgress(soloRecipient, monster.TemplateId);
                                        foreach (var (title, current, target, completed) in questResults)
                                        {
                                            string msg = completed
                                                ? $"[Задание] {title}: {current}/{target} — задание выполнено! Вернитесь на доску заданий, чтобы сдать."
                                                : $"[Задание] {title}: {current}/{target}";
                                            await Hub.SendToClient(soloClient, new GameMessage
                                            {
                                                Type = "chat",
                                                Data = new { Name = "Система", Text = msg }
                                            });
                                        }
                                        await Hub.SendQuestLog(soloClient, soloRecipient);
                                    }
                                }
                            }
                            else
                            {
                                if (isEvaded)
                                    await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} уклонился от вашей атаки.");
                                else
                                {
                                    string critText = isCrit ? " (КРИТ!)" : "";
                                    await ChatTo(client, ChatChannel.Combat, "Бой", $"Вы нанесли {dmgToMonster} урона{critText} {monster.Name}.");
                                }

                                string logCrit = isCrit ? " (КРИТ!)" : "";
                                string logEvade = isEvaded ? " (УКЛОНЕНИЕ!)" : "";
                                Log.Debug($"{pl.Name} атаковал {monster.Name}: {dmgToMonster} урона{logCrit}, {dmgToPlayer} урона{logEvade}");

                                if (!isEvaded)
                                {
                                    var dmgMsg = new GameMessage
                                    {
                                        Type = "damage",
                                        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = dmgToMonster, IsCrit = isCrit }
                                    };
                                    await Hub.SendToClient(client, dmgMsg);
                                    await Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
                                }

                                await Hub.SendToClient(client, new GameMessage
                                {
                                    Type = "combat_update",
                                    Data = new { MonsterName = monster.Name, MonsterHealth = monster.Health, MonsterMaxHealth = monster.MaxHealth }
                                });

                                await Hub.SendToClient(client, new GameMessage
                                {
                                    Type = "combat_state",
                                    Data = new { InCombat = true, TargetId = monster.Id.ToString(), TargetName = monster.Name, TargetHp = monster.Health, TargetMaxHp = monster.MaxHealth }
                                });

                            if (!isEvaded && dmgToPlayer > 0)
                            {
                                pl.Health -= dmgToPlayer;
                                pl.LastDamagedTime = DateTime.UtcNow;
                                var hitMsg = new GameMessage
                                {
                                    Type = "damage",
                                    Data = new { Target = "player", PlayerName = pl.Name, MonsterId = monster.Id.ToString(), X = pl.X, Y = pl.Y, Amount = dmgToPlayer, IsCrit = false }
                                };
                                await Hub.SendToClient(client, hitMsg);
                                await Hub.SendDamageNearbyAsync(pl.X, pl.Y, hitMsg, pl);
                                await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} нанёс вам {dmgToPlayer} урона. ({pl.Health}/{pl.MaxHealth + pl.Equipment.GetBonusMaxHealth()}) HP");
                                if (pl.PartyId.HasValue)
                                {
                                    var party = PartyManager.GetParty(pl.PartyId.Value);
                                    if (party != null) await PartyManager.SendPartyUpdateAsync(party);
                                }
                            }

                                if (pl.Health <= 0)
                                {
                                    pl.Health = Balance.RespawnHealth(pl.MaxHealth);
                                    int lostGold = Balance.ComputeDeathGoldLoss(pl.Gold);
                                    pl.Gold -= lostGold;
                                    pl.Combat.Cancel();
                                    pl.Combat.OffHandLastAttackTime = DateTime.MinValue;
                                    Log.Info($"{pl.Name} погиб! Потеряно {lostGold} золота. Телепортация.");
                                    await ChatTo(client, ChatChannel.System, "Система", $"Вы погибли! Потеряно {lostGold} золота. Телепортация...");
                                    await Hub.SendToClient(client, new GameMessage
                                    {
                                        Type = "combat_state",
                                        Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
                                    });

                                    int sx = MerchantManager.MerchantX + World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
                                    int sy = MerchantManager.MerchantY + World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
                                    sx = Math.Clamp(sx, 0, World.Map.Width - 1);
                                    sy = Math.Clamp(sy, 0, World.Map.Height - 1);
                                    pl.X = sx;
                                    pl.Y = sy;

                                    if (pl.PartyId.HasValue)
                                    {
                                        var party = PartyManager.GetParty(pl.PartyId.Value);
                                        if (party != null) await PartyManager.SendPartyUpdateAsync(party);
                                    }
                                }
                            }
                        } // конец if (mainAttackReady)

                        // === OFF-HAND ATTACK (dual wield, с задержкой) ===
                        if (!pl.Combat.HasTarget) { await Hub.SendStatusAsync(client, pl); changed = true; continue; }
                        if (!mainFired && offHandReady && pl.Equipment.IsDualWielding())
                        {
                            var offMonster = MonsterManager.FindMonsterById(pl.Combat.TargetMonsterId!.Value);
                            if (offMonster == null || offMonster.Health <= 0)
                            {
                                // Манекен: мгновенное полное лечение
                                if (offMonster != null && offMonster.IsMannequin && offMonster.Health <= 0)
                                {
                                    offMonster.Health = offMonster.MaxHealth;
                                    offMonster.LastDamagedTime = DateTime.UtcNow;
                                }
                                pl.Combat.OffHandLastAttackTime = DateTime.MinValue;
                            }
                            else
                            {
                                pl.Combat.OffHandLastAttackTime = DateTime.UtcNow;

                                var (ohDmg, ohCrit, ohEvaded) = MonsterManager.CalculateOffHandAttack(pl, offMonster);
                                if (ohDmg > 0)
                                {
                                    offMonster.Health -= ohDmg;
                                    offMonster.LastDamagedTime = DateTime.UtcNow;
                                    offMonster.DamageTracker[pl.Id] = offMonster.DamageTracker.GetValueOrDefault(pl.Id) + ohDmg;

                                    string ohCritText = ohCrit ? " (КРИТ!)" : "";
                                    string ohWeaponName = pl.Equipment.GetOffHandWeapon()?.Name ?? "оружие";
                                    await ChatTo(client, ChatChannel.Combat, "Бой", $"Второе оружие ({ohWeaponName}) нанесло {ohDmg} урона{ohCritText} {offMonster.Name}.");

                                    var ohDmgMsg = new GameMessage
                                    {
                                        Type = "damage",
                                        Data = new { Target = "monster", MonsterId = offMonster.Id.ToString(), X = offMonster.X, Y = offMonster.Y, Amount = ohDmg, IsCrit = ohCrit }
                                    };
                                    await Hub.SendToClient(client, ohDmgMsg);
                                    await Hub.SendDamageNearbyAsync(offMonster.X, offMonster.Y, ohDmgMsg, pl);

                                    if (offMonster.Health <= 0)
                                    {
                                        // Манекен: мгновенное полное лечение вместо смерти
                                        if (offMonster.IsMannequin)
                                        {
                                            offMonster.Health = offMonster.MaxHealth;
                                            offMonster.LastDamagedTime = DateTime.UtcNow;
                                            await ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{(ohCrit ? " (КРИТ!)" : "")}");
                                            await Hub.SendToClient(client, new GameMessage
                                            {
                                                Type = "combat_update",
                                                Data = new { MonsterName = offMonster.Name, MonsterHealth = offMonster.Health, MonsterMaxHealth = offMonster.MaxHealth }
                                            });
                                            continue;
                                        }

                                        pl.Combat.Cancel();
                                        pl.Combat.OffHandLastAttackTime = DateTime.MinValue;

                                        int shownDmg = Math.Max(0, offMonster.Health + ohDmg);
                                        string ohCritText2 = ohCrit ? " (КРИТ!)" : "";
                                        Log.Info($"{pl.Name} убил {offMonster.Name} вторым оружием!{ohCritText2}");
                                        await ChatTo(client, ChatChannel.Combat, "Бой", $"Второе оружие нанесло {shownDmg} урона{ohCritText2} и убило {offMonster.Name}!");

                                        await Hub.SendToClient(client, new GameMessage
                                        {
                                            Type = "combat_state",
                                            Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
                                        });

                                        // Тот же код лута/опыта что и для основного удара
                                        var damageTracker = offMonster.DamageTracker;
                                        var soloRecipient = damageTracker.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
                                        Player soloP = soloRecipient.Key != Guid.Empty && World.TryGetPlayer(soloRecipient.Key, out var topP) && topP != null
                                            ? topP : pl;

                                        soloP.Experience += offMonster.XpReward;
                                        int xpNeeded = Balance.XpNeededForNextLevel(soloP.Level);
                                        if (soloP.Experience >= xpNeeded)
                                        {
                                            soloP.Level++;
                                            soloP.Experience -= xpNeeded;
                                            soloP.MaxHealth += Balance.MaxHealthPerLevel;
                                            soloP.Health = soloP.MaxHealth;
                                            soloP.AttributePoints += Balance.AttributePointsPerLevel;
                                        }

                                        var soloLoot = LootManager.RollLoot(offMonster.TemplateId);
                                        var soloPlayerLoot = new Dictionary<Guid, CorpsePlayerLoot>
                                        {
                                            [soloP.Id] = new CorpsePlayerLoot
                                            {
                                                PlayerName = soloP.Name,
                                                Gold = offMonster.GoldReward,
                                                Items = soloLoot,
                                                DamagePercent = 100
                                            }
                                        };
                                        CorpseManager.CreateCorpse(offMonster, new List<Item>(), soloPlayerLoot);
                                        MonsterManager.RemoveMonster(offMonster);

                                        var soloClient = World.FindClientByPlayer(soloP);
                                        if (soloClient != null)
                                        {
                                            int totalItems = soloLoot.Count;
                                            if (totalItems > 0 || offMonster.GoldReward > 0)
                                                await Hub.SendToClient(soloClient, new GameMessage
                                                {
                                                    Type = "chat",
                                                    Data = new { Name = "Система", Text = $"Тело {offMonster.Name} осталось на земле. Нажмите, чтобы забрать дроп ({totalItems} предм., {offMonster.GoldReward} зол.)." }
                                                });
                                            else
                                                await Hub.SendToClient(soloClient, new GameMessage
                                                {
                                                    Type = "chat",
                                                    Data = new { Name = "Система", Text = $"Тело {offMonster.Name} осталось на земле. Дропа нет." }
                                                });

                                            var questResults = QuestManager.IncrementKillProgress(soloP, offMonster.TemplateId);
                                            foreach (var (title, current, target, completed) in questResults)
                                            {
                                                string msg = completed
                                                    ? $"[Задание] {title}: {current}/{target} — задание выполнено!"
                                                    : $"[Задание] {title}: {current}/{target}";
                                                await Hub.SendToClient(soloClient, new GameMessage
                                                {
                                                    Type = "chat",
                                                    Data = new { Name = "Система", Text = msg }
                                                });
                                            }
                                            await Hub.SendQuestLog(soloClient, soloP);
                                        }
                                    }
                                    else
                                    {
                                        await Hub.SendToClient(client, new GameMessage
                                        {
                                            Type = "combat_update",
                                            Data = new { MonsterName = offMonster.Name, MonsterHealth = offMonster.Health, MonsterMaxHealth = offMonster.MaxHealth }
                                        });
                                        await Hub.SendToClient(client, new GameMessage
                                        {
                                            Type = "combat_state",
                                            Data = new { InCombat = true, TargetId = offMonster.Id.ToString(), TargetName = offMonster.Name, TargetHp = offMonster.Health, TargetMaxHp = offMonster.MaxHealth }
                                        });
                                    }
                                }
                                else if (ohEvaded)
                                {
                                    await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} уклонился от удара вторым оружием.");
                                }
                            }
                        }

                        await Hub.SendStatusAsync(client, pl);

                        changed = true;
                    }
                }
                if (changed) await Hub.BroadcastMapAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка боевого цикла", ex);
            }
        }
    }

    internal static int GetAttackSpeed(Player player)
        => Balance.GetAttackSpeedWithWeapon(player.Agility, player.Equipment.GetWeaponSpeedModifier());

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
                    player.Health -= damage;
                    player.LastDamagedTime = DateTime.UtcNow;
        var client = World.FindClientByPlayer(player);
                    if (client == null) continue;

                    var hitMsg = new GameMessage
                    {
                        Type = "damage",
                        Data = new { Target = "player", PlayerName = player.Name, MonsterId = (string?)null, X = player.X, Y = player.Y, Amount = damage, IsCrit = false }
                    };
                    await Hub.SendToClient(client, hitMsg);
                    await Hub.SendDamageNearbyAsync(player.X, player.Y, hitMsg, player);

                    await Hub.SendToClient(client, new GameMessage
                    {
                        Type = "combat_update",
                        Data = new { MonsterName = monster.Name, MonsterHealth = monster.Health, MonsterMaxHealth = monster.MaxHealth }
                    });
                    await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} нанёс вам {damage} урона. ({player.Health}/{player.MaxHealth + player.Equipment.GetBonusMaxHealth()}) HP");

                    if (player.PartyId.HasValue)
                    {
                        var party = PartyManager.GetParty(player.PartyId.Value);
                        if (party != null) await PartyManager.SendPartyUpdateAsync(party);
                    }

                    if (player.Health <= 0)
                    {
                        player.Health = Balance.RespawnHealth(player.MaxHealth);
                        int lostGold = Balance.ComputeDeathGoldLoss(player.Gold);
                        player.Gold -= lostGold;
                        player.Combat.Cancel();
                        player.Interaction.Clear();
                        player.Movement.Stop();
                        Log.Info($"{player.Name} погиб от {monster.Name}! Потеряно {lostGold} золота.");
                        await ChatTo(client, ChatChannel.System, "Система", $"Вы погибли от {monster.Name}! Потеряно {lostGold} золота. Телепортация...");
                        await Hub.SendToClient(client, new GameMessage
                        {
                            Type = "combat_state",
                            Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
                        });

                        int sx = MerchantManager.MerchantX + World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
                        int sy = MerchantManager.MerchantY + World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
                        sx = Math.Clamp(sx, 0, World.Map.Width - 1);
                        sy = Math.Clamp(sy, 0, World.Map.Height - 1);
                        player.X = sx;
                        player.Y = sy;
                        await Hub.BroadcastMapAsync();

                        if (player.PartyId.HasValue)
                        {
                            var party = PartyManager.GetParty(player.PartyId.Value);
                            if (party != null) await PartyManager.SendPartyUpdateAsync(party);
                        }
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

                        if (pl.PartyId.HasValue)
                        {
                            var party = PartyManager.GetParty(pl.PartyId.Value);
                            if (party != null) await PartyManager.SendPartyUpdateAsync(party);
                        }
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
                        DebuffManager.TickDebuffs(mon);
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
}
