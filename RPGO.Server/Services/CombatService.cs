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

                    var monster = _svc.Monsters.FindMonsterById(pl.Combat.TargetMonsterId!.Value);
                    if (monster == null || monster.Health <= 0)
                    {
                        await HandleInvalidTarget(pl, monster);
                        changed = true;
                        continue;
                    }

                    int dist = Math.Abs(pl.X - monster.X) + Math.Abs(pl.Y - monster.Y);
                    int weaponRange = pl.Equipment.GetWeaponAttackRange();

                    if (dist > weaponRange)
                    {
                        if (ChaseTarget(pl, monster)) changed = true;
                    }
                    else
                    {
                        var client = _svc.World.FindClientByPlayer(pl);
                        if (client == null) continue;

                        int attackIntervalMs = Balance.AttackIntervalMs(
                            Balance.GetAttackSpeed(pl.Agility), pl.Equipment.GetWeaponSpeedModifier());
                        bool mainAttackReady = (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= attackIntervalMs;
                        bool offHandReady = pl.Equipment.IsDualWielding()
                            && pl.Combat.LastAttackTime > pl.Combat.OffHandLastAttackTime
                            && (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= Balance.OffHandDelayMs;

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

        string subtype = pl.Equipment.GetWeaponSubtype();
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

        if (weaponRange > 1 && !isEvaded)
        {
            string visualType = subtype == "bow" ? "arrow" : "magic_bolt";
            var proj = _svc.Projectiles.Spawn(pl, monster, visualType, dmgToMonster, isCrit);
            await _svc.Projectiles.BroadcastSpawn(proj);
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
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + dmgToMonster), IsCrit = isCrit }
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
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = dmgToMonster, IsCrit = isCrit }
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
        var (ohDmg, ohCrit, ohEvaded) = _svc.Monsters.CalculateOffHandAttack(pl, offMonster);

        if (ohEvaded)
        {
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} уклонился от удара вторым оружием.");
            return;
        }

        if (ohDmg <= 0) return;

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
        await _svc.Hub.SendToClient(client, ohDmgMsg);
        await _svc.Hub.SendDamageNearbyAsync(offMonster.X, offMonster.Y, ohDmgMsg, pl);

        if (offMonster.Health <= 0)
        {
            if (offMonster.IsMannequin)
            {
                offMonster.Health = offMonster.MaxHealth;
                offMonster.LastDamagedTime = DateTime.UtcNow;
                await ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{(ohCrit ? " (КРИТ!)" : "")}");
                await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(offMonster.Name, offMonster.Health, offMonster.MaxHealth));
                return;
            }

            var ohKillMsg = new GameMessage
            {
                Type = "damage",
                Data = new { Target = "monster", MonsterId = offMonster.Id.ToString(), X = offMonster.X, Y = offMonster.Y, Amount = Math.Max(0, offMonster.Health + ohDmg), IsCrit = ohCrit }
            };
            await _svc.KillService.ResolveMonsterKill(pl, offMonster, ohDmg, true, ohKillMsg);
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

        int sx = _svc.Merchant.MerchantX + _svc.World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
        int sy = _svc.Merchant.MerchantY + _svc.World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
        sx = Math.Clamp(sx, 0, _svc.World.Map.Width - 1);
        sy = Math.Clamp(sy, 0, _svc.World.Map.Height - 1);
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
