using System.Diagnostics;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server;

/// <summary>
/// Единый game loop: одна задача с Stopwatch, диспатч по интервалам.
/// Заменяет 12 фоновых циклов на один.
/// </summary>
public class GameServerHost
{
    private readonly GameServices _svc;
    private readonly CancellationTokenSource _cts = new();

    public GameServerHost(GameServices svc)
    {
        _svc = svc;
    }

    public void StartAsync()
    {
        Task.Run(() => RunUnifiedLoop(_cts.Token), _cts.Token);
    }

    public void Stop()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task RunUnifiedLoop(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long lastProjectile = 0;
        long lastCombat = 0, lastMovePath = 0;
        long lastMonsterAttack = 0, lastDeathTimer = 0, lastMonsterWander = 0;
        long lastHazard = 0, lastDebuff = 0, lastInstance = 0;
        long lastRegen = 0, lastCorpse = 0, lastSession = 0;
        long lastRespawn = 0;
        long lastEntityState = 0;
        long lastDisconnectSweep = 0;

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { break; }

            long now = sw.ElapsedMilliseconds;

            // 50ms — projectiles
            if (now - lastProjectile >= Balance.ProjectileTickMs)
            {
                lastProjectile = now;
                try { await _svc.Projectiles.Tick(); }
                catch (Exception ex) { Log.Error("[Tick] Projectile", ex); }
            }

            // 200ms — combat + movement
            if (now - lastCombat >= Balance.LoopCombatMs)
            {
                lastCombat = now;
                try { await _svc.Combat.CombatTick(); }
                catch (Exception ex) { Log.Error("[Tick] Combat", ex); }
            }
            if (now - lastMovePath >= Balance.LoopMovePathMs)
            {
                lastMovePath = now;
                try { await _svc.Interactions.Tick(); }
                catch (Exception ex) { Log.Error("[Tick] MovePath", ex); }
            }

            // 50ms — monster attacks, 500ms — death timers, 50ms — monster wander
            if (now - lastMonsterAttack >= Balance.LoopMonsterAttackMs)
            {
                lastMonsterAttack = now;
                try { await _svc.MonsterAttacks.MonsterAttackTick(); }
                catch (Exception ex) { Log.Error("[Tick] MonsterAttack", ex); }
            }
            if (now - lastDeathTimer >= Balance.LoopDeathTimerMs)
            {
                lastDeathTimer = now;
                try { await _svc.PlayerDeath.DeathTimerTick(); }
                catch (Exception ex) { Log.Error("[Tick] DeathTimer", ex); }
            }
            if (now - lastMonsterWander >= Balance.LoopMonsterWanderMs)
            {
                lastMonsterWander = now;
                try { _svc.Monsters.WanderStep(); }
                catch (Exception ex) { Log.Error("[Tick] MonsterWander", ex); }
            }

            // 50ms — лёгкая рассылка позиций сущностей (Вариант 4)
            if (now - lastEntityState >= 50)
            {
                lastEntityState = now;
                try { await _svc.Hub.BroadcastEntityStatesAsync(); }
                catch (Exception ex) { Log.Error("[Tick] EntityState", ex); }
            }

            // 1000ms — hazards, debuffs, instances
            if (now - lastHazard >= Balance.DebuffTickMs)
            {
                lastHazard = now;
                try { await _svc.Hazard.Tick(); }
                catch (Exception ex) { Log.Error("[Tick] Hazard", ex); }
            }
            if (now - lastDebuff >= Balance.DebuffTickMs)
            {
                lastDebuff = now;
                try { await DebuffTick(); }
                catch (Exception ex) { Log.Error("[Tick] Debuff", ex); }
            }
            if (now - lastInstance >= Balance.LoopInstanceMs)
            {
                lastInstance = now;
                try { await _svc.Instances.TickAsync(); }
                catch (Exception ex) { Log.Error("[Tick] Instance", ex); }
            }
            if (now - lastRespawn >= Balance.LoopRespawnMs)
            {
                lastRespawn = now;
                try { _svc.Monsters.TickRespawns(); }
                catch (Exception ex) { Log.Error("[Tick] MonsterRespawn", ex); }
                try { _svc.Collectibles.TickRespawns(); }
                catch (Exception ex) { Log.Error("[Tick] CollectibleRespawn", ex); }
            }

            // 5000ms — regen
            if (now - lastRegen >= Balance.PlayerRegenOutOfCombatTickMs)
            {
                lastRegen = now;
                try { await RegenTick(); }
                catch (Exception ex) { Log.Error("[Tick] Regen", ex); }
            }

            // 30s — corpse cleanup
            if (now - lastCorpse >= Balance.LoopCorpseCleanupMs)
            {
                lastCorpse = now;
                try
                {
                    _svc.Corpses.CleanupExpired();
                    await _svc.Hub.BroadcastMapAsync();
                }
                catch (Exception ex) { Log.Error("[Tick] CorpseCleanup", ex); }
            }

            // 60s — session cleanup
            if (now - lastSession >= Balance.LoopSessionCleanupMs)
            {
                lastSession = now;
                try { SessionManager.Cleanup(); }
                catch (Exception ex) { Log.Error("[Tick] SessionCleanup", ex); }
            }

            // 2s — finalize pending disconnects whose reconnect window expired
            if (now - lastDisconnectSweep >= Balance.LoopDisconnectSweepMs)
            {
                lastDisconnectSweep = now;
                try { await FinalizeExpiredDisconnectsAsync(); }
                catch (Exception ex) { Log.Error("[Tick] DisconnectSweep", ex); }
            }
        }
    }

    private async Task FinalizeExpiredDisconnectsAsync()
    {
        foreach (var player in _svc.World.TakeExpiredPendingReconnects())
        {
            await FinalizeDisconnectAsync(player);
        }
    }

    private async Task FinalizeDisconnectAsync(Player player)
    {
        // Все операции привязаны к идентичности старого объекта (Id/ссылка),
        // поэтому при повторном входе нового игрока с тем же именем не затрагивают его.
        var tradeSession = _svc.Trade.GetSession(player.Id);
        if (tradeSession != null) _svc.Trade.CancelSession(tradeSession, "отключение клиента");
        player.IsTrading = false;

        await _svc.Party.LeavePartyAsync(player);

        _svc.Instances.RemovePlayer(player);

        _svc.World.RemovePlayer(player);
        Log.Info($"Игрок {player.Name} покинул игру (окно переподключения истекло)");
        await _svc.Hub.BroadcastMapAsync();

        _svc.Persistence.EnqueueSave(player);
    }

    private async Task DebuffTick()
    {
        foreach (var pl in _svc.World.GetPlayersSnapshot())
        {
            if (pl.IsDead) continue;
            bool dualWieldChanged = _svc.Debuffs.CheckDualWieldBuff(pl);
            bool hasDebuffs = pl.GetDebuffsSnapshot().Count > 0;
            if (hasDebuffs)
                _svc.Debuffs.TickDebuffs(pl);
            if (dualWieldChanged || hasDebuffs)
            {
                var conn = _svc.World.FindClientByPlayer(pl);
                if (conn != null) await _svc.Hub.SendStatusAsync(conn, pl);
            }
        }
        foreach (var mon in _svc.World.GetMonstersSnapshot())
        {
            if (mon.GetDebuffsSnapshot().Count > 0)
            {
                _svc.Debuffs.TickDebuffs(mon);
                await _svc.Combat.SendTargetDebuffUpdateAsync(mon);
            }
        }
    }

    private async Task RegenTick()
    {
        const int inCombatDelayMs = Balance.PlayerRegenInCombatDelayMs;
        const int outOfCombatHeal = Balance.PlayerRegenOutOfCombatHeal;
        const double inCombatFraction = Balance.PlayerRegenInCombatFraction;
        const int inCombatTickMs = Balance.PlayerRegenInCombatTickMs;
        const int outOfCombatTickMs = Balance.PlayerRegenOutOfCombatTickMs;

        var now = DateTime.UtcNow;

        foreach (var pl in _svc.World.GetPlayersSnapshot())
        {
            if (pl.IsDead) continue;

            bool plInCombat = (now - pl.LastDamagedTime).TotalMilliseconds < inCombatDelayMs;
            int tick = plInCombat ? inCombatTickMs : outOfCombatTickMs;

            if ((now - pl.LastRegenTime).TotalMilliseconds >= tick)
            {
                int maxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth();
                double hpRegenMult = 1.0 + pl.GetHealthRegenPercent() / 100.0;
                double mpRegenMult = 1.0 + pl.GetManaRegenPercent() / 100.0;
                int heal = 0;
                if (pl.Health < maxHp)
                {
                    heal = plInCombat
                        ? Math.Max(Balance.PlayerRegenMinHeal, (int)(maxHp * inCombatFraction * hpRegenMult))
                        : (int)(outOfCombatHeal * hpRegenMult);
                    pl.Health = Math.Min(maxHp, pl.Health + heal);
                }

                int maxMana = pl.MaxMana + pl.Equipment.GetBonusMaxMana();
                int manaTick = 0;
                if (pl.Mana < maxMana)
                {
                    manaTick = plInCombat
                        ? Math.Max(Balance.ManaRegenMin, (int)(maxMana * Balance.ManaRegenInCombatFraction * mpRegenMult))
                        : (int)(Balance.ManaRegenOutOfCombat * mpRegenMult);
                    pl.Mana = Math.Min(maxMana, pl.Mana + manaTick);
                }

                pl.LastRegenTime = now;

                var conn = _svc.World.FindClientByPlayer(pl);
                if (conn != null)
                {
                    if (heal > 0)
                    {
                        var healMsg = new GameMessage
                        {
                            Type = "heal",
                            Data = new { Target = "player", PlayerName = pl.Name, X = pl.X, Y = pl.Y, Amount = heal }
                        };
                        await _svc.Hub.SendToClient(conn, healMsg);
                        await _svc.Hub.SendDamageNearbyAsync(pl.X, pl.Y, healMsg, pl);
                    }
                    if (manaTick > 0)
                    {
                        var manaMsg = new GameMessage
                        {
                            Type = "mana_regen",
                            Data = new { X = pl.X, Y = pl.Y, Amount = manaTick }
                        };
                        await _svc.Hub.SendToClient(conn, manaMsg);
                        await _svc.Hub.SendDamageNearbyAsync(pl.X, pl.Y, manaMsg, pl);
                    }
                    await _svc.Hub.SendStatusAsync(conn, pl);
                }
                await _svc.Party.SendUpdateForAsync(pl);
            }
        }

        _svc.Monsters.RegenStep();
        await _svc.Hub.BroadcastMapAsync();
    }
}
