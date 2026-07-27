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

                        // Бафф-навыки применяются сразу, не дожидаясь атаки
                        await ProcessInstantBuffs(pl, client);

                        bool mainAttackReady = (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= attackIntervalMs;

                        if (!mainAttackReady && !offHandReady) continue;

                        var queuedSkill = await ProcessSkillQueue(pl, client);

                        bool mainFired = false;
                        if (mainAttackReady)
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

        var (dmgToMonster, dmgToPlayer, monsterDead, isCrit, isEvaded) =
            _svc.Monsters.CalculateCombat(pl, monster, queuedSkill == null && weaponRange <= 1);

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
            var (ohDmg, ohCrit, ohEvaded) = _svc.Monsters.CalculateOffHandAttack(pl, offMonster);

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

        var (meleeDmg, meleeCrit, meleeEvaded) = _svc.Monsters.CalculateOffHandAttack(pl, offMonster);

        if (meleeEvaded)
        {
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} уклонился от удара вторым оружием.");
            return;
        }

        if (meleeDmg <= 0) return;

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
                    player.Health -= damage;
                    player.LastDamagedTime = DateTime.UtcNow;
                    var client = _svc.World.FindClientByPlayer(player);
                    if (client == null) continue;

                    var hitMsg = GameMessage.Damage("player", null, player.X, player.Y, damage, false, player.Name);
                    await _svc.Hub.SendToClient(client, hitMsg);
                    await _svc.Hub.SendDamageNearbyAsync(player.X, player.Y, hitMsg, player);

                    await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                    await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} нанёс вам {damage} урона. ({player.Health}/{player.MaxHealth + player.Equipment.GetBonusMaxHealth()}) HP");

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

        double evadeRoll = Random.Shared.NextDouble() * 100;
        bool isEvaded = evadeRoll < target.GetEvadeChance();

        int finalDmg = isEvaded ? 0 : Math.Max(Balance.MinDamage, rawDmg - target.GetTotalDefense());

        if (!isEvaded)
        {
            target.Health -= finalDmg;
            target.LastDamagedTime = DateTime.UtcNow;

            // Уведомление атакующего
            string critText = isCrit ? " (КРИТ!)" : "";
            await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                $"Вы нанесли {finalDmg} урона{critText} {target.Name}. ({target.Health}/{target.MaxHealth + target.Equipment.GetBonusMaxHealth()}) HP");

            // Урон目标
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
        else
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} уклонился от вашей атаки.");
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
            target.Combat.Cancel();
            target.Interaction.Clear();
            target.Movement.Stop();
            target.IsDead = true;
            target.DeathTime = DateTime.UtcNow;

            var targetClient = _svc.World.FindClientByPlayer(target);
            if (targetClient != null)
            {
                await _svc.Hub.SendToClient(targetClient, GameMessage.ResetCombat());
                await _svc.Hub.SendToClient(targetClient, GameMessage.PlayerDeath(0));
                await ChatTo(targetClient, ChatChannel.System, "Система",
                    $"Вы погибли в PvP от {pl.Name}! Возрождение через 5 сек...");
            }

            Log.Info($"{pl.Name} убил {target.Name} в PvP!");

            var atkConn = _svc.World.FindClientByPlayer(pl);
            if (atkConn != null)
                await ChatTo(atkConn, ChatChannel.System, "Система", $"Вы победили {target.Name} в PvP!");
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
