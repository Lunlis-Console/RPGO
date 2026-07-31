using RPGGame.Server.Services;
using RPGGame.Shared.Models;

namespace RPGGame.Server;

/// <summary>
/// Тонкий координатор фоновых циклов. Вся логика вынесена в CombatService / InteractionService.
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
        var ct = _cts.Token;
        Task.Run(() => _svc.Combat.RunCombatLoop(ct), ct);
        Task.Run(() => _svc.Combat.RunMonsterAttackLoop(ct), ct);
        Task.Run(() => _svc.Combat.RunDeathTimerLoop(ct), ct);
        Task.Run(() => _svc.Combat.RunHazardTickLoop(ct), ct);
        Task.Run(() => _svc.Interactions.RunMovePathLoop(ct), ct);
        Task.Run(() => RunMonsterWanderLoop(ct), ct);
        Task.Run(() => RunRegenLoop(ct), ct);
        Task.Run(() => RunDebuffTickLoop(ct), ct);
        Task.Run(() => RunCorpseCleanupLoop(ct), ct);
        Task.Run(() => _svc.Projectiles.RunTick(ct), ct);
        Task.Run(() => RunSessionCleanupLoop(ct), ct);
        Task.Run(() => RunInstanceTickLoop(ct), ct);
    }

    public void Stop()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task RunMonsterWanderLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Balance.LoopMonsterWanderMs / 3, ct);
                bool moved = _svc.Monsters.WanderStep();
                if (moved)
                    await _svc.Hub.BroadcastMapAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error("Ошибка цикла блуждания монстров", ex); }
        }
    }

    private async Task RunRegenLoop(CancellationToken ct)
    {
        const int inCombatDelayMs = Balance.PlayerRegenInCombatDelayMs;
        const int outOfCombatHeal = Balance.PlayerRegenOutOfCombatHeal;
        const int outOfCombatTickMs = Balance.PlayerRegenOutOfCombatTickMs;
        const double inCombatFraction = Balance.PlayerRegenInCombatFraction;
        const int inCombatTickMs = Balance.PlayerRegenInCombatTickMs;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(outOfCombatTickMs, ct);
                var now = DateTime.UtcNow;

                foreach (var pl in _svc.World.GetPlayersSnapshot())
                {
                    if (pl.IsDead) continue;

                    bool plInCombat = (now - pl.LastDamagedTime).TotalMilliseconds < inCombatDelayMs;
                    int tick = plInCombat ? inCombatTickMs : outOfCombatTickMs;

                    if ((now - pl.LastRegenTime).TotalMilliseconds >= tick)
                    {
                        int maxHp = pl.MaxHealth + pl.Equipment.GetBonusMaxHealth();
                        int heal = 0;
                        if (pl.Health < maxHp)
                        {
                            heal = plInCombat
                                ? Math.Max(Balance.PlayerRegenMinHeal, (int)(maxHp * inCombatFraction))
                                : outOfCombatHeal;
                            pl.Health = Math.Min(maxHp, pl.Health + heal);
                        }

                        int maxMana = pl.MaxMana;
                        int manaTick = 0;
                        if (pl.Mana < maxMana)
                        {
                            manaTick = plInCombat
                                ? Math.Max(Balance.ManaRegenMin, (int)(maxMana * Balance.ManaRegenInCombatFraction))
                                : Balance.ManaRegenOutOfCombat;
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
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error("Ошибка цикла регенерации", ex); }
        }
    }

    private async Task RunDebuffTickLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Balance.DebuffTickMs, ct);
                foreach (var pl in _svc.World.GetPlayersSnapshot())
                {
                    if (pl.IsDead) continue;
                    bool dualWieldChanged = _svc.Debuffs.CheckDualWieldBuff(pl);
                    bool hasDebuffs = pl.ActiveDebuffs.Count > 0;
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
                    if (mon.ActiveDebuffs.Count > 0)
                    {
                        _svc.Debuffs.TickDebuffs(mon);
                        await _svc.Combat.SendTargetDebuffUpdateAsync(mon);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error("Ошибка цикла дебаффов", ex); }
        }
    }

    private async Task RunCorpseCleanupLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(30_000, ct);
                _svc.Corpses.CleanupExpired();
                _svc.Monsters.SpawnOneMonsterPublic();
                await _svc.Hub.BroadcastMapAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error("Ошибка цикла очистки трупов", ex); }
        }
    }

    private static async Task RunSessionCleanupLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(60_000, ct);
                SessionManager.Cleanup();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error("Ошибка цикла очистки сессий", ex); }
        }
    }

    private async Task RunInstanceTickLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct);
                await _svc.Instances.TickAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error("Ошибка цикла инстансов", ex); }
        }
    }
}
