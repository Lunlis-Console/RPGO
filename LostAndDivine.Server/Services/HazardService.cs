using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server;

/// <summary>
/// Ground hazard logic: tick loop, applying effects to monsters and players.
/// Extracted from CombatService for separation of concerns.
/// </summary>
public class HazardService
{
    private readonly IGameServices _svc;

    internal MonsterManager Monsters => _svc.Monsters;
    internal KillService KillService => _svc.KillService;
    internal DebuffManager Debuffs => _svc.Debuffs;
    internal GameWorld World => _svc.World;
    internal INetworkHub Hub => _svc.Hub;

    public HazardService(IGameServices svc)
    {
        _svc = svc;
    }

    public async Task Tick()
    {
        _svc.World.RemoveExpiredHazards();
        foreach (var h in _svc.World.GetHazardsSnapshot())
        {
            foreach (var m in _svc.Monsters.GetAllMonstersIncludingInstances())
            {
                if (m.Health <= 0 || m.X != h.X || m.Y != h.Y || m.ZoneId != h.ZoneId) continue;
                await ApplyHazardToMonster(h, m);
            }
            foreach (var p in _svc.World.GetPlayersSnapshot())
            {
                if (p.IsDead || p.X != h.X || p.Y != h.Y || p.CurrentZoneId != h.ZoneId) continue;
                if (p.Id == h.OwnerId) continue;
                ApplyHazardToPlayer(h, p);
            }
        }
    }

    private async Task ApplyHazardToMonster(GroundHazard h, Monster m)
    {
        switch (h.Kind)
        {
            case HazardKind.Smoke:
                if (!h.AffectedIds.Contains(m.Id))
                {
                    h.AffectedIds.Add(m.Id);
                    var deb = ActiveDebuff.Create(DebuffType.AccuracyReduction, Balance.SmokeAccuracyReduction,
                        (int)(h.ExpiresAt - DateTime.UtcNow).TotalMilliseconds, "trap", "Дым", "−40% точности.");
                    _svc.Debuffs.ApplyDebuff(m, deb);
                    await _svc.Combat.SendTargetDebuffUpdateAsync(m);
                }
                break;
            case HazardKind.Snare:
                if (!h.AffectedIds.Contains(m.Id))
                {
                    h.AffectedIds.Add(m.Id);
                    var root = ActiveDebuff.Create(DebuffType.Root, 0,
                        (int)(h.ExpiresAt - DateTime.UtcNow).TotalMilliseconds, "trap", "Капкан", "Обездвижен.");
                    _svc.Debuffs.ApplyDebuff(m, root);
                    await _svc.Combat.SendTargetDebuffUpdateAsync(m);
                }
                break;
            case HazardKind.Acid:
                if (h.DotDamagePerTick > 0)
                {
                    m.Health -= h.DotDamagePerTick;
                    m.LastDamagedTime = DateTime.UtcNow;
                    if (!_svc.Debuffs.HasDebuff(m, DebuffType.Slow))
                    {
                        var slow = ActiveDebuff.Create(DebuffType.Slow, Balance.AcidSlowValue,
                            Balance.TrapDurationMs, "trap", "Кислота", "−10% скорости.");
                        _svc.Debuffs.ApplyDebuff(m, slow);
                    }
                    if (!_svc.Debuffs.HasDebuff(m, DebuffType.Dot))
                    {
                        var dot = ActiveDebuff.Create(DebuffType.Dot, h.DotDamagePerTick,
                            Balance.TrapDurationMs, "trap", "Кислота", $"{h.DotDamagePerTick} урона/тик.");
                        _svc.Debuffs.ApplyDebuff(m, dot);
                    }
                    await _svc.Combat.SendTargetDebuffUpdateAsync(m);
                    var owner = _svc.World.GetPlayersSnapshot().FirstOrDefault(p => p.Id == h.OwnerId);
                    if (owner != null)
                    {
                        m.DamageTracker.AddOrUpdate(owner.Id, h.DotDamagePerTick, (k, old) => old + h.DotDamagePerTick);
                        var client = _svc.World.FindClientByPlayer(owner);
                        if (client != null)
                            await _svc.Combat.SendDmgToMonster(client, m, h.DotDamagePerTick, false, "main", owner, isSkill: true);
                    }
                    if (m.Health <= 0 && !m.IsMannequin && owner != null)
                        await _svc.KillService.ResolveMonsterKill(owner, m, h.DotDamagePerTick, true, null);
                }
                break;
        }
    }

    private void ApplyHazardToPlayer(GroundHazard h, Player p)
    {
        switch (h.Kind)
        {
            case HazardKind.Smoke:
                if (!h.AffectedIds.Contains(p.Id))
                {
                    h.AffectedIds.Add(p.Id);
                    _svc.Debuffs.ApplyDebuff(p, ActiveDebuff.Create(DebuffType.AccuracyReduction, Balance.SmokeAccuracyReduction,
                        (int)(h.ExpiresAt - DateTime.UtcNow).TotalMilliseconds, "trap", "Дым", "−40% точности."));
                }
                break;
            case HazardKind.Snare:
                if (!h.AffectedIds.Contains(p.Id))
                {
                    h.AffectedIds.Add(p.Id);
                    _svc.Debuffs.ApplyDebuff(p, ActiveDebuff.Create(DebuffType.Root, 0,
                        (int)(h.ExpiresAt - DateTime.UtcNow).TotalMilliseconds, "trap", "Капкан", "Обездвижен."));
                }
                break;
            case HazardKind.Acid:
                if (h.DotDamagePerTick > 0)
                {
                    lock (p.Sync)
                    {
                        p.Health -= h.DotDamagePerTick;
                        p.LastDamagedTime = DateTime.UtcNow;
                    }
                    if (!_svc.Debuffs.HasDebuff(p, DebuffType.Slow))
                        _svc.Debuffs.ApplyDebuff(p, ActiveDebuff.Create(DebuffType.Slow, Balance.AcidSlowValue,
                            Balance.TrapDurationMs, "trap", "Кислота", "−10% скорости."));
                    if (!_svc.Debuffs.HasDebuff(p, DebuffType.Dot))
                        _svc.Debuffs.ApplyDebuff(p, ActiveDebuff.Create(DebuffType.Dot, h.DotDamagePerTick,
                            Balance.TrapDurationMs, "trap", "Кислота", $"{h.DotDamagePerTick} урона/тик."));
                }
                break;
        }
    }
}
