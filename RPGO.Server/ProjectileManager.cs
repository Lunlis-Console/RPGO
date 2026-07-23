using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Server.Network;

namespace RPGGame.Server;

public class ProjectileManager
{
    private readonly GameWorld _world;
    private INetworkHub? _hub;
    private readonly List<Projectile> _projectiles = new();
    private readonly object _lock = new();

    public ProjectileManager(GameWorld world)
    {
        _world = world;
    }

    public void SetHub(INetworkHub hub) => _hub = hub;

    public Projectile Spawn(
        Player owner, Monster target,
        string visualType, int damage, bool isCrit)
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
            SpawnTime = DateTime.UtcNow
        };
        lock (_lock) { _projectiles.Add(proj); }
        return proj;
    }

    public async Task RunTick()
    {
        if (_hub == null) return;
        var svc = Program.Services;
        List<Projectile> snapshot;
        lock (_lock) { snapshot = _projectiles.ToList(); }

        foreach (var proj in snapshot)
        {
            double elapsed = (DateTime.UtcNow - proj.SpawnTime).TotalMilliseconds;
            if (elapsed < Balance.ProjectileFlightMs) continue;

            lock (_lock) { _projectiles.Remove(proj); }

            var monster = svc.Monsters.FindMonsterById(proj.TargetMonsterId);
            if (monster == null || monster.Health <= 0) continue;

            Player? owner = null;
            _world.TryGetPlayer(proj.OwnerId, out owner);
            if (owner == null || owner.Health <= 0) continue;

            monster.Health -= proj.Damage;
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker[proj.OwnerId] =
                monster.DamageTracker.GetValueOrDefault(proj.OwnerId) + proj.Damage;

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
                    await Program.ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{critText}");
                    continue;
                }

                var killDmgMsg = new GameMessage
                {
                    Type = "damage",
                    Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = shownDmg, IsCrit = proj.IsCrit }
                };
                await svc.KillService.ResolveMonsterKill(owner, monster, proj.Damage, true, killDmgMsg, isProjectile: true);
            }
            else
            {
                await Program.ChatTo(client, ChatChannel.Combat, "Бой",
                    $"Вы нанесли {proj.Damage} урона{critText} {monster.Name}.");

                var dmgMsg = new GameMessage
                {
                    Type = "damage",
                    Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = proj.Damage, IsCrit = proj.IsCrit }
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
}
