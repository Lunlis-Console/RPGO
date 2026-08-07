using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.Server.Network;

namespace LostAndDivine.Server;

public class ProjectileManager
{
    private readonly GameWorld _world;
    private INetworkHub? _hub;
    private GameServices _svc = null!;
    private readonly List<Projectile> _projectiles = new();
    private readonly object _lock = new();

    public ProjectileManager(GameWorld world)
    {
        _world = world;
    }

    public void SetHub(INetworkHub hub) => _hub = hub;

    public void SetServices(GameServices svc) => _svc = svc;

    public Projectile Spawn(
        Player owner, Monster target,
        string visualType, int damage, bool isCrit, string attackHand = "main",
        string? skillName = null)
    {
        var proj = new Projectile
        {
            StartX = owner.X,
            StartY = owner.Y,
            CurrentX = owner.X,
            CurrentY = owner.Y,
            TargetX = target.X,
            TargetY = target.Y,
            VisualType = visualType,
            Damage = damage,
            IsCrit = isCrit,
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            TargetMonsterId = target.Id,
            AttackHand = attackHand,
            SkillName = skillName,
            SpawnTime = DateTime.UtcNow
        };
        lock (_lock) { _projectiles.Add(proj); }
        return proj;
    }

    public async Task RunTick(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Balance.ProjectileTickMs, ct);
                await Tick();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Error("Ошибка цикла снарядов", ex);
            }
        }
    }

    public async Task Tick()
    {
        if (_hub == null) return;
        var svc = _svc;
        List<Projectile> snapshot;
        lock (_lock) { snapshot = _projectiles.ToList(); }

        foreach (var proj in snapshot)
        {
            double elapsed = (DateTime.UtcNow - proj.SpawnTime).TotalMilliseconds;
            if (elapsed < Balance.ProjectileFlightMs) continue;

            lock (_lock) { _projectiles.Remove(proj); }

            var monster = svc.Monsters.FindMonsterById(proj.TargetMonsterId);
            if (monster == null || monster.Health <= 0) continue;

            if (monster.ReturningToSpawn) continue;

            Player? owner = null;
            _world.TryGetPlayer(proj.OwnerId, out owner);
            if (owner == null || owner.Health <= 0) continue;

            monster.Health -= proj.Damage;
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker.AddOrUpdate(proj.OwnerId, proj.Damage, (k, old) => old + proj.Damage);

            var client = _world.FindClientByPlayer(owner);
            if (client == null) continue;

            string critText = proj.IsCrit ? " (КРИТ!)" : "";
            int shownDmg = Math.Max(0, monster.Health + proj.Damage);

            if (monster.Health <= 0)
            {
                if (monster.IsMannequin)
                {
                    monster.Health = monster.MaxHealth;
                    monster.LastDamagedTime = DateTime.UtcNow;
                    await _hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                    await _svc.ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{critText}");
                    continue;
                }

                var killDmgMsg = new GameMessage
                {
                    Type = "damage",
                    Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = shownDmg, IsCrit = proj.IsCrit, Hand = "main", IsSkill = proj.SkillName != null }
                };
                await svc.KillService.ResolveMonsterKill(owner, monster, proj.Damage, true, killDmgMsg, isProjectile: true);
            }
            else
            {
                string skillPrefix = proj.SkillName != null ? $"«{proj.SkillName}»: " : "";
                await _svc.ChatTo(client, ChatChannel.Combat, "Бой",
                    $"{skillPrefix}Вы нанесли {proj.Damage} урона{critText} {monster.Name}.");

                var dmgMsg = new GameMessage
                {
                    Type = "damage",
                    Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = proj.Damage, IsCrit = proj.IsCrit, Hand = proj.AttackHand, IsSkill = proj.SkillName != null }
                };
                await _hub.SendToClient(client, dmgMsg);
                await _hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, owner);
                await _hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                await _hub.SendToClient(client, new GameMessage
                {
                    Type = "combat_state",
                    Data = new { InCombat = true, TargetId = monster.Id.ToString(), TargetName = monster.Name, TargetHp = monster.Health, TargetMaxHp = monster.MaxHealth }
                });
            }

            var hitMsg = new GameMessage
            {
                Type = "projectile_hit",
                Data = new { Id = proj.Id.ToString(), X = monster.X, Y = monster.Y }
            };
            await _hub.SendToClient(client, hitMsg);
        }

        if (snapshot.Count > 0)
            await _hub.BroadcastMapAsync();
    }

    public async Task BroadcastSpawn(Projectile proj)
    {
        if (_hub == null) return;
        var msg = new GameMessage
        {
            Type = "projectile_spawn",
            Data = new
            {
                Id = proj.Id.ToString(),
                StartX = proj.StartX,
                StartY = proj.StartY,
                TargetX = proj.TargetX,
                TargetY = proj.TargetY,
                VisualType = proj.VisualType,
                FlightMs = Balance.ProjectileFlightMs
            }
        };
        await _hub.SendToAllAsync(msg);
    }

    /// <summary>Спавн визуальной стрелы без цели (не наносит урон).</summary>
    public async Task BroadcastArrowVisual(double startX, double startY, double targetX, double targetY, string visualType = "arrow")
    {
        if (_hub == null) return;
        string id = Guid.NewGuid().ToString("N")[..8];
        var spawnMsg = new GameMessage
        {
            Type = "projectile_spawn",
            Data = new
            {
                Id = id,
                StartX = startX,
                StartY = startY,
                TargetX = targetX,
                TargetY = targetY,
                VisualType = visualType,
                FlightMs = Balance.ProjectileFlightMs
            }
        };
        await _hub.SendToAllAsync(spawnMsg);

        _ = Task.Run(async () =>
        {
            await Task.Delay(Balance.ProjectileFlightMs + 50);
            if (_hub != null)
            {
                var hitMsg = new GameMessage
                {
                    Type = "projectile_hit",
                    Data = new { Id = id, X = targetX, Y = targetY }
                };
                await _hub.SendToAllAsync(hitMsg);
            }
        });
    }
}
