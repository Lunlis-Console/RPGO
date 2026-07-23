using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server;

public static class ProjectileManager
{
    private static readonly List<Projectile> _projectiles = new();
    private static readonly object _lock = new();

    public static Projectile Spawn(
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

    public static async Task RunTick()
    {
        List<Projectile> snapshot;
        lock (_lock) { snapshot = _projectiles.ToList(); }

        foreach (var proj in snapshot)
        {
            double elapsed = (DateTime.UtcNow - proj.SpawnTime).TotalMilliseconds;
            if (elapsed < Balance.ProjectileFlightMs) continue;

            lock (_lock) { _projectiles.Remove(proj); }

            var monster = MonsterManager.FindMonsterById(proj.TargetMonsterId);
            if (monster == null || monster.Health <= 0) continue;

            Player? owner = null;
            Program.World.TryGetPlayer(proj.OwnerId, out owner);
            if (owner == null || owner.Health <= 0) continue;

            monster.Health -= proj.Damage;
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker[proj.OwnerId] =
                monster.DamageTracker.GetValueOrDefault(proj.OwnerId) + proj.Damage;

            var client = Program.World.FindClientByPlayer(owner);
            if (client == null) continue;

            string critText = proj.IsCrit ? " (КРИТ!)" : "";

            if (monster.Health <= 0)
            {
                if (monster.IsMannequin)
                {
                    monster.Health = monster.MaxHealth;
                    monster.LastDamagedTime = DateTime.UtcNow;
                    await Program.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                    await Program.ChatTo(client, ChatChannel.Combat, "Бой", $"Манекен восстановил все HP!{critText}");
                    continue;
                }

                owner.Combat.Cancel();
                owner.Combat.OffHandLastAttackTime = DateTime.MinValue;
                int shownDmg = Math.Max(0, monster.Health + proj.Damage);

                Log.Info($"{owner.Name} убил {monster.Name} снарядом!{critText}");
                await Program.ChatTo(client, ChatChannel.Combat, "Бой",
                    $"Вы нанесли {shownDmg} урона{critText} и убили {monster.Name}!");

                var dmgMsg = new GameMessage
                {
                    Type = "damage",
                    Data = new { Target = "monster", MonsterId = monster.Id.ToString(), X = monster.X, Y = monster.Y, Amount = shownDmg, IsCrit = proj.IsCrit }
                };
                await Program.Hub.SendToClient(client, dmgMsg);
                await Program.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, owner);
                await Program.Hub.SendToClient(client, GameMessage.ResetCombat());

                // XP / loot (solo)
                owner.Experience += monster.XpReward;
                if (owner.TryLevelUp()) Log.Info($"{owner.Name} повысил уровень до {owner.Level}!");

                var loot = LootManager.RollLoot(monster.TemplateId);
                var playerLoot = new Dictionary<Guid, CorpsePlayerLoot>
                {
                    [owner.Id] = new CorpsePlayerLoot
                    {
                        PlayerName = owner.Name,
                        Gold = monster.GoldReward,
                        Items = loot,
                        DamagePercent = 100
                    }
                };
                CorpseManager.CreateCorpse(monster, new List<Item>(), playerLoot);
                MonsterManager.RemoveMonster(monster);

                int totalItems = loot.Count;
                if (totalItems > 0 || monster.GoldReward > 0)
                    await Program.ChatTo(client, ChatChannel.System, "Система",
                        $"Тело {monster.Name} осталось на земле. Нажмите, чтобы забрать дроп ({totalItems} предм., {monster.GoldReward} зол.).");
                else
                    await Program.ChatTo(client, ChatChannel.System, "Система",
                        $"Тело {monster.Name} осталось на земле. Дропа нет.");

                var questResults = QuestManager.IncrementKillProgress(owner, monster.TemplateId);
                foreach (var (title, current, target, completed) in questResults)
                {
                    string msg = completed
                        ? $"[Задание] {title}: {current}/{target} — задание выполнено! Вернитесь на доску заданий, чтобы сдать."
                        : $"[Задание] {title}: {current}/{target}";
                    await Program.ChatTo(client, ChatChannel.System, "Система", msg);
                }
                await Program.Hub.SendQuestLog(client, owner);
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
                await Program.Hub.SendToClient(client, dmgMsg);
                await Program.Hub.SendDamageNearbyAsync(monster.X, monster.Y, dmgMsg, owner);
                await Program.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
                await Program.Hub.SendToClient(client, new GameMessage
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
            await Program.Hub.SendToClient(client, hitMsg);
        }
    }

    public static async Task BroadcastSpawn(Projectile proj)
    {
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
        await Program.Hub.SendToAllAsync(msg);
    }
}
