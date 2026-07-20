using RPGGame.Shared.Models;

namespace RPGGame.Server;

/// <summary>
/// Тонкая обёртка над GameWorld для логики монстров.
/// Состояние (список монстров, шаблоны, очередь атак) хранится в GameWorld.
/// </summary>
public static class MonsterManager
{
    private static GameWorld World => Program.World;

    public static List<(Monster Monster, Player Player, int Damage)> DrainPendingAttacks()
        => World.DrainMonsterAttacks();

    public static void Initialize()
    {
        World.SetMonsterTemplates(DatabaseManager.LoadMonsterTemplates());
        World.ClearMonsters();
        SpawnMonsters(Balance.MonsterSpawnCount);
    }

    public static void SpawnMonsters(int count)
    {
        for (int i = 0; i < count; i++)
            SpawnOneMonster();
    }

    private static void SpawnOneMonster()
    {
        int x, y;
        int attempts = 0;
        do
        {
            x = World.NextRandom(0, World.Map.Width);
            y = World.NextRandom(0, World.Map.Height);
            attempts++;
        } while ((IsOccupied(x, y) || IsNearMerchant(x, y)) && attempts < Balance.SpawnMaxAttempts);

        if (attempts >= Balance.SpawnMaxAttempts) return;

        int dist = GetDistance(x, y);
        var template = PickTemplateByDistance(dist);
        double mult = 1.0 + dist * Balance.SpawnDifficultyPerDist;

        int health = (int)(template.Health * mult);
        int xp = (int)(template.XpReward * mult);
        int gold = (int)(template.GoldReward * mult);

        var monster = new Monster
        {
            TemplateId = template.Id,
            Name = template.Name,
            X = x,
            Y = y,
            SpawnX = x,
            SpawnY = y,
            WanderRadius = Balance.MonsterWanderRadius,
            Health = health,
            MaxHealth = health,
            XpReward = xp,
            GoldReward = gold,
            Symbol = template.Symbol,
            Level = template.Tier,
            MoveIntervalMs = World.NextRandom(Balance.MonsterMoveMinMs, Balance.MonsterMoveMaxMs),
            LastMoveTime = DateTime.UtcNow.AddMilliseconds(-World.NextRandom(0, Balance.MonsterSpawnJitterMaxMs))
        };
        monster.Strength = template.Strength;
        monster.Stamina = template.Stamina;
        monster.Agility = template.Agility;
        monster.Cunning = template.Cunning;
        monster.Wisdom = template.Wisdom;
        monster.Will = template.Will;
        monster.CritChance = template.CritChance;
        monster.CritDamage = template.CritDamage;
        monster.EvadeChance = template.EvadeChance;
        World.AddMonster(monster);
    }

    public static void SpawnOneMonsterPublic() => SpawnOneMonster();

    private static DatabaseManager.MonsterTemplate PickTemplateByDistance(int dist)
    {
        int tier = Balance.MonsterTierByDistance(dist);
        var templates = World.GetMonsterTemplates();
        var pool = templates.Where(t => t.Tier == tier).ToList();
        if (pool.Count == 0) pool = templates;
        return pool[World.NextRandom(0, pool.Count)];
    }

