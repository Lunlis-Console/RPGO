using LostAndDivine.Server.Network;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server;

/// <summary>
/// PvE combat logic: attack loop, skills, death/respawn, debuff UI.
/// PvP extracted to PvPService, hazards to HazardService.
/// </summary>
public class CombatService
{
    private readonly IGameServices _svc;

    // Доступ к сервисам для skill-экзекуторов
    internal MonsterManager Monsters => _svc.Monsters;
    internal KillService KillService => _svc.KillService;
    internal DebuffManager Debuffs => _svc.Debuffs;
    internal GameWorld World => _svc.World;
    internal ZoneManager Zones => _svc.Zones;
    internal INetworkHub Hub => _svc.Hub;
    internal ProjectileManager Projectiles => _svc.Projectiles;

    public CombatService(IGameServices svc)
    {
        _svc = svc;
    }

    private Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
        => _svc.ChatTo(conn, channel, name, text);

    // Публичные хелперы для skill-экзекуторов
    internal Task ChatToC(ClientConnection? conn, string name, string text)
        => _svc.ChatToC(conn, name, text);

    internal async Task SendPlayerAttack(string playerName, string hand, string? skillId = null,
        int? targetX = null, int? targetY = null, int? buffDurationMs = null)
    {
        var data = new Dictionary<string, object> { ["PlayerName"] = playerName, ["Hand"] = hand };
        if (skillId != null) data["SkillId"] = skillId;
        if (targetX.HasValue) data["TargetX"] = targetX.Value;
        if (targetY.HasValue) data["TargetY"] = targetY.Value;
        if (buffDurationMs.HasValue) data["BuffDurationMs"] = buffDurationMs.Value;
        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = GameMessageType.PlayerAttack,
            Data = data
        });
    }

    internal async Task SendSkillCooldown(ClientConnection client, Skill skill, double cdMult = 1.0)
    {
        int effectiveCdMs = (int)(skill.CooldownMs * cdMult);
        await _svc.Hub.SendToClient(client, new GameMessage
        {
            Type = GameMessageType.SkillCooldown,
            Data = new { SkillId = skill.Id, RemainingMs = effectiveCdMs, TotalMs = effectiveCdMs }
        });
    }

    internal async Task SendDmgToMonster(ClientConnection client, Monster monster, int dmg, bool isCrit, string hand, Player pl, bool isSkill = false)
    {
        var dmgMsg = new GameMessage
        {
            Type = GameMessageType.Damage,
            Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = dmg, IsCrit = isCrit, Hand = hand, IsSkill = isSkill }
        };
        await _svc.Hub.SendToClient(client, dmgMsg);
        await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
        foreach (var targetter in _svc.World.GetPlayersSnapshot())
        {
            if (targetter.Id != pl.Id && targetter.Combat.HasTarget && targetter.Combat.TargetMonsterId == monster.Id)
            {
                var conn = _svc.World.FindClientByPlayer(targetter);
                if (conn != null) await _svc.Hub.SendToClient(conn, dmgMsg);
            }
        }
    }

    internal Task SendToC(ClientConnection client, GameMessage msg)
        => _svc.Hub.SendToClient(client, msg);

    internal async Task SendDmgNearbyTo(GameMessage msg, Player nearPlayer)
    {
        await _svc.Hub.SendDamageNearbyAsync(nearPlayer.X, nearPlayer.Y, msg, nearPlayer);
    }

    internal Task SendMyStatus(ClientConnection? client, Player pl)
    {
        if (client != null) return _svc.Hub.SendStatusAsync(client, pl);
        return Task.CompletedTask;
    }

    // ──────────────── Боевой тик (PvE), вызывается из единого игрового цикла ────────────────

    public async Task CombatTick()
    {
        bool changed = false;
        foreach (var pl in _svc.World.GetPlayersSnapshot())
        {
            if (pl.IsDead || !pl.Combat.HasTarget) continue;
            if (_svc.Debuffs.HasDebuff(pl, DebuffType.Stun)) continue;

            // PvP target — delegate to PvPService
            if (pl.Combat.IsPvPTarget)
            {
                changed |= await _svc.PvP.RunPvPTick(pl);
                continue;
            }

            var monster = _svc.Monsters.FindMonsterById(pl.Combat.TargetMonsterId!.Value);
            if (monster == null || monster.Health <= 0 || monster.ZoneId != pl.CurrentZoneId)
            {
                // Прерываем каст если цель потеряна
                if (pl.Combat.CastingSkillId != null)
                {
                    pl.Combat.CastingSkillId = null;
                    pl.Combat.CastTargetId = null;
                }
                await HandleInvalidTarget(pl, monster);
                changed = true;
                continue;
            }

            // Неблокирующий каст: если идёт каст — ждём или завершаем
            if (pl.Combat.CastingSkillId != null)
            {
                var cClient = _svc.World.FindClientByPlayer(pl);
                if (cClient == null)
                {
                    pl.Combat.CastingSkillId = null;
                    pl.Combat.CastTargetId = null;
                }
                else if (DateTime.UtcNow < pl.Combat.CastEndTime)
                {
                    if (_svc.Debuffs.HasDebuff(pl, DebuffType.Stun))
                    {
                        pl.Combat.CastingSkillId = null;
                        pl.Combat.CastTargetId = null;
                        await ChatTo(cClient, ChatChannel.Combat, "Бой", "Каст прерван — оглушение.");
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    string castId = pl.Combat.CastingSkillId;
                    Guid? castTarget = pl.Combat.CastTargetId;
                    pl.Combat.CastingSkillId = null;
                    pl.Combat.CastTargetId = null;
                    var castSkill = DatabaseManager.GetSkill(castId);
                    if (castSkill != null && monster.Id == castTarget && monster.Health > 0 && monster.ZoneId == pl.CurrentZoneId)
                    {
                        int curDist = Math.Abs(pl.X - monster.X) + Math.Abs(pl.Y - monster.Y);
                        int curRange = pl.GetEffectiveAttackRange();
                        if (curDist <= curRange)
                        {
                            await ExecuteMainHandAttack(pl, monster, cClient, castSkill, curRange, isCastCompletion: true);
                            await _svc.Hub.SendStatusAsync(cClient, pl);
                            changed = true;
                        }
                        else
                        {
                            await ChatTo(cClient, ChatChannel.Combat, "Бой", "Каст завершён, но цель вне досягаемости.");
                        }
                    }
                    else
                    {
                        await ChatTo(cClient, ChatChannel.Combat, "Бой", "Каст прерван — цель потеряна.");
                    }
                    continue;
                }
            }

            int dist = Math.Abs(pl.X - monster.X) + Math.Abs(pl.Y - monster.Y);
            int weaponRange = pl.GetEffectiveAttackRange();
            var offHandWeapon = pl.Equipment.GetOffHandWeapon();
            int offHandRange = offHandWeapon?.AttackRange ?? 0;
            bool offHandCanShoot = offHandRange > 1 && dist <= offHandRange;

            int attackIntervalMs = Balance.AttackIntervalMs(
                Balance.GetAttackSpeed(pl.GetAttackSpeedPoints()) * pl.Equipment.GetWeaponSpeedModifier() * pl.GetAttackSpeedGearMultiplier());
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

            {
                var buffClient = _svc.World.FindClientByPlayer(pl);
                if (buffClient != null)
                    await ProcessInstantBuffs(pl, buffClient);
            }

            if (dist > weaponRange && offHandCanShoot)
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
            else if (dist <= weaponRange)
            {
                var client = _svc.World.FindClientByPlayer(pl);
                if (client == null) continue;

                if (pl.Combat.PendingSkillHitsRemaining > 0)
                {
                    var punishExec = Skills.SkillRegistry.Get(pl.Combat.PendingSkillId ?? "");
                    if (punishExec != null && await punishExec.CheckPunishPvE(pl, monster, client, this))
                    {
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
                        int interval = Skills.SkillRegistry.Get(pl.Combat.PendingSkillId ?? "")?.ComboIntervalMs ?? Balance.SlashHitIntervalMs;
                        if (elapsed >= interval)
                        {
                            await ExecuteComboHit(pl, monster, client);
                            changed = true;
                        }
                        await _svc.Hub.SendStatusAsync(client, pl);
                        changed = true;
                        continue;
                    }
                }

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
        if (changed)
        {
            foreach (var pl in _svc.World.GetPlayersSnapshot())
                _svc.Hub.MarkZoneDirty(pl.CurrentZoneId);
            await _svc.Hub.BroadcastMapAsync();
        }
    }

    // ──────────────── Атаки ────────────────

    public async Task ExecuteMainHandAttack(Player pl, Monster monster, ClientConnection client, Skill? queuedSkill, int weaponRange, bool isCastCompletion = false)
    {
        if (!isCastCompletion)
            pl.Combat.LastAttackTime = DateTime.UtcNow;

        int dx = monster.X - pl.X;
        int dy = monster.Y - pl.Y;
        int dist = Math.Abs(dx) + Math.Abs(dy);
        if (Math.Abs(dx) >= Math.Abs(dy))
            pl.Facing = dx > 0 ? Facing.Right : Facing.Left;
        else
            pl.Facing = dy > 0 ? Facing.Down : Facing.Up;

        string subtype = pl.Equipment.GetWeaponSubtype();
        WeaponCategory category = pl.Equipment.GetWeaponCategory();
        string attackHand;
        var effectiveMain = pl.Equipment.GetEffectiveMainHandWeapon();
        if (effectiveMain != null)
        {
            attackHand = Equipment.IsCasterOffhand(effectiveMain) ? "off" : "main";
        }
        else
        {
            var lh = pl.Equipment.Slots.TryGetValue("lhand", out var l) ? l : null;
            attackHand = (lh != null && !Equipment.IsCasterOffhand(lh) && !lh.TwoHanded) ? "off" : "main";
        }
        bool isCastStart = queuedSkill != null && !isCastCompletion && queuedSkill.CastTimeMs > 0 && (int)(queuedSkill.CastTimeMs * pl.GetCastTimeMultiplier()) > 0;
        bool doWeaponProc = !isCastStart;
        bool forceProc = queuedSkill?.Id == SkillIds.StrongArm && weaponRange <= 1;
        if (doWeaponProc && !string.IsNullOrEmpty(subtype))
        {
            var (debuff, isNew) = forceProc
                ? _svc.Debuffs.ForceWeaponProc(pl, monster, subtype)
                : _svc.Debuffs.OnWeaponProc(pl, monster, subtype);
            if (debuff != null)
            {
                string action = isNew ? "наложено" : "обновлено";
                string targetName = WeaponAffectsTarget(category) ? monster.Name : pl.Name;
                await ChatTo(client, ChatChannel.Combat, "Бой",
                    $"{debuff.DisplayName} {action} на {targetName} ({debuff.DurationMs / 1000}с)");
                if (monster.GetDebuffsSnapshot().Count > 0)
                    await SendTargetDebuffUpdateAsync(monster);
            }
        }

        if (queuedSkill != null)
        {
            bool skillBlocked = queuedSkill.Id == SkillIds.StrongArm && weaponRange > 1;
            if (skillBlocked)
            {
                await ChatTo(client, ChatChannel.Combat, "Бой",
                    $"«{queuedSkill.Name}» доступен только с оружием ближнего боя.");
                pl.QueuedSkillIds.RemoveAt(0);
                await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, _svc.Hub);
                queuedSkill = null;
            }
            else if (queuedSkill.Id == SkillIds.Flurry)
            {
                var buff = ActiveDebuff.Create(DebuffType.AttackSpeedBonus, Balance.AttackSpeedBonusValue,
                    Balance.AttackSpeedBonusDurationMs, "skill", "Проворность",
                    $"Увеличивает скорость атаки на {(int)(Balance.AttackSpeedBonusValue * 100)}%");
                _svc.Debuffs.ApplyDebuff(pl, buff);
                await SendPlayerAttack(pl.Name, "main", SkillIds.Flurry, buffDurationMs: Balance.AttackSpeedBonusDurationMs);
                pl.Mana = Math.Max(0, pl.Mana - queuedSkill.MpCost);
                pl.LastSkillUse[queuedSkill.Id] = DateTime.UtcNow;
                pl.QueuedSkillIds.RemoveAt(0);
                await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, _svc.Hub);
                await SendSkillCooldown(client, queuedSkill, pl.GetSkillRankCdMult(queuedSkill.Id));
                await ChatTo(client, ChatChannel.Combat, "Бой", $"Применён навык «{queuedSkill.Name}»! Проворность на 10 сек.");
                return;
            }
            else
            {
                // Неблокирующий каст: вместо Task.Delay ставим состояние каста и выходим
                if (!isCastCompletion && queuedSkill.CastTimeMs > 0)
                {
                    int castMs = (int)(queuedSkill.CastTimeMs * pl.GetCastTimeMultiplier());
                    if (castMs > 0)
                    {
                        pl.Combat.CastingSkillId = queuedSkill.Id;
                        pl.Combat.CastEndTime = DateTime.UtcNow.AddMilliseconds(castMs);
                        pl.Combat.CastTargetId = monster.Id;
                        if (pl.QueuedSkillIds.Count > 0 && pl.QueuedSkillIds[0] == queuedSkill.Id)
                        {
                            pl.QueuedSkillIds.RemoveAt(0);
                            await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, _svc.Hub);
                        }
                        await ChatTo(client, ChatChannel.Combat, "Бой", $"Каст «{queuedSkill.Name}» {castMs}мс...");
                        // Отправляем кулдаун сразу? Нет, после завершения
                        return;
                    }
                }

                var executor = Skills.SkillRegistry.Get(queuedSkill.Id);
                if (executor != null)
                {
                    bool ok = await executor.ExecutePvE(pl, monster, queuedSkill, client, this, weaponRange);
                    if (ok) return;

                    if (pl.QueuedSkillIds.Count > 0 && pl.QueuedSkillIds[0] == queuedSkill.Id)
                    {
                        pl.QueuedSkillIds.RemoveAt(0);
                        await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, _svc.Hub);
                    }
                    queuedSkill = null;
                }
                else
                {
                    int baseDamage = (int)Math.Max(Balance.MinDamage,
                        _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage())
                        * (1.0 - _svc.Monsters.GetEffectiveDefense(monster, pl.GetArmorPenetration() / 100.0, magic: pl.IsMagicalDamage())));
                    double rankMult = pl.GetSkillRankDmgMult(queuedSkill.Id);
                    int skillDamage = (int)Math.Max(Balance.MinDamage, baseDamage * queuedSkill.DamageMultiplier * rankMult);
                    skillDamage = _svc.Monsters.ApplyDmgReduction(pl, skillDamage);
                    monster.Health -= skillDamage;
                    monster.LastDamagedTime = DateTime.UtcNow;
                    monster.DamageTracker.AddOrUpdate(pl.Id, skillDamage, (k, old) => old + skillDamage);
                    pl.Mana = Math.Max(0, pl.Mana - queuedSkill.MpCost);
                    pl.LastSkillUse[queuedSkill.Id] = DateTime.UtcNow;
                    pl.QueuedSkillIds.RemoveAt(0);
                    await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, _svc.Hub);
                    await SendSkillCooldown(client, queuedSkill, pl.GetSkillRankCdMult(queuedSkill.Id));
                    await SendPlayerAttack(pl.Name, attackHand, queuedSkill.Id, monster.X, monster.Y);
                    await ChatTo(client, ChatChannel.Combat, "Бой", $"Применён навык «{queuedSkill.Name}»! {skillDamage} урона (x{queuedSkill.DamageMultiplier}).");
                    await SendDmgToMonster(client, monster, skillDamage, false, attackHand, pl, isSkill: true);
                    await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                    if (monster.Health <= 0)
                    {
                        if (monster.IsMannequin)
                        {
                            monster.Health = monster.MaxHealth;
                            await ChatTo(client, ChatChannel.Combat, "Бой", "Манекен восстановил все HP!");
                        }
                        else
                            await _svc.KillService.ResolveMonsterKill(pl, monster, skillDamage, true, null);
                    }
                    return;
                }
            }
        }

        var (dmgToMonster, dmgToPlayer, monsterDead, isCrit, isEvaded, isParried, isBlocked) =
            _svc.Monsters.CalculateCombat(pl, monster, weaponRange <= 1, weaponRange <= 1);

        if (monster.ReturningToSpawn && !isEvaded && !isParried && !isBlocked)
        {
            var retMsg = GameMessage.Damage("monster", monster.Id.ToString(), monster.X, monster.Y, 0, false, pl.Name, result: "returning");
            await _svc.Hub.SendToClient(client, retMsg);
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} возвращается на спавн — не получает урон.");
            return;
        }

        await TryLifesteal(pl, dmgToMonster, weaponRange <= 1, client);

        if (!isEvaded && weaponRange <= 1 && _svc.Debuffs.HasDebuff(pl, DebuffType.CleaveReady))
        {
            _svc.Debuffs.ClearDebuffs(pl);
            _svc.Monsters.CalculateCleave(pl, monster);
        }

        await SendPlayerAttack(pl.Name, attackHand, targetX: monster.X, targetY: monster.Y);

        if (weaponRange > 1 && !isEvaded)
        {
            if (Math.Abs(dx) >= Math.Abs(dy))
                pl.Facing = dx > 0 ? Facing.Right : Facing.Left;
            else
                pl.Facing = dy > 0 ? Facing.Down : Facing.Up;

            string visualType = category == WeaponCategory.Bow ? "arrow" : "magic_bolt";
            var proj = _svc.Projectiles.Spawn(pl, monster, visualType, dmgToMonster, isCrit, attackHand);
            await _svc.Projectiles.BroadcastSpawn(proj);

            if (category == WeaponCategory.Bow && dmgToMonster > 0)
                await TryExtraArrow(pl, monster, client, attackHand);

            if (_svc.Debuffs.HasDebuff(pl, DebuffType.SuppressingFire) && category == WeaponCategory.Bow)
                await ApplySuppressingFireCone(pl, monster, client);

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
                Type = GameMessageType.Damage,
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
                Type = GameMessageType.Damage,
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = dmgToMonster, IsCrit = isCrit, Hand = attackHand }
            };
            await _svc.Hub.SendToClient(client, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
        }

        await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
        await _svc.Hub.SendToClient(client, new GameMessage
        {
            Type = GameMessageType.CombatState,
            Data = new { InCombat = true, TargetId = monster.Id.ToString(), TargetName = monster.Name, TargetHp = monster.Health, TargetMaxHp = monster.MaxHealth }
        });

        if (!isEvaded && dmgToPlayer > 0)
        {
            pl.Health -= dmgToPlayer;
            pl.LastDamagedTime = DateTime.UtcNow;
            var hitMsg = new GameMessage
            {
                Type = GameMessageType.Damage,
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
            await _svc.PlayerDeath.HandlePlayerDeath(pl, client);
        }
    }

    // ──────────────── Комбо-удар (продолжение навыка) ────────────────

    private async Task ExecuteComboHit(Player pl, Monster monster, ClientConnection client)
    {
        string skillId = pl.Combat.PendingSkillId ?? "";
        var executor = Skills.SkillRegistry.Get(skillId);
        if (executor != null)
            await executor.ExecuteComboPvE(pl, monster, client, this);
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
            Type = GameMessageType.PlayerAttack,
            Data = new { PlayerName = pl.Name, Hand = "main", TargetX = monster.X, TargetY = monster.Y }
        });

        var rng = Random.Shared;
        double effDefense = _svc.Monsters.GetEffectiveDefense(monster, pl.GetArmorPenetration() / 100.0, magic: pl.IsMagicalDamage());
        double effAttack = _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage());
        bool evaded = rng.Next(Balance.ChanceRollMax) < Math.Max(0,
            monster.GetEvadeChance() - (pl.GetAccuracy() - BalanceStatic.AccuracyBase));
        bool parried = !evaded && rng.Next(Balance.ChanceRollMax) < monster.GetParryChance();
        bool blocked = !evaded && !parried && rng.Next(Balance.ChanceRollMax) < monster.GetBlockChance();
        int hitDmg = 0;

        if (!evaded && !parried)
        {
            int baseDmg = Math.Max(Balance.MinDamage, (int)(effAttack * (1.0 - effDefense)));
            double mult = Balance.DuelPunishBaseMult + remainingHits * Balance.DuelPunishPerMissMult;
            hitDmg = (int)Math.Max(Balance.MinDamage, baseDmg * mult);
            hitDmg = _svc.Monsters.ApplyDmgReduction(pl, hitDmg);
            if (blocked)
                hitDmg = 0;
            monster.Health -= hitDmg;
                    await TryLifesteal(pl, hitDmg, true, client);
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker.AddOrUpdate(pl.Id, hitDmg, (k, old) => old + hitDmg);
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
                Type = GameMessageType.Damage,
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = hitDmg, IsCrit = false, Hand = "main" }
            };
            await _svc.Hub.SendToClient(client, dmgMsg);
            await _svc.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, pl);
        }

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
                Type = GameMessageType.Damage,
                Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = Math.Max(0, monster.Health + hitDmg), IsCrit = false, Hand = "main" }
            } : null;
            await _svc.KillService.ResolveMonsterKill(pl, monster, hitDmg, !evaded, killDmgMsg);
            return;
        }

        await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
    }

    // «Кровопускание» (SK0010): лечение части урона
    internal async Task TryLifesteal(Player pl, int dealt, bool isMelee, ClientConnection? client)
    {
        if (dealt <= 0) return;
        if (!pl.LearnedSkills.Contains(SkillIds.Bloodletting)) return;
        if (!isMelee || !IsWieldingMelee(pl)) return;

        int heal = (int)(dealt * Balance.LifestealFraction * pl.GetPassiveRankMult(SkillIds.Bloodletting));
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
        return w.Category is not (WeaponCategory.Staff or WeaponCategory.Bow or WeaponCategory.Grimoire or WeaponCategory.Sphere);
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
        if (offMonster == null) return;
        if (offMonster.Health <= 0)
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
        WeaponCategory offCategory = offWeapon?.Category ?? WeaponCategory.None;
        int offWeaponRange = offWeapon?.AttackRange ?? 1;

        int dx = offMonster.X - pl.X;
        int dy = offMonster.Y - pl.Y;
        int dist = Math.Abs(dx) + Math.Abs(dy);
        bool isRangedAttack = offWeaponRange > 1 && dist > 1;

        if (Math.Abs(dx) >= Math.Abs(dy))
            pl.Facing = dx > 0 ? Facing.Right : Facing.Left;
        else
            pl.Facing = dy > 0 ? Facing.Down : Facing.Up;

        if (isRangedAttack)
        {
            var (ohDmg, ohCrit, ohEvaded, ohParried, ohBlocked) = _svc.Monsters.CalculateOffHandAttack(pl, offMonster);

            string visualType = offCategory == WeaponCategory.Bow ? "arrow" : "magic_bolt";
            var proj = _svc.Projectiles.Spawn(pl, offMonster, visualType, ohDmg, ohCrit, "off");
            await _svc.Projectiles.BroadcastSpawn(proj);

            if (ohEvaded)
            {
                await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} уклонился от атаки.");
                await _svc.Hub.BroadcastMapAsync();
                return;
            }
            if (ohParried)
            {
                await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} парировал атаку!");
                await _svc.Hub.BroadcastMapAsync();
                return;
            }

            await _svc.Hub.BroadcastMapAsync();
            return;
        }

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = GameMessageType.PlayerAttack,
            Data = new { PlayerName = pl.Name, Hand = "off", TargetX = offMonster.X, TargetY = offMonster.Y }
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

        if (offMonster.ReturningToSpawn)
        {
            var retMsg = GameMessage.Damage("monster", offMonster.Id.ToString(), offMonster.X, offMonster.Y, 0, false, pl.Name, result: "returning");
            await _svc.Hub.SendToClient(client, retMsg);
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{offMonster.Name} возвращается на спавн — не получает урон.");
            return;
        }

        await TryLifesteal(pl, meleeDmg, true, client);

        offMonster.Health -= meleeDmg;
        offMonster.LastDamagedTime = DateTime.UtcNow;
        offMonster.DamageTracker.AddOrUpdate(pl.Id, meleeDmg, (k, old) => old + meleeDmg);

        string critText = meleeCrit ? " (КРИТ!)" : "";
        string ohWeaponName = pl.Equipment.GetOffHandWeapon()?.Name ?? "оружие";
        await ChatTo(client, ChatChannel.Combat, "Бой", $"Второе оружие ({ohWeaponName}) нанесло {meleeDmg} урона{critText} {offMonster.Name}.");

        var dmgMsg = new GameMessage
        {
            Type = GameMessageType.Damage,
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
                Type = GameMessageType.Damage,
                Data = new { Target = "monster", MonsterId = offMonster.Id.ToString(), X = offMonster.X, Y = offMonster.Y, Amount = Math.Max(0, offMonster.Health + meleeDmg), IsCrit = meleeCrit, Hand = "off" }
            };
            await _svc.KillService.ResolveMonsterKill(pl, offMonster, meleeDmg, true, killMsg);
        }
        else
        {
            await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(offMonster.Name, offMonster.Health, offMonster.MaxHealth));
            await _svc.Hub.SendToClient(client, new GameMessage
            {
                Type = GameMessageType.CombatState,
                Data = new { InCombat = true, TargetId = offMonster.Id.ToString(), TargetName = offMonster.Name, TargetHp = offMonster.Health, TargetMaxHp = offMonster.MaxHealth }
            });
        }
    }

    // ──────────────── Преследование ────────────────

    public bool ChaseTarget(Player pl, Monster monster)
    {
        if (_svc.Debuffs.HasDebuff(pl, DebuffType.Stun)) return false;
        if (_svc.Debuffs.HasDebuff(pl, DebuffType.Root)) return false;
        int dist = Math.Abs(pl.X - monster.X) + Math.Abs(pl.Y - monster.Y);
        int weaponRange = pl.GetEffectiveAttackRange();
        if (dist <= weaponRange) return false;

        var zoneMap = _svc.Zones.GetOrCreateMap(pl.CurrentZoneId);
        bool blockedCell(int nx, int ny)
        {
            if (zoneMap.IsObstacle(nx, ny)) return true;
            var m = _svc.Monsters.FindMonsterAt(nx, ny);
            return m != null && m.Id != monster.Id;
        }

        if (!FindChaseCell(zoneMap.Width, zoneMap.Height, blockedCell,
                monster.X, monster.Y, pl.X, pl.Y, weaponRange,
                out int bestX, out int bestY))
            return false;
        if (pl.X == bestX && pl.Y == bestY) return true;

        var path = _svc.Pathfinding.FindPath(pl.X, pl.Y, bestX, bestY, pl.CurrentZoneId);
        if (path.Count == 0) return false;
        pl.Movement.SetPath(path);
        return true;
    }

    /// <summary>
    /// Ищет клетку остановки при погоне: кольцо на дистанции атаки (range) от цели,
    /// предпочитая ближайшую к игроку; если кольцо полностью недоступно — ближайшие
    /// клетки (вплоть до соседних). Не даёт дальнобойному персонажу вставать вплотную.
    /// </summary>
    public static bool FindChaseCell(int mapWidth, int mapHeight, Func<int, int, bool> blocked,
        int targetX, int targetY, int playerX, int playerY, int range,
        out int bestX, out int bestY)
    {
        bestX = -1; bestY = -1;
        for (int r = Math.Max(1, range); r >= 1; r--)
        {
            int bestDist = int.MaxValue;
            for (int dx = -r; dx <= r; dx++)
            {
                int dyAbs = r - Math.Abs(dx);
                for (int s = 0; s < 2; s++)
                {
                    int dy = s == 0 ? dyAbs : -dyAbs;
                    int nx = targetX + dx;
                    int ny = targetY + dy;
                    if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight) continue;
                    if (blocked(nx, ny)) continue;
                    int d = Math.Abs(nx - playerX) + Math.Abs(ny - playerY);
                    if (d < bestDist) { bestDist = d; bestX = nx; bestY = ny; }
                }
            }
            if (bestX >= 0) return true;
        }
        return false;
    }

    // ──────────────── Навыки ────────────────

    private static readonly HashSet<string> InstantBuffSkills = new() { SkillIds.Flurry };

    internal async Task ProcessInstantBuffs(Player pl, ClientConnection client)
    {
        if (pl.QueuedSkillIds.Count == 0) return;
        var sid = pl.QueuedSkillIds[0];
        if (!InstantBuffSkills.Contains(sid)) return;

        var skill = DatabaseManager.GetSkill(sid);
        if (skill == null) { pl.QueuedSkillIds.RemoveAt(0); return; }

        bool onCd = pl.LastSkillUse.TryGetValue(sid, out var last)
            && (DateTime.UtcNow - last).TotalMilliseconds < skill.CooldownMs * pl.GetSkillRankCdMult(sid);
        if (onCd || pl.Mana < skill.MpCost)
        {
            pl.QueuedSkillIds.RemoveAt(0);
            await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, _svc.Hub);
            return;
        }

        pl.Mana = Math.Max(0, pl.Mana - skill.MpCost);
        pl.LastSkillUse[skill.Id] = DateTime.UtcNow;
        pl.QueuedSkillIds.RemoveAt(0);

        if (skill.Id == SkillIds.Flurry)
        {
            var buff = ActiveDebuff.Create(DebuffType.AttackSpeedBonus, Balance.AttackSpeedBonusValue,
                Balance.AttackSpeedBonusDurationMs, "skill", "Проворность",
                $"Увеличивает скорость атаки на {(int)(Balance.AttackSpeedBonusValue * 100)}%");
            _svc.Debuffs.ApplyDebuff(pl, buff);

            _ = _svc.Hub.SendToAllAsync(new GameMessage
            {
                Type = GameMessageType.PlayerAttack,
                Data = new { PlayerName = pl.Name, Hand = "main", SkillId = SkillIds.Flurry, BuffDurationMs = Balance.AttackSpeedBonusDurationMs }
            });
        }

        await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, _svc.Hub);
        await _svc.Hub.SendToClient(client, new GameMessage
        {
            Type = GameMessageType.SkillCooldown,
            Data = new { SkillId = skill.Id, RemainingMs = (int)(skill.CooldownMs * pl.GetSkillRankCdMult(skill.Id)), TotalMs = (int)(skill.CooldownMs * pl.GetSkillRankCdMult(skill.Id)) }
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
            && (DateTime.UtcNow - last).TotalMilliseconds < cand.CooldownMs * pl.GetSkillRankCdMult(sid);
        bool noMp = pl.Mana < cand.MpCost;

        if (onCd || noMp)
        {
            pl.QueuedSkillIds.RemoveAt(0);
            await MessageHandlers.UseSkillHandler.SendSkillQueue(client, pl, _svc.Hub);
            await _svc.Hub.SendToClient(client, GameMessage.Chat("Бой", onCd
                ? $"«{cand.Name}» ещё на перезарядке — пропускаем."
                : $"«{cand.Name}»: недостаточно маны ({pl.Mana}/{cand.MpCost}) — пропускаем."));
            return null;
        }

        return cand;
    }

    // ──────────────── Цикл монстр-атак ────────────────

    public async Task MonsterAttackTick()
        => await _svc.MonsterAttacks.MonsterAttackTick();

    // ──────────────── Дебаффы ────────────────

    public async Task SendTargetDebuffUpdateAsync(Monster mon)
    {
        var debuffData = mon.GetDebuffsSnapshot().Select(d => new
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

    public async Task SendTargetPlayerDebuffUpdateAsync(Player target, ClientConnection? requester = null)
    {
        var debuffData = target.GetDebuffsSnapshot().Select(d => new
        {
            Type = d.Type.ToString(),
            d.DisplayName,
            d.Description,
            Value = Math.Round(d.Value, 2),
            d.RemainingMs,
            DurationMs = d.DurationMs
        }).ToList();
        Log.Debug($"SendTargetPlayerDebuffUpdate: {target.Name} debuffs={debuffData.Count}");
        foreach (var d in debuffData)
            Log.Debug($"  {d.Type}: {d.DisplayName}");

        var msg = GameMessage.TargetDebuffUpdate(debuffData);
        bool sent = false;
        if (requester != null)
        {
            await _svc.Hub.SendToClient(requester, msg);
            sent = true;
        }
        foreach (var pl in _svc.World.GetPlayersSnapshot())
        {
            if (pl.Combat.HasTarget && pl.Combat.TargetPlayerId == target.Id)
            {
                var conn = _svc.World.FindClientByPlayer(pl);
                if (conn != null) { await _svc.Hub.SendToClient(conn, msg); sent = true; }
            }
        }
        if (!sent) Log.Debug($"  → никому не отправлено (никто не смотрит на {target.Name})");
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

    public static bool WeaponAffectsTarget(WeaponCategory category) => category is WeaponCategory.Dagger or WeaponCategory.Spear or WeaponCategory.Mace or WeaponCategory.Hammer or WeaponCategory.Greathammer;

    // «Вам подарочек» (SK0017)
    private async Task TryExtraArrow(Player pl, Monster primary, ClientConnection client, string hand)
    {
        double chance = pl.GetExtraArrowChance();
        if (chance <= 0) return;
        if (Random.Shared.Next(Balance.ChanceRollMax) >= chance) return;

        var (extraDmg, _, _, extraCrit, extraEvaded, _, _) =
            _svc.Monsters.CalculateCombat(pl, primary, true, isMelee: false);
        if (extraEvaded)
        {
            await ChatTo(client, ChatChannel.Combat, "Бой", $"{primary.Name} уклонился от доп. стрелы.");
            return;
        }
        if (extraDmg <= 0) return;
        string visualType = "arrow";
        var proj = _svc.Projectiles.Spawn(pl, primary, visualType, extraDmg, extraCrit, hand);
        await _svc.Projectiles.BroadcastSpawn(proj);
        await ChatTo(client, ChatChannel.Combat, "Бой", $"«Вам подарочек»: +{extraDmg} урона{(extraCrit ? " (КРИТ!)" : "")}!");
        if (primary.Health <= 0 && !primary.IsMannequin)
            await _svc.KillService.ResolveMonsterKill(pl, primary, extraDmg, true, null);
    }

    // «Подавляющий огонь» (SK0015) — AoE-конус
    private async Task ApplySuppressingFireCone(Player pl, Monster primary, ClientConnection client)
    {
        double mult = Balance.SuppressingFireDmgMult * pl.GetSkillRankDmgMult(SkillIds.SuppressingFire);
        int range = pl.GetEffectiveAttackRange();
        string visualType = pl.Equipment.GetWeaponCategory() == WeaponCategory.Bow ? "arrow" : "magic_bolt";

        (int tx1, int ty1, int tx2, int ty2) = pl.Facing switch
        {
            Facing.Right => (pl.X + range, pl.Y + range, pl.X + range, pl.Y - range),
            Facing.Left => (pl.X - range, pl.Y + range, pl.X - range, pl.Y - range),
            Facing.Down => (pl.X + range, pl.Y + range, pl.X - range, pl.Y + range),
            _ => (pl.X + range, pl.Y - range, pl.X - range, pl.Y - range)
        };
        await _svc.Projectiles.BroadcastArrowVisual(pl.X, pl.Y, tx1, ty1, visualType);
        await _svc.Projectiles.BroadcastArrowVisual(pl.X, pl.Y, tx2, ty2, visualType);

        foreach (var m in _svc.Monsters.GetAllMonstersIncludingInstances())
        {
            if (m.Id == primary.Id || m.Health <= 0 || m.ZoneId != pl.CurrentZoneId) continue;
            int mdx = m.X - pl.X, mdy = m.Y - pl.Y;
            int dist = Math.Abs(mdx) + Math.Abs(mdy);
            if (dist < 1 || dist > range) continue;
            if (!InFacingCone(pl.Facing, mdx, mdy)) continue;

            double effAtk = _svc.Monsters.GetEffectiveAttack(pl, pl.GetMaxAttackDamage(dist));
            double effDef = _svc.Monsters.GetEffectiveDefense(m, pl.GetCloseRangeArmorPen(dist) + pl.GetArmorPenetration() / 100.0, magic: pl.IsMagicalDamage());
            int dmg = Math.Max(Balance.MinDamage, (int)(effAtk * (1.0 - effDef) * mult));
            var proj = _svc.Projectiles.Spawn(pl, m, visualType, dmg, false, "main", "Подавляющий огонь");
            await _svc.Projectiles.BroadcastSpawn(proj);
        }
    }

    private static bool InFacingCone(Facing facing, int dx, int dy) => facing switch
    {
        Facing.Right => dx > 0 && Math.Abs(dy) <= Math.Max(1, dx),
        Facing.Left => dx < 0 && Math.Abs(dy) <= Math.Max(1, -dx),
        Facing.Down => dy > 0 && Math.Abs(dx) <= Math.Max(1, dy),
        Facing.Up => dy < 0 && Math.Abs(dx) <= Math.Max(1, -dy),
        _ => false
    };
}
