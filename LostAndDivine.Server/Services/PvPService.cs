using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server;

/// <summary>
/// PvP combat logic: hit resolution, death, duel skill, chase.
/// Extracted from CombatService for separation of concerns.
/// </summary>
public class PvPService
{
    private readonly IGameServices _svc;

    internal MonsterManager Monsters => _svc.Monsters;
    internal KillService KillService => _svc.KillService;
    internal DebuffManager Debuffs => _svc.Debuffs;
    internal GameWorld World => _svc.World;
    internal INetworkHub Hub => _svc.Hub;
    internal ProjectileManager Projectiles => _svc.Projectiles;

    public PvPService(IGameServices svc)
    {
        _svc = svc;
    }

    private Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
        => _svc.ChatTo(conn, channel, name, text);

    internal Task SendToC(ClientConnection client, GameMessage msg)
        => _svc.Hub.SendToClient(client, msg);

    // ──────── PvP defense roll ────────

    private readonly record struct PvPDefenseRoll(bool Evaded, bool Parried, bool Blocked);
    private readonly record struct PvPHitResult(int Damage, bool Crit, PvPDefenseRoll Roll);

    private static PvPDefenseRoll RollPvPDefense(Player attacker, Player target, int dist)
    {
        bool isMelee = dist <= 1;
        double targetEvade = Math.Max(0, target.GetEvadeChance() - (attacker.GetAccuracy() - BalanceStatic.AccuracyBase));
        var (evaded, parried, blocked) = CombatMath.RollDefense(targetEvade, target.GetParryChance(), target.GetBlockChance(), isMelee);
        return new PvPDefenseRoll(evaded, parried, blocked);
    }