    private static int GetDistance(int x, int y)
    {
        int dx = x - World.Map.MerchantX;
        int dy = y - World.Map.MerchantY;
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsNearMerchant(int x, int y) => GetDistance(x, y) < Balance.SpawnSafeRadiusFromMerchant;

    public static void RespawnMonster(Monster dead)
    {
        World.RemoveMonster(dead);
        SpawnOneMonster();
    }

    public static void RemoveMonster(Monster monster)
    {
        World.RemoveMonster(monster);
    }

    public static void WanderStep()
    {
        var players = World.GetPlayersSnapshot();
        var monsters = World.GetMonstersSnapshot();
        var now = DateTime.UtcNow;

        foreach (var m in monsters)
        {
            Player? target = null;
            int bestDist = int.MaxValue;
            foreach (var p in players)
            {
                if (p.Health <= 0) continue;
                int d = Math.Abs(p.X - m.X) + Math.Abs(p.Y - m.Y);
                if (d <= m.AggroRange && d < bestDist)
                {
                    bestDist = d;
                    target = p;
                }
            }

            if (target != null)
            {
                m.AggroTarget = target;
            }
            else if (m.AggroTarget != null &&
                     (m.AggroTarget.Health <= 0 ||
                      Math.Abs(m.AggroTarget.X - m.X) + Math.Abs(m.AggroTarget.Y - m.Y) > m.AggroRange))
            {
                m.AggroTarget = null;
            }

            if (m.AggroTarget != null && m.AggroTarget.Health > 0)
            {
                int dist = Math.Abs(m.AggroTarget.X - m.X) + Math.Abs(m.AggroTarget.Y - m.Y);
                if (dist <= 1)
                {
                    if ((now - m.LastMoveTime).TotalMilliseconds >= m.MoveIntervalMs)
                    {
                        m.LastMoveTime = now;
                        int dmgToPlayer = Math.Max(1, m.GetTotalAttack() - m.AggroTarget.GetTotalDefense());
                        World.QueueMonsterAttack(m, m.AggroTarget, dmgToPlayer);
                    }
                    continue;
                }
                int stepX = Math.Sign(m.AggroTarget.X - m.X);
                int stepY = Math.Sign(m.AggroTarget.Y - m.Y);

                // Движение СТРОГО по 4 сторонам (без диагональных шагов), чтобы
                // сущности не упирались друг в друга по диагонали. При диагонали
                // шагаем только по одной оси, не наступая на клетку цели.
                int mx = 0, my = 0;
                if (stepX != 0 && stepY != 0)
                {
                    if (m.X + stepX != m.AggroTarget.X || m.Y != m.AggroTarget.Y)
                        mx = stepX; // шаг по X не ведёт прямо в клетку цели
                    else
                        my = stepY;
                }
                else if (stepX != 0)
                    mx = stepX;
                else if (stepY != 0)
                    my = stepY;

                if ((mx != 0 && (m.X + mx != m.AggroTarget.X || m.Y != m.AggroTarget.Y))
                    || (my != 0 && (m.Y + my != m.AggroTarget.Y || m.X != m.AggroTarget.X)))
                {
                    TryMoveTowards(m, mx, my);
                }
                continue;
            }

            if ((now - m.LastMoveTime).TotalMilliseconds < m.MoveIntervalMs) continue;

            if (World.NextRandom(0, 100) < Balance.MonsterWanderSkipChance) continue;

            int dx = World.NextRandom(-1, 2);
            int dy = World.NextRandom(-1, 2);
            if (dx == 0 && dy == 0) continue;

            int nx = m.X + dx;
            int ny = m.Y + dy;

            if (nx < 0 || nx >= World.Map.Width || ny < 0 || ny >= World.Map.Height) continue;
            if (Math.Abs(nx - m.SpawnX) > m.WanderRadius || Math.Abs(ny - m.SpawnY) > m.WanderRadius) continue;
            if (IsNearMerchant(nx, ny)) continue;

            m.X = nx;
            m.Y = ny;
            m.LastMoveTime = now;
        }
    }

    private static bool TryMoveTowards(Monster m, int dx, int dy)
    {
        if (dx == 0 && dy == 0) return false;
        int nx = m.X + dx;
        int ny = m.Y + dy;
        if (nx < 0 || nx >= World.Map.Width || ny < 0 || ny >= World.Map.Height) return false;
        if (Math.Abs(nx - m.SpawnX) > m.WanderRadius || Math.Abs(ny - m.SpawnY) > m.WanderRadius) return false;
        if (IsNearMerchant(nx, ny)) return false;
        m.X = nx;
        m.Y = ny;
        return true;
    }

    private static bool IsOccupied(int x, int y)
        => World.GetMonstersSnapshot().Any(m => m.X == x && m.Y == y);

    public static Monster? FindMonsterAt(int x, int y) => World.FindMonsterAt(x, y);

    public static Monster? FindMonsterById(Guid id) => World.FindMonsterById(id);

    public static List<Monster> GetAllMonsters() => World.GetMonstersSnapshot();

    public static List<MonsterPosition> GetMonsterPositions()
    {
        return World.GetMonstersSnapshot().Select(m => new MonsterPosition
        {
            Id = m.Id,
            TemplateId = m.TemplateId,
            Name = m.Name,
            X = m.X,
            Y = m.Y,
            Health = m.Health,
            MaxHealth = m.MaxHealth,
            Symbol = m.Symbol,
            Level = m.Level
        }).ToList();
    }

    public static int GetMonsterCount() => World.GetMonsterCount();

    public static void RegenStep()
    {
        const int fullHealDelayMs = Balance.MonsterRegenFullHealDelayMs;
        const int inCombatDelayMs = Balance.MonsterRegenInCombatDelayMs;
        const int outOfCombatHeal = Balance.MonsterRegenOutOfCombatHeal;
        const int outOfCombatDelayMs = Balance.MonsterRegenOutOfCombatTickMs;
        const int inCombatTickMs = Balance.MonsterRegenInCombatTickMs;
        const double inCombatFraction = Balance.MonsterRegenInCombatFraction;

        var now = DateTime.UtcNow;
        foreach (var m in World.GetMonstersSnapshot())
        {
            if (m.Health >= m.MaxHealth) continue;

            bool outOfCombat = m.AggroTarget == null &&
                               (now - m.LastDamagedTime).TotalMilliseconds > fullHealDelayMs;
            if (outOfCombat)
            {
                m.Health = m.MaxHealth;
                continue;
            }

            bool mInCombat = (now - m.LastDamagedTime).TotalMilliseconds < inCombatDelayMs;
            int tick = mInCombat ? inCombatTickMs : outOfCombatDelayMs;

            if ((now - m.LastRegenTime).TotalMilliseconds >= tick)
            {
                int heal = mInCombat
                    ? Math.Max(Balance.MonsterRegenMinHeal, (int)(m.MaxHealth * inCombatFraction))
                    : outOfCombatHeal;
                m.Health = Math.Min(m.MaxHealth, m.Health + heal);
                m.LastRegenTime = now;
            }
        }
    }

    public static (int damageToMonster, int damageToPlayer, bool monsterDead, bool isCrit, bool isEvaded) CalculateCombat(Player player, Monster monster, bool applyMonsterDamage = true)
    {
        var rng = new Random();

        bool monsterEvaded = rng.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
        int playerDamage = 0;
        bool isCrit = false;
        if (!monsterEvaded)
        {
            isCrit = rng.Next(Balance.ChanceRollMax) < player.GetCritChance();
            int baseDamage = Math.Max(Balance.MinDamage, player.GetTotalAttack() - monster.GetTotalDefense());
            playerDamage = isCrit ? (int)(baseDamage * player.GetCritDamage()) : baseDamage;
            if (applyMonsterDamage)
            {
                monster.Health -= playerDamage;
                monster.LastDamagedTime = DateTime.UtcNow;
                monster.DamageTracker[player.Id] = monster.DamageTracker.GetValueOrDefault(player.Id) + playerDamage;
            }
        }
        bool monsterDead = monster.Health <= 0;

        int monsterDamage = 0;
        bool isEvaded = false;
        if (!monsterDead)
        {
            isEvaded = rng.Next(Balance.ChanceRollMax) < player.GetEvadeChance();
            if (!isEvaded)
            {
                bool monsterCrit = rng.Next(Balance.ChanceRollMax) < monster.GetCritChance();
                int baseDamage = Math.Max(Balance.MinDamage, monster.GetTotalAttack() - player.GetTotalDefense());
                monsterDamage = monsterCrit ? (int)(baseDamage * monster.GetCritDamage()) : baseDamage;
            }
        }

        return (playerDamage, monsterDamage, monsterDead, isCrit, isEvaded);
    }
}
