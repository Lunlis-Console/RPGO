using RPGGame.Shared.Models;

namespace RPGGame.Server;

/// <summary>
/// Тонкий координатор фоновых циклов. Вся логика вынесена в CombatService / InteractionService.
/// </summary>
public class GameServerHost
{
    private readonly GameServices _svc;

    public GameServerHost(GameServices svc)
    {
        _svc = svc;
    }

    public void StartAsync()
    {
        Task.Run(() => _svc.Combat.RunCombatLoop());
        Task.Run(() => _svc.Combat.RunMonsterAttackLoop());
        Task.Run(() => _svc.Combat.RunDeathTimerLoop());
        Task.Run(() => _svc.Interactions.RunMovePathLoop());
        Task.Run(RunMonsterWanderLoop);
        Task.Run(RunRegenLoop);
        Task.Run(RunDebuffTickLoop);
        Task.Run(RunCorpseCleanupLoop);
        Task.Run(() => _svc.Projectiles.RunTick());
    }

    private async Task RunMonsterWanderLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(Balance.LoopMonsterWanderMs);
                _svc.Monsters.WanderStep();
                await _svc.Hub.BroadcastMapAsync();
            }
            catch (Exception ex) { Log.Error("Ошибка цикла блуждания монстров", ex); }
        }
    }

    private async Task RunRegenLoop()
    {
        const int inCombatDelayMs = Balance.PlayerRegenInCombatDelayMs;
        const int outOfCombatHeal = Balance.PlayerRegenOutOfCombatHeal;
        const int outOfCombatTickMs = Balance.PlayerRegenOutOfCombatTickMs;
        const double inCombatFraction = Balance.PlayerRegenInCombatFraction;
        const int inCombatTickMs = Balance.PlayerRegenInCombatTickMs;

        while (true)
        {
            try
            {
                await Task.Delay(outOfCombatTickMs);
                var now = DateTime.UtcNow;

                foreach (var pl in _svc.World.GetPlayersSnapshot())
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

                        int maxMana = pl.MaxMana;
                        if (pl.Mana < maxMana)
                        {
                            int manaTick = plInCombat
                                ? Math.Max(Balance.ManaRegenMin, (int)(maxMana * Balance.ManaRegenInCombatFraction))
                                : Balance.ManaRegenOutOfCombat;
                            pl.Mana = Math.Min(maxMana, pl.Mana + manaTick);
                        }

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
                            await _svc.Hub.SendStatusAsync(conn, pl);
                        }
                        await _svc.Party.SendUpdateForAsync(pl);
                    }
                }

                _svc.Monsters.RegenStep();
                await _svc.Hub.BroadcastMapAsync();
            }
            catch (Exception ex) { Log.Error("Ошибка цикла регенерации", ex); }
        }
    }

    private async Task RunDebuffTickLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(Balance.DebuffTickMs);
                foreach (var pl in _svc.World.GetPlayersSnapshot())
                {
                    if (pl.IsDead) continue;
                    if (pl.ActiveDebuffs.Count > 0)
                    {
                        _svc.Debuffs.TickDebuffs(pl);
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
            catch (Exception ex) { Log.Error("Ошибка цикла дебаффов", ex); }
        }
    }

    private async Task RunCorpseCleanupLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(30_000);
                _svc.Corpses.CleanupExpired();
                _svc.Monsters.SpawnOneMonsterPublic();
                await _svc.Hub.BroadcastMapAsync();
            }
            catch (Exception ex) { Log.Error("Ошибка цикла очистки трупов", ex); }
        }
    }
}
