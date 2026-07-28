using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server;

/// <summary>
/// Боевая логика: цикл атак, обработка навыков, преследование, смерть/возрождение.
/// Вынесена из GameServerHost для разделения ответственности.
/// </summary>
public class CombatService
{
    private readonly GameServices _svc;

    public CombatService(GameServices svc)
    {
        _svc = svc;
    }

    private Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
    {
        if (conn == null) return Task.CompletedTask;
        return _svc.Hub.SendChatToAsync(conn, channel, name, text);
    }

    // ──────────────── Боевой цикл ────────────────

    public async Task RunCombatLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(200);
                bool changed = false;
                foreach (var pl in _svc.World.GetPlayersSnapshot())
                {
                    if (pl.IsDead || !pl.Combat.HasTarget) continue;
                    if (_svc.Debuffs.HasDebuff(pl, DebuffType.Stun)) continue;

                    // PvP target
                    if (pl.Combat.IsPvPTarget)
                    {
                        changed |= await RunPvPTick(pl);
                        continue;
                    }

                    var monster = _svc.Monsters.FindMonsterById(pl.Combat.TargetMonsterId!.Value);
                    if (monster == null || monster.Health <= 0 || monster.ZoneId != pl.CurrentZoneId)
                    {
                        await HandleInvalidTarget(pl, monster);
                        changed = true;
                        continue;
                    }

                    int dist = Math.Abs(pl.X - monster.X) + Math.Abs(pl.Y - monster.Y);
                    int weaponRange = pl.Equipment.GetWeaponAttackRange();
                    var offHandWeapon = pl.Equipment.GetOffHandWeapon();
                    int offHandRange = offHandWeapon?.AttackRange ?? 0;
                    bool offHandCanShoot = offHandRange > 1 && dist <= offHandRange;

                     int attackIntervalMs = Balance.AttackIntervalMs(
                         Balance.GetAttackSpeed(pl.Agility), pl.Equipment.GetWeaponSpeedModifier());
                     double speedBuff = 1.0 + _svc.Debuffs.GetDebuffValue(pl, DebuffType.AttackSpeedBonus);
                     attackIntervalMs = (int)(attackIntervalMs / speedBuff);
                     bool offHandReady = pl.Equipment.IsDualWielding()
                         && pl.Combat.LastAttackTime > pl.Combat.OffHandLastAttackTime
                         && (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= Math.Max(Balance.OffHandDelayMinMs, (int)(attackIntervalMs * Balance.OffHandDelayFraction));
                     bool offHandCanFireNow;
                     if (offHandCanShoot)
                         offHandCanFireNow = (DateTime.UtcNow - pl.Combat.OffHandLastAttackTime).TotalMilliseconds >= attackIntervalMs;
                     else
                         offHandCanFireNow = pl.Equipment.IsDualWielding()
                             && (DateTime.UtcNow - pl.Combat.OffHandLastAttackTime).TotalMilliseconds >= Math.Max(Balance.OffHandDelayMinMs, (int)(attackIntervalMs * Balance.OffHandDelayFraction));

                    if (dist > weaponRange && !offHandCanShoot)
                    {
                        if (ChaseTarget(pl, monster)) changed = true;
                    }
                    else if (dist > weaponRange && offHandCanShoot)
                    {
                        var client = _svc.World.FindClientByPlayer(pl);
                        if (client == null) continue;
                        if (offHandCanFireNow)
                        {
                            await ExecuteOffHandAttack(pl, client);
                            await _svc.Hub.SendStatusAsync(client, pl);
                            changed = true;
                        }
                    }
                    else
                    {
                        var client = _svc.World.FindClientByPlayer(pl);
                        if (client == null) continue;

                        // Комбо-навык: отложенные удары
                        if (pl.Combat.PendingSkillHitsRemaining > 0)
                        {
                            // «ЭТО ДУЭЛЬ!»: цель (монстр) сменила таргет с игрока — наказание
                            if (pl.Combat.PendingSkillId == "SK0009" && monster.AggroTarget != pl)
                            {
                                await ExecuteDuelPunish(pl, monster, client);
                                changed = true;
                                continue;
                            }
                            if (pl.Combat.PendingSkillTargetId != monster.Id)
                            {
                                pl.Combat.PendingSkillHitsRemaining = 0;
                                pl.Combat.PendingSkillId = null;
                            }
                            else
                            {
                                double elapsed = (DateTime.UtcNow - pl.Combat.PendingSkillLastHitTime).TotalMilliseconds;
                                if (elapsed >= Balance.SlashHitIntervalMs)
                                {
                                    await ExecuteComboHit(pl, monster, client);
                                    changed = true;
                                }
                                await _svc.Hub.SendStatusAsync(client, pl);
                                changed = true;
                                continue;
                            }
                        }

                        // Бафф-навыки применяются сразу, не дожидаясь атаки
                        await ProcessInstantBuffs(pl, client);

                        var queuedSkill = await ProcessSkillQueue(pl, client);
                        bool hasAttackSkill = queuedSkill != null && !InstantBuffSkills.Contains(queuedSkill.Id);

                        bool mainAttackReady = (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= attackIntervalMs;

                        if (!mainAttackReady && !offHandReady && !hasAttackSkill) continue;

                        bool mainFired = false;
                        if (mainAttackReady || hasAttackSkill)
                        {
                            mainFired = true;
                            await ExecuteMainHandAttack(pl, monster, client, queuedSkill, weaponRange);
                        }

                        if (pl.Combat.HasTarget && !mainFired && offHandReady && pl.Equipment.IsDualWielding())
                        {
                            await ExecuteOffHandAttack(pl, client);
                        }

                        await _svc.Hub.SendStatusAsync(client, pl);
                        changed = true;
                    }
                }
                if (changed) await _svc.Hub.BroadcastMapAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка боевого цикла", ex);
            }
        }
    }

    // ──────────────── Атаки ────────────────

    public async Task ExecuteMainHandAttack(Player pl, Monster monster, ClientConnection client, Skill? queuedSkill, int weaponRange)
    {
        pl.Combat.LastAttackTime = DateTime.UtcNow;

        // Face the target before attacking
        int dx = monster.X - pl.X;
        int dy = monster.Y - pl.Y;
        int dist = Math.Abs(dx) + Math.Abs(dy);
        if (Math.Abs(dx) >= Math.Abs(dy))
            pl.Facing = dx > 0 ? "right" : "left";
        else
            pl.Facing = dy > 0 ? "down" : "up";

        string subtype = pl.Equipment.GetWeaponSubtype();
        string attackHand;
        var effectiveMain = pl.Equipment.GetEffectiveMainHandWeapon();
        if (effectiveMain != null)
        {
            attackHand = Equipment.IsCasterOffhand(effectiveMain) ? "off" : "main";
        }
        else
        {
            // No right-hand weapon; check if left hand has a non-caster one-hander
            var lh = pl.Equipment.Slots.TryGetValue("lhand", out var l) ? l : null;
            attackHand = (lh != null && !Equipment.IsCasterOffhand(lh) && !lh.TwoHanded) ? "off" : "main";
        }
        bool forceProc = queuedSkill?.Id == "SK0001" && weaponRange <= 1;
        if (!string.IsNullOrEmpty(subtype))
        {
            var (debuff, isNew) = forceProc
                ? _svc.Debuffs.ForceWeaponProc(pl, monster, subtype)
                : _svc.Debuffs.OnWeaponProc(pl, monster, subtype);
            if (debuff != null)
            {
                string action = isNew ? "наложено" : "обновлено";
                string targetName = WeaponAffectsTarget(subtype) ? monster.Name : pl.Name;
                await ChatTo(client, ChatChannel.Combat, "Бой",
                    $"{debuff.DisplayName} {action} на {targetName} ({debuff.DurationMs / 1000}с)");
                if (monster.ActiveDebuffs.Count > 0)
                    await SendTargetDebuffUpdateAsync(monster);
            }
        }

        var (dmgToMonster, dmgToPlayer, monsterDead, isCrit, isEvaded, isParried, isBlocked) =
            _svc.Monsters.CalculateCombat(pl, monster, queuedSkill == null && weaponRange <= 1, weaponRange <= 1);

        if (queuedSkill == null)
            await TryLifesteal(pl, dmgToMonster, weaponRange <= 1, client);

        if (!isEvaded && weaponRange <= 1 && _svc.Debuffs.HasDebuff(pl, DebuffType.CleaveReady))
        {
            _svc.Debuffs.ClearDebuffs(pl);
            _svc.Monsters.CalculateCleave(pl, monster);
        }

        if (queuedSkill != null)
        {
            bool skillBlocked = queuedSkill.Id == "SK0001" && weaponRange > 1;
            if (skillBlocked)
            {
                await ChatTo(client, ChatChannel.Combat, "Бой",
                    $"«{queuedSkill.Name}» доступен только с оружием ближнего боя.");
                pl.QueuedSkillIds.RemoveAt(0);
                await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
            }
            else if (queuedSkill.Id == "SK0002")
            {
                var buff = ActiveDebuff.Create(DebuffType.AttackSpeedBonus, Balance.AttackSpeedBonusValue,
                    Balance.AttackSpeedBonusDurationMs, "skill", "Проворность",
                    $"Увеличивает скорость атаки на {(int)(Balance.AttackSpeedBonusValue * 100)}%");
                _svc.Debuffs.ApplyDebuff(pl, buff);
                pl.Mana = Math.Max(0, pl.Mana - queuedSkill.MpCost);
                pl.LastSkillUse[queuedSkill.Id] = DateTime.UtcNow;
                pl.QueuedSkillIds.RemoveAt(0);
                await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
                await _svc.Hub.SendToClient(client, new GameMessage
                {
                    Type = "skill_cooldown",
                    Data = new { SkillId = queuedSkill.Id, RemainingMs = queuedSkill.CooldownMs, TotalMs = queuedSkill.CooldownMs }
                });
                await ChatTo(client, ChatChannel.Combat, "Бой", $"Применён навык «{queuedSkill.Name}»! Проворность на 10 сек.");
            }
            else if (queuedSkill.Id == "SK0004")
            {
                pl.Mana = Math.Max(0, pl.Mana - queuedSkill.MpCost);
                pl.LastSkillUse[queuedSkill.Id] = DateTime.UtcNow;
                pl.QueuedSkillIds.RemoveAt(0);
                await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
                await _svc.Hub.SendToClient(client, new GameMessage
                {
                    Type = "skill_cooldown",
                    Data = new { SkillId = queuedSkill.Id, RemainingMs = queuedSkill.CooldownMs, TotalMs = queuedSkill.CooldownMs }
                });

                // Первый удар — правая рука
                await _svc.Hub.SendToAllAsync(new GameMessage
                {
                    Type = "player_attack",
                    Data = new { PlayerName = pl.Name, Hand = "main" }
                });

                var rng = new Random();
                double effDefense = _svc.Monsters.GetEffectiveDefense(monster);
                double effAttack = _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
                bool evaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
                bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
                bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
                int hitDmg = 0;
                bool hitCrit = false;

                if (!evaded && !parried)
                {
                    hitCrit = rng.Next(Balance.ChanceRollMax) < pl.GetCritChance();
                    int baseDmg = Math.Max(Balance.MinDamage, (int)(effAttack - effDefense));
                    hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * queuedSkill.DamageMultiplier);
                    if (hitCrit)
                        hitDmg = (int)(hitDmg * (pl.GetCritDamage() + 0.2));
                    hitDmg = _svc.Monsters.ApplyDmgReduction(pl, hitDmg);
                    if (blocked)
                        hitDmg = Math.Max(Balance.MinDamage, hitDmg - monster.GetBlockValue());
                    monster.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, true, client);
                    monster.LastDamagedTime = DateTime.UtcNow;
                    monster.DamageTracker[pl.Id] = monster.DamageTracker.GetValueOrDefault(pl.Id) + hitDmg;
                }

                if (evaded)
                {
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"{monster.Name} уклонился от первого удара «{queuedSkill.Name}».");
                }
                else if (parried)
                {
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"{monster.Name} парировал первый удар «{queuedSkill.Name}»!");
                }
                else
                {
                    string critText = hitCrit ? " (КРИТ!)" : "";
                    string blockText = blocked ? " (блок)" : "";
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"«{queuedSkill.Name}» — первый удар: {hitDmg} урона{critText}{blockText} {monster.Name}.");
                    var dmgMsg = new GameMessage
                    {
                        Type = "damage",
                        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = hitDmg, IsCrit = hitCrit, Hand = "main" }
                    };
                    await _svc.Hub.SendToClient(client, dmgMsg);
                    await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
                }

                pl.Combat.LastAttackTime = DateTime.UtcNow;

                if (monster.Health <= 0)
                {
                    var killDmgMsg = !evaded ? new GameMessage
                    {
                        Type = "damage",
                        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + hitDmg), IsCrit = hitCrit, Hand = "main" }
                    } : null;
                    await _svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killDmgMsg);
                    return;
                }

                await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));

                // Ставим отложенный второй удар
                pl.Combat.PendingSkillHitsRemaining = 1;
                pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
                pl.Combat.PendingSkillTargetId = monster.Id;
                pl.Combat.PendingSkillId = queuedSkill.Id;
                return;
            }
            else if (queuedSkill.Id == "SK0007")
            {
                pl.Mana = Math.Max(0, pl.Mana - queuedSkill.MpCost);
                pl.LastSkillUse[queuedSkill.Id] = DateTime.UtcNow;
                pl.QueuedSkillIds.RemoveAt(0);
                await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
                await _svc.Hub.SendToClient(client, new GameMessage
                {
                    Type = "skill_cooldown",
                    Data = new { SkillId = queuedSkill.Id, RemainingMs = queuedSkill.CooldownMs, TotalMs = queuedSkill.CooldownMs }
                });

                // Первый удар — правая рука
                await _svc.Hub.SendToAllAsync(new GameMessage
                {
                    Type = "player_attack",
                    Data = new { PlayerName = pl.Name, Hand = "main" }
                });

                var rng = new Random();
                double effDefense = _svc.Monsters.GetEffectiveDefense(monster);
                double effAttack = _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
                bool evaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
                bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
                bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
                int hitDmg = 0;
                bool hitCrit = false;

                if (!evaded && !parried)
                {
                    hitCrit = rng.Next(Balance.ChanceRollMax) < pl.GetCritChance();
                    int baseDmg = Math.Max(Balance.MinDamage, (int)(effAttack - effDefense));
                    hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * queuedSkill.DamageMultiplier);
                    if (hitCrit)
                        hitDmg = (int)(hitDmg * (pl.GetCritDamage() + 0.2));
                    hitDmg = _svc.Monsters.ApplyDmgReduction(pl, hitDmg);
                    if (blocked)
                        hitDmg = Math.Max(Balance.MinDamage, hitDmg - monster.GetBlockValue());
                    monster.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, true, client);
                    monster.LastDamagedTime = DateTime.UtcNow;
                    monster.DamageTracker[pl.Id] = monster.DamageTracker.GetValueOrDefault(pl.Id) + hitDmg;
                }

                if (evaded)
                {
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"{monster.Name} уклонился от первого удара «{queuedSkill.Name}».");
                }
                else if (parried)
                {
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"{monster.Name} парировал первый удар «{queuedSkill.Name}»!");
                }
                else
                {
                    string critText = hitCrit ? " (КРИТ!)" : "";
                    string blockText = blocked ? " (блок)" : "";
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"«{queuedSkill.Name}» — первый удар: {hitDmg} урона{critText}{blockText} {monster.Name}.");
                    var dmgMsg = new GameMessage
                    {
                        Type = "damage",
                        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = hitDmg, IsCrit = hitCrit, Hand = "main" }
                    };
                    await _svc.Hub.SendToClient(client, dmgMsg);
                    await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);

                    if (rng.Next(Balance.ChanceRollMax) < Balance.HolyTrinityDebuffChance)
                    {
                        await ApplyHolyTrinityDebuff(pl, monster, client, rng);
                    }
                }

                pl.Combat.LastAttackTime = DateTime.UtcNow;

                if (monster.Health <= 0)
                {
                    var killDmgMsg = !evaded ? new GameMessage
                    {
                        Type = "damage",
                        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + hitDmg), IsCrit = hitCrit, Hand = "main" }
                    } : null;
                    await _svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killDmgMsg);
                    return;
                }

                await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));

                // Ставим отложенные удары 2 и 3
                pl.Combat.PendingSkillHitsRemaining = 2;
                pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
                pl.Combat.PendingSkillTargetId = monster.Id;
                pl.Combat.PendingSkillId = queuedSkill.Id;
                return;
            }
            else if (queuedSkill.Id == "SK0009")
            {
                pl.Mana = Math.Max(0, pl.Mana - queuedSkill.MpCost);
                pl.LastSkillUse[queuedSkill.Id] = DateTime.UtcNow;
                pl.QueuedSkillIds.RemoveAt(0);
                await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
                await _svc.Hub.SendToClient(client, new GameMessage
                {
                    Type = "skill_cooldown",
                    Data = new { SkillId = queuedSkill.Id, RemainingMs = queuedSkill.CooldownMs, TotalMs = queuedSkill.CooldownMs }
                });

                // Первый удар — правая рука
                await _svc.Hub.SendToAllAsync(new GameMessage
                {
                    Type = "player_attack",
                    Data = new { PlayerName = pl.Name, Hand = "main" }
                });

                var rng = new Random();
                double effDefense = _svc.Monsters.GetEffectiveDefense(monster);
                double effAttack = _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
                bool evaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
                bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
                bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
                int hitDmg = 0;
                bool hitCrit = false;

                if (!evaded && !parried)
                {
                    hitCrit = rng.Next(Balance.ChanceRollMax) < pl.GetCritChance();
                    int baseDmg = Math.Max(Balance.MinDamage, (int)(effAttack - effDefense));
                    hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * queuedSkill.DamageMultiplier);
                    if (hitCrit)
                        hitDmg = (int)(hitDmg * (pl.GetCritDamage() + 0.2));
                    hitDmg = _svc.Monsters.ApplyDmgReduction(pl, hitDmg);
                    if (blocked)
                        hitDmg = Math.Max(Balance.MinDamage, hitDmg - monster.GetBlockValue());
                    monster.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, true, client);
                    monster.LastDamagedTime = DateTime.UtcNow;
                    monster.DamageTracker[pl.Id] = monster.DamageTracker.GetValueOrDefault(pl.Id) + hitDmg;
                }

                if (evaded)
                {
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"{monster.Name} уклонился от первого удара «{queuedSkill.Name}».");
                }
                else if (parried)
                {
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"{monster.Name} парировал первый удар «{queuedSkill.Name}»!");
                }
                else
                {
                    string critText = hitCrit ? " (КРИТ!)" : "";
                    string blockText = blocked ? " (блок)" : "";
                    await ChatTo(client, ChatChannel.Combat, "Бой",
                        $"«{queuedSkill.Name}» — первый удар: {hitDmg} урона{critText}{blockText} {monster.Name}.");
                    var dmgMsg = new GameMessage
                    {
                        Type = "damage",
                        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = hitDmg, IsCrit = hitCrit, Hand = "main" }
                    };
                    await _svc.Hub.SendToClient(client, dmgMsg);
                    await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
                }

                pl.Combat.LastAttackTime = DateTime.UtcNow;

                if (monster.Health <= 0)
                {
                    var killDmgMsg = !evaded ? new GameMessage
                    {
                        Type = "damage",
                        Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + hitDmg), IsCrit = hitCrit, Hand = "main" }
                    } : null;
                    await _svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killDmgMsg);
                    return;
                }

                await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));

                // Ставим отложенные удары 2..6
                pl.Combat.PendingSkillHitsRemaining = Balance.DuelHitCount - 1;
                pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
                pl.Combat.PendingSkillTargetId = monster.Id;
                pl.Combat.PendingSkillId = queuedSkill.Id;
                return;
            }
            else
            {
                int baseDamage = (int)Math.Max(Balance.MinDamage,
                    _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage()) - _svc.Monsters.GetEffectiveDefense(monster));
                int skillDamage = (int)Math.Max(Balance.MinDamage, baseDamage * queuedSkill.DamageMultiplier);
                dmgToMonster = _svc.Monsters.ApplyDmgReduction(pl, skillDamage);
                pl.Mana = Math.Max(0, pl.Mana - queuedSkill.MpCost);
                pl.LastSkillUse[queuedSkill.Id] = DateTime.UtcNow;
                pl.QueuedSkillIds.RemoveAt(0);
                await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
                await _svc.Hub.SendToClient(client, new GameMessage
                {
                    Type = "skill_cooldown",
                    Data = new { SkillId = queuedSkill.Id, RemainingMs = queuedSkill.CooldownMs, TotalMs = queuedSkill.CooldownMs }
                });
                await ChatTo(client, ChatChannel.Combat, "Бой", $"Применён навык «{queuedSkill.Name}»! Урон x{queuedSkill.DamageMultiplier}.");
            }
        }

        if (queuedSkill?.Id == "SK0001" && dmgToMonster > 0)
        {
            var rngStun = new Random();
            if (rngStun.Next(Balance.ChanceRollMax) < Balance.StunChanceOnHit)
            {
                var stun = ActiveDebuff.Create(DebuffType.Stun, 0,
                    Balance.StunDurationMs, "skill", "Оглушение",
                    $"Оглушение на {Balance.StunDurationMs / 1000} сек.");
                _svc.Debuffs.ApplyDebuff(monster, stun);
                await ChatTo(client, ChatChannel.Combat, "Бой",
                    $"«Крепкая рука» оглушил {monster.Name} на {Balance.StunDurationMs / 1000} сек.!");
                await SendTargetDebuffUpdateAsync(monster);
            }
        }

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = attackHand }
        });

        if (weaponRange > 1 && !isEvaded)
        {
            // Face the target before ranged attack
            if (Math.Abs(dx) >= Math.Abs(dy))
                pl.Facing = dx > 0 ? "right" : "left";
            else
                pl.Facing = dy > 0 ? "down" : "up";

            string visualType = subtype == "bow" ? "arrow" : "magic_bolt";
            var proj = _svc.Projectiles.Spawn(pl, monster, visualType, dmgToMonster, isCrit, attackHand);
            await _svc.Projectiles.BroadcastSpawn(proj);

            // Broadcast map to sync facing
            await _svc.Hub.BroadcastMapAsync();
            
            return;
        }

        if (monsterDead && monster.IsMannequin)
        {
            monster.Health = monster.MaxHealth;
            monster.LastDamagedTime = DateTime.UtcNow;
            await ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{(isCrit ? " (КРИТ!)" : "")}");
            await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
            return;
        }

        if (monsterDead)
        {
            var killDmgMsg = !isEvaded ? new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + dmgToMonster), IsCrit = isCrit, Hand = attackHand }
            } : null;
            await _svc.KillService.ResolveMonsterKill(pl, monster, dmgToMonster, !isEvaded, killDmgMsg);
            return;
        }

        if (isEvaded)
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} уклонился от вашей атаки.");
        else if (isParried)
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} парировал вашу атаку!");
        else if (isBlocked)
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} заблокировал часть урона щитом!");
        else
        {
            string critText = isCrit ? " (КРИТ!)" : "";
            await ChatTo(client, ChatChannel.Combat, "Бой", $"Вы нанесли {dmgToMonster} урона{critText} {monster.Name}.");
        }

        if (!isEvaded)
        {
            var dmgMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = dmgToMonster, IsCrit = isCrit, Hand = attackHand }
            };
            await _svc.Hub.SendToClient(client, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
        }

        await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
        await _svc.Hub.SendToClient(client, new GameMessage
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
            await _svc.Hub.SendToClient(client, hitMsg);
            await _svc.Hub.SendDamageNearbyAsync(pl.X, pl.Y, hitMsg, pl);
            await ChatTo(client, ChatChannel.Combat, "Бой",
                $"{monster.Name} нанёс вам {dmgToPlayer} урона. ({pl.Health}/{pl.MaxHealth + pl.Equipment.GetBonusMaxHealth()}) HP");
            await _svc.Party.SendUpdateForAsync(pl);
        }

        if (pl.Health <= 0)
        {
            pl.Combat.Cancel();
            pl.Combat.OffHandLastAttackTime = DateTime.MinValue;
            await HandlePlayerDeath(pl, client);
        }
    }

    // ──────────────── Комбо-удар (продолжение навыка) ────────────────

    private async Task ExecuteComboHit(Player pl, Monster monster, ClientConnection client)
    {
        pl.Combat.PendingSkillHitsRemaining--;
        bool moreHits = pl.Combat.PendingSkillHitsRemaining > 0;
        if (!moreHits)
            pl.Combat.PendingSkillTargetId = null;

        if (monster.Health <= 0) return;

        string skillId = pl.Combat.PendingSkillId ?? "";
        bool isHolyTrinity = skillId == "SK0007";
        bool isDuel = skillId == "SK0009";
        string hitHand = (isHolyTrinity || isDuel) ? "main" : "off";

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = hitHand }
        });

        var rng = new Random();
        double effDefense = _svc.Monsters.GetEffectiveDefense(monster);
        bool evaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
        bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
        bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
        int hitDmg = 0;
        bool hitCrit = false;

        if (!evaded && !parried)
        {
            double effAttack;
            double dmgMult;
            if (isHolyTrinity)
            {
                effAttack = _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
                dmgMult = 2.0;
            }
            else if (isDuel)
            {
                int hitNumber = Balance.DuelHitCount - pl.Combat.PendingSkillHitsRemaining;
                effAttack = _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
                dmgMult = Balance.DuelFirstHitMult + (hitNumber - 1) * Balance.DuelPerHitBonus;
            }
            else
            {
                effAttack = _svc.Monsters.GetEffectiveAttack(pl, pl.RollOffHandDamage());
                dmgMult = 1.5;
            }

            hitCrit = rng.Next(Balance.ChanceRollMax) < pl.GetCritChance();
            int baseDmg = Math.Max(Balance.MinDamage, (int)(effAttack - effDefense));
            hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * dmgMult);
            if (hitCrit)
                hitDmg = (int)(hitDmg * (pl.GetCritDamage() + 0.2));

            if (!isHolyTrinity && !isDuel)
            {
                double offHandFraction = pl.LearnedSkills.Contains("SK0003")
                    ? 0.75
                    : Equipment.OffHandDamageFraction;
                hitDmg = Math.Max(Balance.MinDamage, (int)(hitDmg * offHandFraction));
            }

            hitDmg = _svc.Monsters.ApplyDmgReduction(pl, hitDmg);
            if (blocked)
                hitDmg = Math.Max(Balance.MinDamage, hitDmg - monster.GetBlockValue());
            monster.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, true, client);
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker[pl.Id] = monster.DamageTracker.GetValueOrDefault(pl.Id) + hitDmg;
        }

        string skillName = isHolyTrinity ? "Святая троица" : isDuel ? "ЭТО ДУЭЛЬ!" : "Разрез";
        if (evaded)
        {
            await ChatTo(client, ChatChannel.Combat, "Бой",
                $"{monster.Name} уклонился от удара «{skillName}».");
        }
        else if (parried)
        {
            await ChatTo(client, ChatChannel.Combat, "Бой",
                $"{monster.Name} парировал удар «{skillName}»!");
        }
        else if (blocked)
        {
            string critText = hitCrit ? " (КРИТ!)" : "";
            string handLabel = isHolyTrinity ? "" : " — левая рука";
            await ChatTo(client, ChatChannel.Combat, "Бой",
                $"«{skillName}»{handLabel}: {hitDmg} урона{critText} {monster.Name} (блок).");
            var dmgMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = hitDmg, IsCrit = hitCrit, Hand = hitHand }
            };
            await _svc.Hub.SendToClient(client, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
        }
        else
        {
            string critText = hitCrit ? " (КРИТ!)" : "";
            string handLabel = isHolyTrinity ? "" : " — левая рука";
            await ChatTo(client, ChatChannel.Combat, "Бой",
                $"«{skillName}»{handLabel}: {hitDmg} урона{critText} {monster.Name}.");
            var dmgMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = hitDmg, IsCrit = hitCrit, Hand = hitHand }
            };
            await _svc.Hub.SendToClient(client, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);

            if (isHolyTrinity && rng.Next(Balance.ChanceRollMax) < Balance.HolyTrinityDebuffChance)
            {
                await ApplyHolyTrinityDebuff(pl, monster, client, rng);
            }
        }

        pl.Combat.LastAttackTime = DateTime.UtcNow;

        if (monster.Health <= 0)
        {
            var killDmgMsg = !evaded ? new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + hitDmg), IsCrit = hitCrit, Hand = hitHand }
            } : null;
            await _svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killDmgMsg);
            return;
        }

        if (moreHits)
        {
            pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
        }

        await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
        await _svc.Hub.SendToClient(client, new GameMessage
        {
            Type = "combat_state",
            Data = new { InCombat = true, TargetId = monster.Id.ToString(), TargetName = monster.Name, TargetHp = monster.Health, TargetMaxHp = monster.MaxHealth }
        });
    }

    // «ЭТО ДУЭЛЬ!»: наказание при смене таргета монстром (PvE)
    private async Task ExecuteDuelPunish(Player pl, Monster monster, ClientConnection client)
    {
        int remainingHits = pl.Combat.PendingSkillHitsRemaining;
        pl.Combat.PendingSkillHitsRemaining = 0;
        pl.Combat.PendingSkillId = null;
        pl.Combat.PendingSkillTargetId = null;

        if (monster.Health <= 0) return;

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "main" }
        });

        var rng = new Random();
        double effDefense = _svc.Monsters.GetEffectiveDefense(monster);
        double effAttack = _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
        bool evaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
        bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
        bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
        int hitDmg = 0;

        if (!evaded && !parried)
        {
            int baseDmg = Math.Max(Balance.MinDamage, (int)(effAttack - effDefense));
            double mult = Balance.DuelPunishBaseMult + remainingHits * Balance.DuelPunishPerMissMult;
            hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * mult);
            hitDmg = _svc.Monsters.ApplyDmgReduction(pl, hitDmg);
            if (blocked)
                hitDmg = Math.Max(Balance.MinDamage, hitDmg - monster.GetBlockValue());
            monster.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, true, client);
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker[pl.Id] = monster.DamageTracker.GetValueOrDefault(pl.Id) + hitDmg;
        }

        if (evaded)
            await ChatTo(client, ChatChannel.Combat, "Бой",
                $"{monster.Name} уклонился от наказания «{pl.Name}»!");
        else if (parried)
            await ChatTo(client, ChatChannel.Combat, "Бой",
                $"{monster.Name} парировал наказание «{pl.Name}»!");
        else
        {
            string blockText = blocked ? " (блок)" : "";
            await ChatTo(client, ChatChannel.Combat, "Бой",
                $"«ЭТО ДУЭЛЬ!» — наказание за смену таргета: {hitDmg} урона{blockText} {monster.Name}!");
            var dmgMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = hitDmg, IsCrit = false, Hand = "main" }
            };
            await _svc.Hub.SendToClient(client, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
        }

        // Оглушение с 100% шансом
        if (!evaded && !parried)
        {
            var stun = ActiveDebuff.Create(DebuffType.Stun, 0,
                Balance.DuelStunMs, "skill", "Оглушение (Дуэль)",
                $"Оглушение на {Balance.DuelStunMs / 1000} сек. — наказание за смену таргета.");
            _svc.Debuffs.ApplyDebuff(monster, stun);
        }

        if (monster.Health <= 0)
        {
            var killDmgMsg = !evaded ? new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + hitDmg), IsCrit = false, Hand = "main" }
            } : null;
            await _svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killDmgMsg);
            return;
        }

        await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
    }

    private async Task HandlePvPDeath(Player killer, Player victim, ClientConnection? killerClient)
    {
        victim.Combat.Cancel();
        victim.Interaction.Clear();
        victim.Movement.Stop();
        victim.IsDead = true;
        victim.DeathTime = DateTime.UtcNow;

        int lostGold = Balance.ComputeDeathGoldLoss(victim.Gold);
        victim.Gold -= lostGold;

        var victimClient = _svc.World.FindClientByPlayer(victim);
        if (victimClient != null)
        {
            await _svc.Hub.SendToClient(victimClient, GameMessage.ResetCombat());
            await _svc.Hub.SendToClient(victimClient, GameMessage.PlayerDeath(lostGold));
            await ChatTo(victimClient, ChatChannel.System, "Система",
                $"Вы погибли в PvP от {killer.Name}! Возрождение через 5 сек...");
        }

        Log.Info($"{killer.Name} убил {victim.Name} в PvP!");
        if (killerClient != null)
            await ChatTo(killerClient, ChatChannel.System, "Система", $"Вы победили {victim.Name} в PvP!");
    }

    // «Кровопускание» (SK0010): лечение части урона, нанесённого мечом (ближний бой, не кастер/лук).
    private async Task TryLifesteal(Player pl, int dealt, bool isMelee, ClientConnection? client)
    {
        if (dealt <= 0) return;
        if (!pl.LearnedSkills.Contains("SK0010")) return;
        if (!isMelee || !IsWieldingMelee(pl)) return;

        int heal = (int)(dealt * Balance.LifestealFraction);
        if (heal <= 0) return;
        int maxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth();
        if (pl.Health >= maxHp) return;

        pl.Health = Math.Min(maxHp, pl.Health + heal);
        if (client != null)
        {
            await _svc.Hub.SendStatusAsync(client, pl);
            await ChatTo(client, ChatChannel.Combat, "Бой", $"«Кровопускание»: +{heal} HP.");
        }
    }

    private static bool IsWieldingMelee(Player pl)
    {
        var w = pl.Equipment.GetEffectiveMainHandWeapon() ?? pl.Equipment.GetOffHandWeapon();
        if (w == null) return false;
        string sub = w.WeaponSubtype ?? "";
        return sub != "staff" && sub != "bow" && sub != "grimoire" && sub != "sphere";
    }

    // «ЭТО ДУЭЛЬ!»: первый удар по PvP-цели
    private async Task ExecutePvPFirstHit(Player pl, Player target, ClientConnection atkClient, Skill skill, int weaponRange)
    {
        int dist = Math.Abs(pl.X - target.X) + Math.Abs(pl.Y - target.Y);
        pl.Mana = Math.Max(0, pl.Mana - skill.MpCost);
        pl.LastSkillUse[skill.Id] = DateTime.UtcNow;
        pl.QueuedSkillIds.RemoveAt(0);
        await MessageHandlers.UseSkillHandler.SendSkillQueue(atkClient, pl);
        await _svc.Hub.SendToClient(atkClient, new GameMessage
        {
            Type = "skill_cooldown",
            Data = new { SkillId = skill.Id, RemainingMs = skill.CooldownMs, TotalMs = skill.CooldownMs }
        });

        pl.Combat.LastAttackTime = DateTime.UtcNow;

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "main" }
        });

        bool isEvaded = Random.Shared.NextDouble() * 100 < target.GetEvadeChance();
        bool isParried = !isEvaded && dist <= 1 && Random.Shared.NextDouble() * 100 < target.GetParryChance();
        bool isBlocked = !isEvaded && !isParried && Random.Shared.NextDouble() * 100 < target.GetBlockChance();

        int hitDmg = 0;
        bool hitCrit = false;
        string critText = "";
        if (!isEvaded && !isParried)
        {
            int rawDmg = Math.Max(Balance.MinDamage, pl.GetTotalAttack(dist) - target.GetTotalDefense());
            hitDmg = (int)Math.Max(Balance.MinDamage, rawDmg * skill.DamageMultiplier);
            hitCrit = Random.Shared.NextDouble() * 100 < pl.GetCritChance();
            if (hitCrit) hitDmg = (int)(hitDmg * pl.GetCritDamage());
            if (isBlocked) hitDmg = Math.Max(Balance.MinDamage, hitDmg - target.GetBlockValue());
            target.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;
        }

        if (isEvaded)
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} уклонился от первого удара «{skill.Name}».");
        else if (isParried)
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} парировал первый удар «{skill.Name}»!");
        else
        {
            critText = hitCrit ? " (КРИТ!)" : "";
            string blockText = isBlocked ? " (блок)" : "";
            await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                $"«{skill.Name}» — первый удар: {hitDmg} урона{critText}{blockText} {target.Name}.");
            var dmgMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit }
            };
            await _svc.Hub.SendToClient(atkClient, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, dmgMsg, target);
        }

        var targetClient = _svc.World.FindClientByPlayer(target);
        if (targetClient != null)
        {
            var hitMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit }
            };
            await _svc.Hub.SendToClient(targetClient, hitMsg);
            await ChatTo(targetClient, ChatChannel.Combat, "Бой",
                $"{pl.Name} применил «{skill.Name}»: {hitDmg} урона{critText} вам.");
            await _svc.Hub.SendStatusAsync(targetClient, target);
        }

        if (target.Health <= 0)
        {
            await HandlePvPDeath(pl, target, atkClient);
            return;
        }

        // «ЭТО ДУЭЛЬ!»: наказание «армируется» только если цель САМА атаковала игрока.
        // Игроки сами решают, кого атаковать — принудительно таргет не ставим.
        pl.Combat.DuelPunishArmed = target.Combat.TargetPlayerId == pl.Id;

        await _svc.Hub.SendStatusAsync(atkClient, pl);
        if (targetClient != null)
            await _svc.Hub.SendToClient(targetClient, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true }
            });

        // Отложенные удары 2..6
        pl.Combat.PendingSkillHitsRemaining = Balance.DuelHitCount - 1;
        pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
        pl.Combat.PendingSkillTargetId = target.Id;
        pl.Combat.PendingSkillId = skill.Id;
    }

    // «ЭТО ДУЭЛЬ!»: продолжение серии по PvP-цели
    private async Task ExecuteComboHitPvP(Player pl, Player target, ClientConnection atkClient)
    {
        pl.Combat.PendingSkillHitsRemaining--;
        bool moreHits = pl.Combat.PendingSkillHitsRemaining > 0;
        if (!moreHits) pl.Combat.PendingSkillTargetId = null;

        if (target.Health <= 0) return;

        int dist = Math.Abs(pl.X - target.X) + Math.Abs(pl.Y - target.Y);
        int hitNumber = Balance.DuelHitCount - pl.Combat.PendingSkillHitsRemaining;
        double mult = Balance.DuelFirstHitMult + (hitNumber - 1) * Balance.DuelPerHitBonus;

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "main" }
        });

        bool isEvaded = Random.Shared.NextDouble() * 100 < target.GetEvadeChance();
        bool isParried = !isEvaded && dist <= 1 && Random.Shared.NextDouble() * 100 < target.GetParryChance();
        bool isBlocked = !isEvaded && !isParried && Random.Shared.NextDouble() * 100 < target.GetBlockChance();

        int hitDmg = 0;
        bool hitCrit = false;
        string critText = "";
        if (!isEvaded && !isParried)
        {
            int rawDmg = Math.Max(Balance.MinDamage, pl.GetTotalAttack(dist) - target.GetTotalDefense());
            hitDmg = (int)Math.Max(Balance.MinDamage, rawDmg * mult);
            hitCrit = Random.Shared.NextDouble() * 100 < pl.GetCritChance();
            if (hitCrit) hitDmg = (int)(hitDmg * pl.GetCritDamage());
            if (isBlocked) hitDmg = Math.Max(Balance.MinDamage, hitDmg - target.GetBlockValue());
            target.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;
        }

        if (isEvaded)
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} уклонился от удара «ЭТО ДУЭЛЬ!».");
        else if (isParried)
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} парировал удар «ЭТО ДУЭЛЬ!»!");
        else
        {
            critText = hitCrit ? " (КРИТ!)" : "";
            string blockText = isBlocked ? " (блок)" : "";
            await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                $"«ЭТО ДУЭЛЬ!»: {hitDmg} урона{critText}{blockText} {target.Name}.");
            var dmgMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit }
            };
            await _svc.Hub.SendToClient(atkClient, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, dmgMsg, target);
        }

        var targetClient = _svc.World.FindClientByPlayer(target);
        if (targetClient != null)
        {
            var hitMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = hitCrit }
            };
            await _svc.Hub.SendToClient(targetClient, hitMsg);
            await ChatTo(targetClient, ChatChannel.Combat, "Бой",
                $"{pl.Name} нанёс вам {hitDmg} урона{critText} «ЭТО ДУЭЛЬ!».");
            await _svc.Hub.SendStatusAsync(targetClient, target);
        }

        pl.Combat.LastAttackTime = DateTime.UtcNow;

        if (target.Health <= 0)
        {
            await HandlePvPDeath(pl, target, atkClient);
            return;
        }

        if (moreHits) pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;

        await _svc.Hub.SendStatusAsync(atkClient, pl);
        if (targetClient != null)
            await _svc.Hub.SendToClient(targetClient, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true }
            });
    }

    // «ЭТО ДУЭЛЬ!»: наказание при смене таргета PvP-целью
    private async Task ExecuteDuelPunishPvP(Player pl, Player target, ClientConnection atkClient)
    {
        int remainingHits = pl.Combat.PendingSkillHitsRemaining;
        pl.Combat.PendingSkillHitsRemaining = 0;
        pl.Combat.PendingSkillId = null;
        pl.Combat.PendingSkillTargetId = null;
        pl.Combat.DuelPunishArmed = false;

        if (target.Health <= 0) return;

        int dist = Math.Abs(pl.X - target.X) + Math.Abs(pl.Y - target.Y);

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "main" }
        });

        bool isEvaded = Random.Shared.NextDouble() * 100 < target.GetEvadeChance();
        bool isParried = !isEvaded && dist <= 1 && Random.Shared.NextDouble() * 100 < target.GetParryChance();
        bool isBlocked = !isEvaded && !isParried && Random.Shared.NextDouble() * 100 < target.GetBlockChance();

        int hitDmg = 0;
        if (!isEvaded && !isParried)
        {
            int rawDmg = Math.Max(Balance.MinDamage, pl.GetTotalAttack(dist) - target.GetTotalDefense());
            double mult = Balance.DuelPunishBaseMult + remainingHits * Balance.DuelPunishPerMissMult;
            hitDmg = (int)Math.Max(Balance.MinDamage, rawDmg * mult);
            if (isBlocked) hitDmg = Math.Max(Balance.MinDamage, hitDmg - target.GetBlockValue());
            target.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;
        }

        if (isEvaded)
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} уклонился от наказания «ЭТО ДУЭЛЬ!»!");
        else if (isParried)
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} парировал наказание «ЭТО ДУЭЛЬ!»!");
        else
        {
            string blockText = isBlocked ? " (блок)" : "";
            await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                $"«ЭТО ДУЭЛЬ!» — наказание за смену таргета: {hitDmg} урона{blockText} {target.Name}!");
            var dmgMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = false }
            };
            await _svc.Hub.SendToClient(atkClient, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, dmgMsg, target);
        }

        var targetClient = _svc.World.FindClientByPlayer(target);
        if (targetClient != null)
        {
            var hitMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = hitDmg, IsCrit = false }
            };
            await _svc.Hub.SendToClient(targetClient, hitMsg);
            await ChatTo(targetClient, ChatChannel.Combat, "Бой",
                $"{pl.Name} наказал вас сменой таргета «ЭТО ДУЭЛЬ!»: {hitDmg} урона.");
            await _svc.Hub.SendStatusAsync(targetClient, target);
        }

        // Оглушение с 100% шансом
        if (!isEvaded && !isParried)
        {
            var stun = ActiveDebuff.Create(DebuffType.Stun, 0,
                Balance.DuelStunMs, "skill", "Оглушение (Дуэль)",
                $"Оглушение на {Balance.DuelStunMs / 1000} сек. — наказание за смену таргета.");
            _svc.Debuffs.ApplyDebuff(target, stun);
        }

        if (target.Health <= 0)
        {
            await HandlePvPDeath(pl, target, atkClient);
            return;
        }

        await _svc.Hub.SendStatusAsync(atkClient, pl);
        if (targetClient != null)
            await _svc.Hub.SendToClient(targetClient, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true }
            });
    }

    private async Task ApplyHolyTrinityDebuff(Player pl, Monster monster, ClientConnection client, Random rng)
    {
        int roll = rng.Next(3);
        ActiveDebuff debuff;
        switch (roll)
        {
            case 0:
                debuff = ActiveDebuff.Create(DebuffType.Root, 0,
                    Balance.RootDurationMs, "skill", "Обездвижен",
                    $"Обездвиживает на {Balance.RootDurationMs / 1000} сек.");
                break;
            case 1:
                debuff = ActiveDebuff.Create(DebuffType.DamageReduction, Balance.MaceDamageReductionValue,
                    Balance.MaceDisarmDurationMs, "skill", "Обезоружен",
                    $"Снижает урон цели на {(int)(Balance.MaceDamageReductionValue * 100)}%");
                break;
            default:
                debuff = ActiveDebuff.Create(DebuffType.AccuracyReduction, Balance.HammerAccuracyReductionValue,
                    Balance.HammerStunDurationMs, "skill", "Контузия",
                    $"Снижает точность цели на {(int)(Balance.HammerAccuracyReductionValue * 100)}%");
                break;
        }
        _svc.Debuffs.ApplyDebuff(monster, debuff);
        await ChatTo(client, ChatChannel.Combat, "Бой",
            $"«Святая троица» наложила «{debuff.DisplayName}» на {monster.Name}!");
        await SendTargetDebuffUpdateAsync(monster);
    }

    public async Task ExecuteOffHandAttack(Player pl, ClientConnection client)
    {
        var offMonster = _svc.Monsters.FindMonsterById(pl.Combat.TargetMonsterId!.Value);
        if (offMonster == null || offMonster.Health <= 0)
        {
            if (offMonster != null && offMonster.IsMannequin && offMonster.Health <= 0)
            {
                offMonster.Health = offMonster.MaxHealth;
                offMonster.LastDamagedTime = DateTime.UtcNow;
            }
            pl.Combat.OffHandLastAttackTime = DateTime.MinValue;
            return;
        }

        pl.Combat.OffHandLastAttackTime = DateTime.UtcNow;

        var offWeapon = pl.Equipment.GetOffHandWeapon();
        string offSubtype = offWeapon?.WeaponSubtype ?? "";
        int offWeaponRange = offWeapon?.AttackRange ?? 1;

        int dx = offMonster.X - pl.X;
        int dy = offMonster.Y - pl.Y;
        int dist = Math.Abs(dx) + Math.Abs(dy);
        bool isRangedAttack = offWeaponRange > 1 && dist > 1;

        // Face the target before off-hand attack
        if (Math.Abs(dx) >= Math.Abs(dy))
            pl.Facing = dx > 0 ? "right" : "left";
        else
            pl.Facing = dy > 0 ? "down" : "up";

        if (isRangedAttack)
        {
            var (ohDmg, ohCrit, ohEvaded, ohParried, ohBlocked) = _svc.Monsters.CalculateOffHandAttack(pl, offMonster);

            string visualType = offSubtype == "bow" ? "arrow" : "magic_bolt";
            var proj = _svc.Projectiles.Spawn(pl, offMonster, visualType, ohDmg, ohCrit, "off");
            await _svc.Projectiles.BroadcastSpawn(proj);
            await _svc.Hub.BroadcastMapAsync();
            return;
        }

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "off" }
        });

        var (meleeDmg, meleeCrit, meleeEvaded, meleeParried, meleeBlocked) = _svc.Monsters.CalculateOffHandAttack(pl, offMonster);

        if (meleeEvaded)
        {
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} уклонился от удара вторым оружием.");
            return;
        }
        if (meleeParried)
        {
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} парировал удар вторым оружием!");
            return;
        }
        if (meleeBlocked)
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} заблокировал часть урона щитом!");
        else
        {
            string ohCritText = meleeCrit ? " (КРИТ!)" : "";
            await ChatTo(client, ChatChannel.Combat, "Бой", $"Вы нанесли {meleeDmg} урона{ohCritText} {offMonster.Name}.");
        }

        if (meleeDmg <= 0) return;

        await TryLifesteal(pl, meleeDmg, true, client);

        offMonster.Health -= meleeDmg;
        offMonster.LastDamagedTime = DateTime.UtcNow;
        offMonster.DamageTracker[pl.Id] = offMonster.DamageTracker.GetValueOrDefault(pl.Id) + meleeDmg;

        string critText = meleeCrit ? " (КРИТ!)" : "";
        string ohWeaponName = pl.Equipment.GetOffHandWeapon()?.Name ?? "оружие";
        await ChatTo(client, ChatChannel.Combat, "Бой", $"Второе оружие ({ohWeaponName}) нанесло {meleeDmg} урона{critText} {offMonster.Name}.");

        var dmgMsg = new GameMessage
        {
            Type = "damage",
            Data = new { Target = "monster", MonsterId = offMonster.Id.ToString(), X = offMonster.X, Y = offMonster.Y, Amount = meleeDmg, IsCrit = meleeCrit, Hand = "off" }
        };
        await _svc.Hub.SendToClient(client, dmgMsg);
        await _svc.Hub.SendDamageNearbyAsync(offMonster.X, offMonster.Y, dmgMsg, pl);

        if (offMonster.Health <= 0)
        {
            if (offMonster.IsMannequin)
            {
                offMonster.Health = offMonster.MaxHealth;
                offMonster.LastDamagedTime = DateTime.UtcNow;
                await ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{(meleeCrit ? " (КРИТ!)" : "")}");
                await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(offMonster.Name, offMonster.Health, offMonster.MaxHealth));
                return;
            }

            var killMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = offMonster.Id.ToString(), X = offMonster.X, Y = offMonster.Y, Amount = Math.Max(0, offMonster.Health + meleeDmg), IsCrit = meleeCrit, Hand = "off" }
            };
            await _svc.KillService.ResolveMonsterKill(pl, offMonster, meleeDmg, true, killMsg);
        }
        else
        {
            await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(offMonster.Name, offMonster.Health, offMonster.MaxHealth));
            await _svc.Hub.SendToClient(client, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = true, TargetId = offMonster.Id.ToString(), TargetName = offMonster.Name, TargetHp = offMonster.Health, TargetMaxHp = offMonster.MaxHealth }
            });
        }
    }

    // ──────────────── Преследование ────────────────

    public bool ChaseTarget(Player pl, Monster monster)
    {
        if (_svc.Debuffs.HasDebuff(pl, DebuffType.Stun)) return false;
        if (_svc.Debuffs.HasDebuff(pl, DebuffType.Root)) return false;
        int moveIntervalMs = Balance.MoveIntervalMs(pl.Speed);
        bool canMove = (DateTime.UtcNow - pl.Movement.LastMoveTime).TotalMilliseconds >= moveIntervalMs;
        if (!canMove) return false;

        int stepX = Math.Sign(monster.X - pl.X);
        int stepY = Math.Sign(monster.Y - pl.Y);

        int mx = 0, my = 0;
        if (stepX != 0 && stepY != 0)
        {
            if (pl.X + stepX >= 0 && pl.X + stepX < _svc.World.Map.Width
                && _svc.Monsters.FindMonsterAt(pl.X + stepX, pl.Y) == null)
                mx = stepX;
            else
                my = stepY;
        }
        else if (stepX != 0) mx = stepX;
        else if (stepY != 0) my = stepY;

        if (mx == 0 && my == 0) return false;

        int nx = pl.X + mx;
        int ny = pl.Y + my;
        if (nx < 0 || nx >= _svc.World.Map.Width || ny < 0 || ny >= _svc.World.Map.Height) return false;
        if (_svc.Monsters.FindMonsterAt(nx, ny) != null) return false;

        if (mx == 1) pl.Facing = "right";
        else if (mx == -1) pl.Facing = "left";
        else if (my == 1) pl.Facing = "down";
        else if (my == -1) pl.Facing = "up";

        pl.X = nx;
        pl.Y = ny;
        pl.Movement.LastMoveTime = DateTime.UtcNow;
        return true;
    }

    // ──────────────── Навыки ────────────────

    private static readonly HashSet<string> InstantBuffSkills = new() { "SK0002" };

    private async Task ProcessInstantBuffs(Player pl, ClientConnection client)
    {
        if (pl.QueuedSkillIds.Count == 0) return;
        var sid = pl.QueuedSkillIds[0];
        if (!InstantBuffSkills.Contains(sid)) return;

        var skill = DatabaseManager.GetSkill(sid);
        if (skill == null) { pl.QueuedSkillIds.RemoveAt(0); return; }

        bool onCd = pl.LastSkillUse.TryGetValue(sid, out var last)
            && (DateTime.UtcNow - last).TotalMilliseconds < skill.CooldownMs;
        if (onCd || pl.Mana < skill.MpCost)
        {
            pl.QueuedSkillIds.RemoveAt(0);
            await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
            return;
        }

        pl.Mana = Math.Max(0, pl.Mana - skill.MpCost);
        pl.LastSkillUse[skill.Id] = DateTime.UtcNow;
        pl.QueuedSkillIds.RemoveAt(0);

        if (skill.Id == "SK0002")
        {
            var buff = ActiveDebuff.Create(DebuffType.AttackSpeedBonus, Balance.AttackSpeedBonusValue,
                Balance.AttackSpeedBonusDurationMs, "skill", "Проворность",
                $"Увеличивает скорость атаки на {(int)(Balance.AttackSpeedBonusValue * 100)}%");
            _svc.Debuffs.ApplyDebuff(pl, buff);
        }

        await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
        await _svc.Hub.SendToClient(client, new GameMessage
        {
            Type = "skill_cooldown",
            Data = new { SkillId = skill.Id, RemainingMs = skill.CooldownMs, TotalMs = skill.CooldownMs }
        });
        await ChatTo(client, ChatChannel.Combat, "Бой", $"Применён навык «{skill.Name}»!");
    }

    public async Task<Skill?> ProcessSkillQueue(Player pl, ClientConnection client)
    {
        if (pl.QueuedSkillIds.Count == 0) return null;

        string sid = pl.QueuedSkillIds[0];
        var cand = DatabaseManager.GetSkill(sid);
        if (cand == null)
        {
            pl.QueuedSkillIds.RemoveAt(0);
            return null;
        }

        bool onCd = pl.LastSkillUse.TryGetValue(sid, out var last)
            && (DateTime.UtcNow - last).TotalMilliseconds < cand.CooldownMs;
        bool noMp = pl.Mana < cand.MpCost;

        if (onCd || noMp)
        {
            pl.QueuedSkillIds.RemoveAt(0);
            await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl);
            await _svc.Hub.SendToClient(client, GameMessage.Chat("Бой", onCd
                ? $"«{cand.Name}» ещё на перезарядке — пропускаем."
                : $"«{cand.Name}»: недостаточно маны ({pl.Mana}/{cand.MpCost}) — пропускаем."));
            return null;
        }

        return cand;
    }

    // ──────────────── Смерть / Возрождение ────────────────

    public async Task HandlePlayerDeath(Player pl, ClientConnection client)
    {
        int lostGold = Balance.ComputeDeathGoldLoss(pl.Gold);
        pl.Gold -= lostGold;
        pl.IsDead = true;
        pl.DeathTime = DateTime.UtcNow;
        Log.Info($"{pl.Name} погиб! Потеряно {lostGold} золота. Таймер 5с.");
        await _svc.Hub.SendToClient(client, GameMessage.ResetCombat());
        await _svc.Hub.SendToClient(client, GameMessage.PlayerDeath(lostGold));
        await ChatTo(client, ChatChannel.System, "Система", $"Вы погибли! Потеряно {lostGold} золота. Возрождение через 5 сек...");
        await _svc.Party.SendUpdateForAsync(pl);
    }

    public async Task RespawnPlayer(Player pl)
    {
        pl.IsDead = false;
        pl.Health = Balance.RespawnHealth(pl.MaxHealth);

        var zone = _svc.Zones.GetZone(pl.CurrentZoneId);
        
        // Если это PvP зона — респавним в безопасной (не-PvP) зоне рядом с торговцем
        int baseX, baseY, mapW, mapH;
        if (zone != null && zone.PvpEnabled)
        {
            // Ищем безопасную зону (не-PvP) с точкой спавна
            var safeZone = _svc.Zones.Zones.Values
                .Where(z => !z.PvpEnabled && (z.SpawnX > 0 || z.SpawnY > 0))
                .OrderBy(z => Math.Abs(z.SpawnX - _svc.Merchant.MerchantX) + Math.Abs(z.SpawnY - _svc.Merchant.MerchantY))
                .FirstOrDefault();
            
            if (safeZone != null)
            {
                baseX = safeZone.SpawnX;
                baseY = safeZone.SpawnY;
                mapW = safeZone.Width;
                mapH = safeZone.Height;
                pl.CurrentZoneId = safeZone.Id;
            }
            else
            {
                // Фоллбек — позиция торговца в текущей зоне
                baseX = _svc.Merchant.MerchantX;
                baseY = _svc.Merchant.MerchantY;
                mapW = zone.Width;
                mapH = zone.Height;
            }
        }
        else
        {
            // Обычная зона — спавн в этой зоне или у торговца
            baseX = zone?.SpawnX ?? _svc.Merchant.MerchantX;
            baseY = zone?.SpawnY ?? _svc.Merchant.MerchantY;
            mapW = zone?.Width ?? _svc.World.Map.Width;
            mapH = zone?.Height ?? _svc.World.Map.Height;
        }

        int sx = baseX + _svc.World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
        int sy = baseY + _svc.World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
        sx = Math.Clamp(sx, 0, mapW - 1);
        sy = Math.Clamp(sy, 0, mapH - 1);
        pl.X = sx;
        pl.Y = sy;

        var client = _svc.World.FindClientByPlayer(pl);
        if (client != null)
        {
            await ChatTo(client, ChatChannel.System, "Система", "Вы возродились!");
            await _svc.Hub.SendToClient(client, GameMessage.SystemChat("Вы возродились!"));
        }
        await _svc.Hub.BroadcastMapAsync();
        await _svc.Party.SendUpdateForAsync(pl);
        if (client != null)
            await _svc.Hub.SendStatusAsync(client, pl);
    }

    // ──────────────── Цикл монстр-атак + вспомогательные ────────────────

    public async Task RunMonsterAttackLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(Balance.LoopMonsterAttackMs);
                var attacks = _svc.Monsters.DrainPendingAttacks();
                foreach (var (monster, player, damage) in attacks)
                {
                    if (player.IsDead) continue;

                    var rng = new Random();
                    bool evaded = rng.Next(Balance.ChanceRollMax) < player.GetEvadeChance();
                    bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < player.GetParryChance();
                    bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < player.GetBlockChance();
                    int finalDmg = blocked ? Math.Max(Balance.MinDamage, damage - player.GetBlockValue()) : damage;
                    if (evaded || parried) finalDmg = 0;

                    player.Health -= finalDmg;
                    player.LastDamagedTime = DateTime.UtcNow;
                    var client = _svc.World.FindClientByPlayer(player);
                    if (client == null) continue;

                    if (evaded)
                    {
                        await ChatTo(client, ChatChannel.Combat, "Бой", $"Вы уклонились от атаки {monster.Name}.");
                    }
                    else if (parried)
                    {
                        await ChatTo(client, ChatChannel.Combat, "Бой", $"Вы парировали атаку {monster.Name}!");
                    }
                    else
                    {
                        var hitMsg = GameMessage.Damage("player", null, player.X, player.Y, finalDmg, false, player.Name);
                        await _svc.Hub.SendToClient(client, hitMsg);
                        await _svc.Hub.SendDamageNearbyAsync(player.X, player.Y, hitMsg, player);

                        if (blocked)
                            await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} нанёс вам {finalDmg} урона (блок!). ({player.Health}/{player.MaxHealth + player.Equipment.GetBonusMaxHealth()}) HP");
                        else
                            await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} нанёс вам {finalDmg} урона. ({player.Health}/{player.MaxHealth + player.Equipment.GetBonusMaxHealth()}) HP");
                    }

                    await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                    await _svc.Party.SendUpdateForAsync(player);

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
                        await _svc.Hub.SendToClient(client, GameMessage.ResetCombat());
                        await _svc.Hub.SendToClient(client, GameMessage.PlayerDeath(lostGold));

                        await _svc.Party.SendUpdateForAsync(player);
                    }

                    await _svc.Hub.SendStatusAsync(client, player);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка боевого цикла монстров", ex);
            }
        }
    }

    public async Task RunDeathTimerLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(500);
                foreach (var pl in _svc.World.GetPlayersSnapshot())
                {
                    if (pl.IsDead && (DateTime.UtcNow - pl.DeathTime).TotalMilliseconds >= Balance.DeathDelayMs)
                    {
                        await RespawnPlayer(pl);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла таймера смерти", ex);
            }
        }
    }

    // ──────────────── Дебаффы ────────────────

    public async Task SendTargetDebuffUpdateAsync(Monster mon)
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
        foreach (var pl in _svc.World.GetPlayersSnapshot())
        {
            if (pl.Combat.HasTarget && pl.Combat.TargetMonsterId == mon.Id)
            {
                var conn = _svc.World.FindClientByPlayer(pl);
                if (conn != null) await _svc.Hub.SendToClient(conn, msg);
            }
        }
    }

    // ──────────────── PvP ────────────────

    private async Task<bool> RunPvPTick(Player pl)
    {
        if (!pl.Combat.IsPvPTarget || pl.Combat.TargetPlayerId == null) return false;

        Player? target = _svc.World.GetPlayersSnapshot()
            .FirstOrDefault(p => p.Id == pl.Combat.TargetPlayerId.Value && p.CurrentZoneId == pl.CurrentZoneId);

        if (target == null || target.IsDead || target.Health <= 0)
        {
            pl.Combat.Cancel();
            var client = _svc.World.FindClientByPlayer(pl);
            if (client != null)
                await _svc.Hub.SendToClient(client, GameMessage.ResetCombat());
            return true;
        }

        int dist = Math.Abs(pl.X - target.X) + Math.Abs(pl.Y - target.Y);
        int weaponRange = pl.Equipment.GetWeaponAttackRange();

        if (dist > weaponRange)
        {
            // Преследование PvP цели
            if (ChasePlayerTarget(pl, target)) return true;
            return false;
        }

        var atkClient = _svc.World.FindClientByPlayer(pl);
        if (atkClient == null) return false;

        // «ЭТО ДУЭЛЬ!»: комбо / наказание в PvP
        if (pl.Combat.PendingSkillHitsRemaining > 0)
        {
            if (pl.Combat.PendingSkillId == "SK0009" && pl.Combat.DuelPunishArmed && target.Combat.TargetPlayerId != pl.Id)
            {
                await ExecuteDuelPunishPvP(pl, target, atkClient);
                return true;
            }
            if (pl.Combat.PendingSkillTargetId != target.Id)
            {
                pl.Combat.PendingSkillHitsRemaining = 0;
                pl.Combat.PendingSkillId = null;
            }
            else
            {
                double elapsed = (DateTime.UtcNow - pl.Combat.PendingSkillLastHitTime).TotalMilliseconds;
                if (elapsed >= Balance.SlashHitIntervalMs)
                    await ExecuteComboHitPvP(pl, target, atkClient);
                else
                    await _svc.Hub.SendStatusAsync(atkClient, pl);
                return true;
            }
        }

        await ProcessInstantBuffs(pl, atkClient);
        var queuedSkill = await ProcessSkillQueue(pl, atkClient);
        if (queuedSkill != null && queuedSkill.Id == "SK0009")
        {
            await ExecutePvPFirstHit(pl, target, atkClient, queuedSkill, weaponRange);
            return true;
        }

        int attackIntervalMs = Balance.AttackIntervalMs(
            Balance.GetAttackSpeed(pl.Agility), pl.Equipment.GetWeaponSpeedModifier());
        double speedBuff = 1.0 + _svc.Debuffs.GetDebuffValue(pl, DebuffType.AttackSpeedBonus);
        attackIntervalMs = (int)(attackIntervalMs / speedBuff);
        bool mainAttackReady = (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= attackIntervalMs;

        if (!mainAttackReady) return false;

        pl.Combat.LastAttackTime = DateTime.UtcNow;

        string attackHand;
        var effectiveMain = pl.Equipment.GetEffectiveMainHandWeapon();
        if (effectiveMain != null)
            attackHand = Equipment.IsCasterOffhand(effectiveMain) ? "off" : "main";
        else
        {
            var lh = pl.Equipment.Slots.TryGetValue("lhand", out var l) ? l : null;
            attackHand = (lh != null && !Equipment.IsCasterOffhand(lh) && !lh.TwoHanded) ? "off" : "main";
        }

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = attackHand }
        });

        // Урон PvP: та же формула, но по игроку
        int rawDmg = Math.Max(Balance.MinDamage, pl.GetTotalAttack(dist));
        bool isCrit = Random.Shared.NextDouble() * 100 < pl.GetCritChance();
        if (isCrit) rawDmg = (int)(rawDmg * pl.GetCritDamage());

        bool isEvaded = Random.Shared.NextDouble() * 100 < target.GetEvadeChance();
        bool isParried = !isEvaded && dist <= 1 && Random.Shared.NextDouble() * 100 < target.GetParryChance();
        bool isBlocked = !isEvaded && !isParried && Random.Shared.NextDouble() * 100 < target.GetBlockChance();

        int finalDmg = 0;
        if (!isEvaded && !isParried)
        {
            finalDmg = Math.Max(Balance.MinDamage, rawDmg - target.GetTotalDefense());
            if (isBlocked)
                finalDmg = Math.Max(Balance.MinDamage, finalDmg - target.GetBlockValue());
        }

        if (isEvaded)
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} уклонился от вашей атаки.");
        }
        else if (isParried)
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} парировал вашу атаку!");
        }
        else
        {
            target.Health -= finalDmg;
                    await TryLifesteal(pl, finalDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;

            string critText = isCrit ? " (КРИТ!)" : "";
            if (isBlocked)
                await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                    $"Вы нанесли {finalDmg} урона{critText} {target.Name} (блок). ({target.Health}/{target.MaxHealth + target.Equipment.GetBonusMaxHealth()}) HP");
            else
                await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                    $"Вы нанесли {finalDmg} урона{critText} {target.Name}. ({target.Health}/{target.MaxHealth + target.Equipment.GetBonusMaxHealth()}) HP");

            var targetClient = _svc.World.FindClientByPlayer(target);
            if (targetClient != null)
            {
                var hitMsg = new GameMessage
                {
                    Type = "damage",
                    Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = finalDmg, IsCrit = isCrit }
                };
                await _svc.Hub.SendToClient(targetClient, hitMsg);
                await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, hitMsg, target);
                await ChatTo(targetClient, ChatChannel.Combat, "Бой",
                    $"{pl.Name} нанёс вам {finalDmg} урона{critText}. ({target.Health}/{target.MaxHealth + target.Equipment.GetBonusMaxHealth()}) HP");
                await _svc.Hub.SendStatusAsync(targetClient, target);
            }
        }

        // Обновление combat state
        await _svc.Hub.SendToClient(atkClient, new GameMessage
        {
            Type = "combat_state",
            Data = new
            {
                InCombat = true,
                TargetId = target.Id.ToString(),
                TargetName = target.Name,
                TargetHp = target.Health,
                TargetMaxHp = target.MaxHealth + target.Equipment.GetBonusMaxHealth(),
                TargetX = target.X,
                TargetY = target.Y,
                IsPvP = true
            }
        });

        await _svc.Hub.SendStatusAsync(atkClient, pl);

        // Смерть PvP цели — без штрафа, просто респаун
        if (target.Health <= 0)
        {
            await HandlePvPDeath(pl, target, atkClient);
        }

        return true;
    }

    private bool ChasePlayerTarget(Player pl, Player target)
    {
        int moveIntervalMs = Balance.MoveIntervalMs(pl.Speed);
        bool canMove = (DateTime.UtcNow - pl.Movement.LastMoveTime).TotalMilliseconds >= moveIntervalMs;
        if (!canMove) return false;

        int stepX = Math.Sign(target.X - pl.X);
        int stepY = Math.Sign(target.Y - pl.Y);

        int mx = 0, my = 0;
        if (stepX != 0 && stepY != 0)
        {
            if (pl.X + stepX >= 0 && pl.X + stepX < _svc.World.Map.Width)
                mx = stepX;
            else
                my = stepY;
        }
        else if (stepX != 0) mx = stepX;
        else if (stepY != 0) my = stepY;

        if (mx == 0 && my == 0) return false;

        int nx = pl.X + mx;
        int ny = pl.Y + my;
        var zoneMap = _svc.Zones.GetOrCreateMap(pl.CurrentZoneId);
        if (nx < 0 || nx >= zoneMap.Width || ny < 0 || ny >= zoneMap.Height) return false;

        if (mx == 1) pl.Facing = "right";
        else if (mx == -1) pl.Facing = "left";
        else if (my == 1) pl.Facing = "down";
        else if (my == -1) pl.Facing = "up";

        pl.X = nx;
        pl.Y = ny;
        pl.Movement.LastMoveTime = DateTime.UtcNow;
        return true;
    }

    // ──────────────── Вспомогательные ────────────────

    private async Task HandleInvalidTarget(Player pl, Monster? monster)
    {
        if (monster != null && monster.IsMannequin && monster.Health <= 0)
        {
            monster.Health = monster.MaxHealth;
            monster.LastDamagedTime = DateTime.UtcNow;
            var mClient = _svc.World.FindClientByPlayer(pl);
            if (mClient != null)
            {
                await _svc.Hub.SendToClient(mClient, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                await ChatTo(mClient, ChatChannel.Combat, "Бой", $"{monster.Name} восстановил все HP!");
            }
            return;
        }
        pl.Combat.Cancel();
        var client = _svc.World.FindClientByPlayer(pl);
        if (client != null)
            await _svc.Hub.SendToClient(client, GameMessage.ResetCombat());
    }

    public static bool WeaponAffectsTarget(string subtype) => subtype is "dagger" or "spear" or "mace" or "hammer" or "greathammer";
}