    private async Task<PvPHitResult> ResolvePvPHit(
        Player attacker, Player target, ClientConnection atkClient,
        double damageMult, bool checkCrit)
    {
        int dist = Math.Abs(attacker.X - target.X) + Math.Abs(attacker.Y - target.Y);
        var roll = RollPvPDefense(attacker, target, dist);

        int hitDmg = 0;
        bool hitCrit = false;
        if (!roll.Evaded && !roll.Parried)
        {
            double reduction = CombatMath.CalcDefenseReduction(
                attacker.IsMagicalDamage() ? target.GetTotalResistance() : target.GetTotalDefense())
                * (1.0 - attacker.GetArmorPenetration() / 100.0);
            int rawDmg = Math.Max(Balance.MinDamage, (int)(attacker.GetTotalAttack(dist) * (1.0 - reduction)));
            hitDmg = (int)Math.Max(Balance.MinDamage, rawDmg * damageMult);
            if (checkCrit)
            {
                hitCrit = Balance.RollPercent(Math.Max(0, attacker.GetCritChance() - target.GetTenacity()));
                if (hitCrit) hitDmg = (int)(hitDmg * attacker.GetCritDamage());
            }
            if (roll.Blocked) hitDmg = 0;
            target.Health -= hitDmg;
            await _svc.Combat.TryLifesteal(attacker, hitDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;
        }
        return new PvPHitResult(hitDmg, hitCrit, roll);
    }

    private async Task SendPvPHitMessages(
        Player attacker, Player target, ClientConnection atkClient,
        PvPHitResult result, string skillName, string hitLabel)
    {
        var (damage, hitCrit, roll) = result;
        var targetClient = _svc.World.FindClientByPlayer(target);

        if (roll.Evaded)
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} уклонился от {hitLabel} «{skillName}».");
            var missMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "miss");
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, missMsg, target);
            if (targetClient != null)
            {
                await _svc.Hub.SendToClient(targetClient, missMsg);
                await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы уклонились от атаки {attacker.Name}.");
            }
        }
        else if (roll.Parried)
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} парировал {hitLabel} «{skillName}»!");
            var parryMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "parry");
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, parryMsg, target);
            if (targetClient != null)
            {
                await _svc.Hub.SendToClient(targetClient, parryMsg);
                await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы парировали атаку {attacker.Name}!");
            }
        }
        else
        {
            string critText = hitCrit ? " (КРИТ!)" : "";
            string blockText = roll.Blocked ? " (блок)" : "";
            await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                $"«{skillName}» — {hitLabel}: {damage} урона{critText}{blockText} {target.Name}.");

            if (roll.Blocked)
            {
                var blockMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "block");
                await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, blockMsg, target);
                if (targetClient != null)
                {
                    await _svc.Hub.SendToClient(targetClient, blockMsg);
                    await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы заблокировали атаку {attacker.Name}!");
                }
            }

            if (targetClient != null)
            {
                var hitMsg = new GameMessage
                {
                    Type = "damage",
                    Data = new { Target = "player", PlayerName = target.Name, X = target.X, Y = target.Y, Amount = damage, IsCrit = hitCrit }
                };
                await _svc.Hub.SendToClient(targetClient, hitMsg);
                await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, hitMsg, target);
                await ChatTo(targetClient, ChatChannel.Combat, "Бой",
                    $"{attacker.Name} нанёс вам {damage} урона{critText} «{skillName}».");
                await _svc.Hub.SendStatusAsync(targetClient, target);
            }
        }
    }

    // ──────── PvP death ────────

    internal async Task HandlePvPDeath(Player killer, Player victim, ClientConnection? killerClient)
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

    // ──────── Duel skill PvP: first hit ────────

    internal async Task ExecutePvPFirstHit(Player pl, Player target, ClientConnection atkClient, Skill skill, int weaponRange)
    {
        pl.Mana = Math.Max(0, pl.Mana - skill.MpCost);
        pl.LastSkillUse[skill.Id] = DateTime.UtcNow;
        pl.QueuedSkillIds.RemoveAt(0);
        await MessageHandlers.UseSkillHandler.SendSkillQueue(atkClient, pl, _svc.Hub);
        await _svc.Hub.SendToClient(atkClient, new GameMessage
        {
            Type = "skill_cooldown",
            Data = new { SkillId = skill.Id, RemainingMs = (int)(skill.CooldownMs * pl.GetSkillRankCdMult(skill.Id)), TotalMs = (int)(skill.CooldownMs * pl.GetSkillRankCdMult(skill.Id)) }
        });

        pl.Combat.LastAttackTime = DateTime.UtcNow;

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "main", TargetX = target.X, TargetY = target.Y }
        });

        var hit = await ResolvePvPHit(pl, target, atkClient, skill.DamageMultiplier, checkCrit: true);
        await SendPvPHitMessages(pl, target, atkClient, hit, skill.Name, "первого удара");

        if (target.Health <= 0)
        {
            await HandlePvPDeath(pl, target, atkClient);
            return;
        }

        pl.Combat.DuelPunishArmed = target.Combat.TargetPlayerId == pl.Id;

        await _svc.Hub.SendStatusAsync(atkClient, pl);
        var targetClient = _svc.World.FindClientByPlayer(target);
        if (targetClient != null)
            await _svc.Hub.SendToClient(targetClient, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true }
            });

        pl.Combat.PendingSkillHitsRemaining = Balance.DuelHitCount - 1;
        pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;
        pl.Combat.PendingSkillTargetId = target.Id;
        pl.Combat.PendingSkillId = skill.Id;
    }

    // ──────── Duel skill PvP: combo hit ────────

    internal async Task ExecuteComboHitPvP(Player pl, Player target, ClientConnection atkClient)
    {
        pl.Combat.PendingSkillHitsRemaining--;
        bool moreHits = pl.Combat.PendingSkillHitsRemaining > 0;
        if (!moreHits) pl.Combat.PendingSkillTargetId = null;

        if (target.Health <= 0) return;

        int hitNumber = Balance.DuelHitCount - pl.Combat.PendingSkillHitsRemaining;
        double mult = Balance.DuelFirstHitMult + (hitNumber - 1) * Balance.DuelPerHitBonus;

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "main", TargetX = target.X, TargetY = target.Y }
        });

        var hit = await ResolvePvPHit(pl, target, atkClient, mult, checkCrit: true);
        await SendPvPHitMessages(pl, target, atkClient, hit, "ЭТО ДУЭЛЬ!", "удара");

        pl.Combat.LastAttackTime = DateTime.UtcNow;

        if (target.Health <= 0)
        {
            await HandlePvPDeath(pl, target, atkClient);
            return;
        }

        if (moreHits) pl.Combat.PendingSkillLastHitTime = DateTime.UtcNow;

        await _svc.Hub.SendStatusAsync(atkClient, pl);
        var targetClient = _svc.World.FindClientByPlayer(target);
        if (targetClient != null)
            await _svc.Hub.SendToClient(targetClient, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true }
            });
    }

    // ──────── Duel skill PvP: punish on target switch ────────

    internal async Task ExecuteDuelPunishPvP(Player pl, Player target, ClientConnection atkClient)
    {
        int remainingHits = pl.Combat.PendingSkillHitsRemaining;
        pl.Combat.PendingSkillHitsRemaining = 0;
        pl.Combat.PendingSkillId = null;
        pl.Combat.PendingSkillTargetId = null;
        pl.Combat.DuelPunishArmed = false;

        if (target.Health <= 0) return;

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "main", TargetX = target.X, TargetY = target.Y }
        });

        double mult = Balance.DuelPunishBaseMult + remainingHits * Balance.DuelPunishPerMissMult;
        var hit = await ResolvePvPHit(pl, target, atkClient, mult, checkCrit: false);
        await SendPvPHitMessages(pl, target, atkClient, hit, "ЭТО ДУЭЛЬ!", "наказания");

        if (!hit.Roll.Evaded && !hit.Roll.Parried)
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
        var targetClient = _svc.World.FindClientByPlayer(target);
        if (targetClient != null)
            await _svc.Hub.SendToClient(targetClient, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = true, TargetId = pl.Id.ToString(), TargetName = pl.Name, TargetHp = pl.Health, TargetMaxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth(), IsPvP = true }
            });
    }

    // ──────── PvP chase ────────

    internal bool ChasePlayerTarget(Player pl, Player target)
    {
        int dist = Math.Abs(pl.X - target.X) + Math.Abs(pl.Y - target.Y);
        int weaponRange = pl.GetEffectiveAttackRange();
        if (dist <= weaponRange) return false;

        var zoneMap = _svc.Zones.GetOrCreateMap(pl.CurrentZoneId);
        if (!CombatService.FindChaseCell(zoneMap.Width, zoneMap.Height, zoneMap.IsObstacle,
                target.X, target.Y, pl.X, pl.Y, weaponRange,
                out int bestX, out int bestY))
            return false;
        if (pl.X == bestX && pl.Y == bestY) return true;

        var path = _svc.Pathfinding.FindPath(pl.X, pl.Y, bestX, bestY, pl.CurrentZoneId);
        if (path.Count == 0) return false;
        pl.Movement.SetPath(path);
        return true;
    }

    // ──────── PvP tick (called from CombatService.RunCombatLoop) ────────

    internal async Task<bool> RunPvPTick(Player pl)
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
        int weaponRange = pl.GetEffectiveAttackRange();

        if (dist > weaponRange)
            return false;

        var atkClient = _svc.World.FindClientByPlayer(pl);
        if (atkClient == null) return false;

        // Combo / punish via skill registry
        if (pl.Combat.PendingSkillHitsRemaining > 0)
        {
            var exec = Skills.SkillRegistry.Get(pl.Combat.PendingSkillId ?? "");
            if (exec != null && await exec.CheckPunishPvP(pl, target, atkClient, _svc.Combat))
                return true;

            if (pl.Combat.PendingSkillTargetId != target.Id)
            {
                pl.Combat.PendingSkillHitsRemaining = 0;
                pl.Combat.PendingSkillId = null;
            }
            else
            {
                double elapsed = (DateTime.UtcNow - pl.Combat.PendingSkillLastHitTime).TotalMilliseconds;
                int interval = exec?.ComboIntervalMs ?? Balance.SlashHitIntervalMs;
                if (elapsed >= interval)
                {
                    if (exec != null)
                        await exec.ExecuteComboPvP(pl, target, atkClient, _svc.Combat);
                    else
                        pl.Combat.PendingSkillHitsRemaining = 0;
                }
                else
                    await _svc.Hub.SendStatusAsync(atkClient, pl);
                return true;
            }
        }

        await _svc.Combat.ProcessInstantBuffs(pl, atkClient);
        var queuedSkill = await _svc.Combat.ProcessSkillQueue(pl, atkClient);
        if (queuedSkill != null)
        {
            var exec = Skills.SkillRegistry.Get(queuedSkill.Id);
            if (exec != null)
            {
                bool ok = await exec.ExecutePvP(pl, target, queuedSkill, atkClient, _svc.Combat, weaponRange, dist);
                if (ok) return true;
                if (pl.QueuedSkillIds.Count > 0 && pl.QueuedSkillIds[0] == queuedSkill.Id)
                {
                    pl.QueuedSkillIds.RemoveAt(0);
                    await MessageHandlers.UseSkillHandler.SendSkillQueue(atkClient, pl, _svc.Hub);
                }
            }
        }

        int attackIntervalMs = Balance.AttackIntervalMs(
            Balance.GetAttackSpeed(pl.Agility), pl.Equipment.GetWeaponSpeedModifier());
        double speedBuff = 1.0 + _svc.Debuffs.GetDebuffValue(pl, DebuffType.AttackSpeedBonus);
        attackIntervalMs = (int)(attackIntervalMs / speedBuff);
        bool mainAttackReady = (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds >= attackIntervalMs;
        bool offHandReady = pl.Equipment.IsDualWielding()
            && pl.Combat.LastAttackTime > pl.Combat.OffHandLastAttackTime
            && (DateTime.UtcNow - pl.Combat.LastAttackTime).TotalMilliseconds
                >= Math.Max(Balance.OffHandDelayMinMs, (int)(attackIntervalMs * Balance.OffHandDelayFraction));

        if (!mainAttackReady && !offHandReady) return false;

        if (mainAttackReady)
            await ExecutePvPMainAutoAttack(pl, target, atkClient, dist);
        else
            await ExecutePvPOffHandAutoAttack(pl, target, atkClient, dist);

        return true;
    }

    // ──────── PvP-атака основной рукой ────────

    private async Task ExecutePvPMainAutoAttack(Player pl, Player target, ClientConnection atkClient, int dist)
    {
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
            Data = new { PlayerName = pl.Name, Hand = attackHand, TargetX = target.X, TargetY = target.Y }
        });

        var (finalDmg, isCrit, isEvaded, isParried, isBlocked) =
            _svc.Monsters.RollAttack(pl, target, pl.RollAttackDamage(), 1.0,
                isMelee: pl.GetEffectiveAttackRange() <= 1);

        var targetClient = _svc.World.FindClientByPlayer(target);

        if (isEvaded)
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} уклонился от вашей атаки.");
            var missMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "miss");
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, missMsg, target);
            if (targetClient != null)
            {
                await _svc.Hub.SendToClient(targetClient, missMsg);
                await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы уклонились от атаки {pl.Name}.");
            }
        }
        else if (isParried)
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой", $"{target.Name} парировал вашу атаку!");
            var parryMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "parry");
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, parryMsg, target);
            if (targetClient != null)
            {
                await _svc.Hub.SendToClient(targetClient, parryMsg);
                await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы парировали атаку {pl.Name}!");
            }
        }
        else
        {
            target.Health -= finalDmg;
            await _svc.Combat.TryLifesteal(pl, finalDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;
            string critText = isCrit ? " (КРИТ!)" : "";

            if (isBlocked)
            {
                await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                    $"Вы нанесли {finalDmg} урона{critText} {target.Name} (блок). ({target.Health}/{target.MaxHealth + target.Equipment.GetBonusMaxHealth()}) HP");
                var blockMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "block");
                await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, blockMsg, target);
                if (targetClient != null)
                {
                    await _svc.Hub.SendToClient(targetClient, blockMsg);
                    await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы заблокировали атаку {pl.Name}!");
                }
            }
            else
            {
                await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                    $"Вы нанесли {finalDmg} урона{critText} {target.Name}. ({target.Health}/{target.MaxHealth + target.Equipment.GetBonusMaxHealth()}) HP");
            }

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

        if (target.Health <= 0)
        {
            await HandlePvPDeath(pl, target, atkClient);
        }
    }

    // ──────── PvP-атака второй рукой (двойное оружие) ────────

    private async Task ExecutePvPOffHandAutoAttack(Player pl, Player target, ClientConnection atkClient, int dist)
    {
        pl.Combat.OffHandLastAttackTime = DateTime.UtcNow;

        await _svc.Hub.SendToAllAsync(new GameMessage
        {
            Type = "player_attack",
            Data = new { PlayerName = pl.Name, Hand = "off", TargetX = target.X, TargetY = target.Y }
        });

        var (finalDmg, isCrit, isEvaded, isParried, isBlocked) =
            _svc.Monsters.RollAttack(pl, target, pl.RollOffHandDamage(),
                pl.GetOffHandDamageFraction(), isMelee: pl.GetEffectiveAttackRange() <= 1);

        string offWeaponName = pl.Equipment.GetOffHandWeapon()?.Name ?? "второе оружие";
        var targetClient = _svc.World.FindClientByPlayer(target);

        if (isEvaded)
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                $"{target.Name} уклонился от вашей атаки вторым оружием.");
            var missMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "miss");
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, missMsg, target);
            if (targetClient != null)
            {
                await _svc.Hub.SendToClient(targetClient, missMsg);
                await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы уклонились от атаки {pl.Name}.");
            }
        }
        else if (isParried)
        {
            await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                $"{target.Name} парировал вашу атаку вторым оружием!");
            var parryMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "parry");
            await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, parryMsg, target);
            if (targetClient != null)
            {
                await _svc.Hub.SendToClient(targetClient, parryMsg);
                await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы парировали атаку {pl.Name}!");
            }
        }
        else
        {
            target.Health -= finalDmg;
            await _svc.Combat.TryLifesteal(pl, finalDmg, dist <= 1, atkClient);
            target.LastDamagedTime = DateTime.UtcNow;
            string critText = isCrit ? " (КРИТ!)" : "";

            if (isBlocked)
            {
                await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                    $"Второе оружие ({offWeaponName}) нанесло {finalDmg} урона{critText} {target.Name} (блок).");
                var blockMsg = GameMessage.Damage("player", null, target.X, target.Y, 0, false, target.Name, result: "block");
                await _svc.Hub.SendDamageNearbyAsync(target.X, target.Y, blockMsg, target);
                if (targetClient != null)
                {
                    await _svc.Hub.SendToClient(targetClient, blockMsg);
                    await ChatTo(targetClient, ChatChannel.Combat, "Бой", $"Вы заблокировали атаку {pl.Name}!");
                }
            }
            else
            {
                await ChatTo(atkClient, ChatChannel.Combat, "Бой",
                    $"Второе оружие ({offWeaponName}) нанесло {finalDmg} урона{critText} {target.Name}. ({target.Health}/{target.MaxHealth + target.Equipment.GetBonusMaxHealth()}) HP");
            }

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
                    $"{pl.Name} нанёс вам {finalDmg} урона{critText} вторым оружием. ({target.Health}/{target.MaxHealth + target.Equipment.GetBonusMaxHealth()}) HP");
                await _svc.Hub.SendStatusAsync(targetClient, target);
            }
        }

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

        if (target.Health <= 0)
        {
            await HandlePvPDeath(pl, target, atkClient);
        }
    }
}
