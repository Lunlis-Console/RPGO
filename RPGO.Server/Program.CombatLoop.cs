using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Server.MessageHandlers;

namespace RPGGame.Server;

public partial class Program
{
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
                    if (pl.IsDead) continue;
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
                                await Hub.SendToClient(mClient, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                                await ChatTo(mClient, ChatChannel.Combat, "Бой", $"{monster.Name} восстановил все HP!");
                            }
                            continue;
                        }
                        pl.Combat.Cancel();
                        var client = World.FindClientByPlayer(pl);
                        if (client != null)
                        {
                            await Hub.SendToClient(client, GameMessage.ResetCombat());
                        }
                        changed = true;
                        continue;
                    }

                    int dist = Math.Abs(pl.X - monster.X) + Math.Abs(pl.Y - monster.Y);
                    int weaponRange = pl.Equipment.GetWeaponAttackRange();

                    var w = pl.Equipment[EquipmentSlots.RightHand];
                    Log.Debug($"[Combat] {pl.Name} vs {monster.Name}: dist={dist} weaponRange={weaponRange} weapon='{w?.Name ?? "null"}' AttackRange={w?.AttackRange ?? -1} TemplateId='{w?.TemplateId ?? ""}'");

                    int moveIntervalMs = Balance.MoveIntervalMs(pl.Speed);
                    bool canMove = (DateTime.UtcNow - pl.Movement.LastMoveTime).TotalMilliseconds >= moveIntervalMs;

                    if (dist > weaponRange)
                    {
                        int stepX = Math.Sign(monster.X - pl.X);
                        int stepY = Math.Sign(monster.Y - pl.Y);

                        if (!canMove) continue;

                        int mx = 0, my = 0;
                        if (stepX != 0 && stepY != 0)
                        {
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

                        int attackIntervalMs = Balance.AttackIntervalMs(Balance.GetAttackSpeed(pl.Agility), pl.Equipment.GetWeaponSpeedModifier());
                        bool mainAttackReady = (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= attackIntervalMs;
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
                                        await Hub.SendToClient(skillClient, GameMessage.Chat("Бой", onCd
                                            ? $"«{cand.Name}» ещё на перезарядке — пропускаем."
                                            : $"«{cand.Name}»: недостаточно маны ({pl.Mana}/{cand.MpCost}) — пропускаем."));
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

                            // === Оружейный прок (ДО расчёта урона, чтобы дебаффы действовали сразу) ===
                            string subtype = pl.Equipment.GetWeaponSubtype();
                            if (!string.IsNullOrEmpty(subtype))
                            {
                                var (debuff, isNew) = DebuffManager.OnWeaponProc(pl, monster, subtype);

                                if (debuff != null)
                                {
                                    string action = isNew ? "наложено" : "обновлено";
                                    string targetName = weaponAffectsTarget(subtype) ? monster.Name : pl.Name;
                                    await ChatTo(client, ChatChannel.Combat, "Бой",
                                        $"{debuff.DisplayName} {action} на {targetName} ({debuff.DurationMs / 1000}с)");

                                    if (monster.ActiveDebuffs.Count > 0)
                                        await SendTargetDebuffUpdateAsync(monster);
                                }
                            }

                            var (dmgToMonster, dmgToPlayer, monsterDead, isCrit, isEvaded) =
                                MonsterManager.CalculateCombat(pl, monster, queuedSkill == null && weaponRange <= 1);

                            // Клив: наносим урон 50% по 3 клеткам
                            if (!isEvaded && weaponRange <= 1 && DebuffManager.HasDebuff(pl, DebuffType.CleaveReady))
                            {
                                DebuffManager.ClearDebuffs(pl);
                                MonsterManager.CalculateCleave(pl, monster);
                            }

                            if (queuedSkill != null)
                            {
                                int baseDamage = (int)Math.Max(Balance.MinDamage, MonsterManager.GetEffectiveAttack(pl, pl.GetMaxAttackDamage()) - MonsterManager.GetEffectiveDefense(monster));
                                int skillDamage = (int)Math.Max(Balance.MinDamage, baseDamage * queuedSkill.DamageMultiplier);
                                dmgToMonster = MonsterManager.ApplyDmgReduction(pl, skillDamage);
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

                            // === ДАЛЬНЕЕ ОРУЖИЕ: снаряд вместо мгновенного урона ===
                            if (weaponRange > 1 && !isEvaded)
                            {
                                string visualType = subtype == "bow" ? "arrow" : "magic_bolt";
                                var proj = ProjectileManager.Spawn(pl, monster, visualType, dmgToMonster, isCrit);
                                await ProjectileManager.BroadcastSpawn(proj);
                                await Hub.SendStatusAsync(client, pl);
                                changed = true;
                                continue;
                            }

                            if (monsterDead)
                            {
                                // Манекен: мгновенное полное лечение вместо смерти
                                if (monster.IsMannequin)
                                {
                                    monster.Health = monster.MaxHealth;
                                    monster.LastDamagedTime = DateTime.UtcNow;
                                    await ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{(isCrit ? " (КРИТ!)" : "")}");
                                    await Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
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

                                await Hub.SendToClient(client, GameMessage.ResetCombat());

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

                                        if (contributor.TryLevelUp()) Log.Info($"{contributor.Name} повысил уровень до {contributor.Level}!");

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

                                    if (soloRecipient.TryLevelUp()) Log.Info($"{soloRecipient.Name} повысил уровень до {soloRecipient.Level}!");

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
                                            await Hub.SendToClient(soloClient, GameMessage.Chat("Система", $"Тело {monster.Name} осталось на земле. Нажмите, чтобы забрать дроп ({totalItems} предм., {monster.GoldReward} зол.)."));
                                        else
                                            await Hub.SendToClient(soloClient, GameMessage.Chat("Система", $"Тело {monster.Name} осталось на земле. Дропа нет."));

                                        var questResults = QuestManager.IncrementKillProgress(soloRecipient, monster.TemplateId);
                                        foreach (var (title, current, target, completed) in questResults)
                                        {
                                            string msg = completed
                                                ? $"[Задание] {title}: {current}/{target} — задание выполнено! Вернитесь на доску заданий, чтобы сдать."
                                                : $"[Задание] {title}: {current}/{target}";
                                            await Hub.SendToClient(soloClient, GameMessage.Chat("Система", msg));
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

                                await Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));

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
                                await PartyManager.SendUpdateForAsync(pl);
                            }

                                if (pl.Health <= 0)
                                {
                                    pl.Combat.Cancel();
                                    pl.Combat.OffHandLastAttackTime = DateTime.MinValue;
                                    await HandlePlayerDeath(pl, client);
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
                                            await Hub.SendToClient(client, GameMessage.CombatUpdate(offMonster.Name, offMonster.Health, offMonster.MaxHealth));
                                            continue;
                                        }

                                        pl.Combat.Cancel();
                                        pl.Combat.OffHandLastAttackTime = DateTime.MinValue;

                                        int shownDmg = Math.Max(0, offMonster.Health + ohDmg);
                                        string ohCritText2 = ohCrit ? " (КРИТ!)" : "";
                                        Log.Info($"{pl.Name} убил {offMonster.Name} вторым оружием!{ohCritText2}");
                                        await ChatTo(client, ChatChannel.Combat, "Бой", $"Второе оружие нанесло {shownDmg} урона{ohCritText2} и убило {offMonster.Name}!");

                                        await Hub.SendToClient(client, GameMessage.ResetCombat());

                                        // Тот же код лута/опыта что и для основного удара
                                        var damageTracker = offMonster.DamageTracker;
                                        var soloRecipient = damageTracker.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
                                        Player soloP = soloRecipient.Key != Guid.Empty && World.TryGetPlayer(soloRecipient.Key, out var topP) && topP != null
                                            ? topP : pl;

                                        soloP.Experience += offMonster.XpReward;
                                        soloP.TryLevelUp();

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
                                                await Hub.SendToClient(soloClient, GameMessage.Chat("Система", $"Тело {offMonster.Name} осталось на земле. Нажмите, чтобы забрать дроп ({totalItems} предм., {offMonster.GoldReward} зол.)."));
                                            else
                                                await Hub.SendToClient(soloClient, GameMessage.Chat("Система", $"Тело {offMonster.Name} осталось на земле. Дропа нет."));

                                            var questResults = QuestManager.IncrementKillProgress(soloP, offMonster.TemplateId);
                                            foreach (var (title, current, target, completed) in questResults)
                                            {
                                                string msg = completed
                                                    ? $"[Задание] {title}: {current}/{target} — задание выполнено!"
                                                    : $"[Задание] {title}: {current}/{target}";
                                                await Hub.SendToClient(soloClient, GameMessage.Chat("Система", msg));
                                            }
                                            await Hub.SendQuestLog(soloClient, soloP);
                                        }
                                    }
                                    else
                                    {
                                        await Hub.SendToClient(client, GameMessage.CombatUpdate(offMonster.Name, offMonster.Health, offMonster.MaxHealth));
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

    private static async Task HandlePlayerDeath(Player pl, ClientConnection client)
    {
        int lostGold = Balance.ComputeDeathGoldLoss(pl.Gold);
        pl.Gold -= lostGold;
        pl.IsDead = true;
        pl.DeathTime = DateTime.UtcNow;
        Log.Info($"{pl.Name} погиб! Потеряно {lostGold} золота. Таймер 5с.");
        await Hub.SendToClient(client, GameMessage.ResetCombat());
        await Hub.SendToClient(client, GameMessage.PlayerDeath(lostGold));
        await ChatTo(client, ChatChannel.System, "Система", $"Вы погибли! Потеряно {lostGold} золота. Возрождение через 5 сек...");
        await PartyManager.SendUpdateForAsync(pl);
    }

    internal static async Task RespawnPlayer(Player pl)
    {
        pl.IsDead = false;
        pl.Health = Balance.RespawnHealth(pl.MaxHealth);

        int sx = MerchantManager.MerchantX + World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
        int sy = MerchantManager.MerchantY + World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
        sx = Math.Clamp(sx, 0, World.Map.Width - 1);
        sy = Math.Clamp(sy, 0, World.Map.Height - 1);
        pl.X = sx;
        pl.Y = sy;

        var client = World.FindClientByPlayer(pl);
        if (client != null)
        {
            await ChatTo(client, ChatChannel.System, "Система", "Вы возродились!");
            await Hub.SendToClient(client, GameMessage.SystemChat("Вы возродились!"));
        }
        await Hub.BroadcastMapAsync();
        await PartyManager.SendUpdateForAsync(pl);
        if (client != null)
            await Hub.SendStatusAsync(client, pl);
    }

    private static bool weaponAffectsTarget(string subtype) => subtype is "dagger" or "spear" or "mace" or "hammer" or "greathammer";
}
