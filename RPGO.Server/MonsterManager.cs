using RPGGame.Shared.Models;

namespace RPGGame.Server;

/// <summary>
/// Тонкая обёртка над GameWorld для логики монстров.
/// Состояние (список монстров, шаблоны, очередь атак) хранится в GameWorld.
/// </summary>
public static class MonsterManager
{
    private static GameWorld World => Program.World;

    public static double GetEffectiveAttack(ICombatant attacker, int baseAttack)
    {
        double dmgBonus = DebuffManager.GetDebuffValue(attacker, DebuffType.DamageBonus);
        return baseAttack * (1.0 + dmgBonus);
    }

    public static double GetEffectiveAttack(ICombatant attacker)
        => GetEffectiveAttack(attacker, attacker.GetTotalAttack());

    public static double GetEffectiveDefense(ICombatant defender)
    {
        double armorPen = DebuffManager.GetDebuffValue(defender, DebuffType.ArmorPenetration);
        return defender.GetTotalDefense() * (1.0 - Math.Min(armorPen, 1.0));
    }

    public static int ApplyDmgReduction(ICombatant attacker, int baseDamage)
    {
        double dmgReduction = DebuffManager.GetDebuffValue(attacker, DebuffType.DamageReduction);
        return Math.Max(Balance.MinDamage, (int)(baseDamage * (1.0 - Math.Min(dmgReduction, 1.0))));
    }

    public static List<(Monster Monster, Player Player, int Damage)> DrainPendingAttacks()
        => World.DrainMonsterAttacks();

    public static void Initialize()
    {
        World.SetMonsterTemplates(DatabaseManager.LoadMonsterTemplates());
        World.ClearMonsters();
        SpawnMonsters(Balance.MonsterSpawnCount);
        SpawnMannequin();
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
        monster.Endurance = template.Endurance;
        monster.Agility = template.Agility;
        monster.Cunning = template.Cunning;
        monster.Intellect = template.Intellect;
        monster.Wisdom = template.Wisdom;
        monster.CritChance = template.CritChance;
        monster.CritDamage = template.CritDamage;
        monster.EvadeChance = template.EvadeChance;
        World.AddMonster(monster);
    }

    public static void SpawnMannequin()
    {
        int mx = World.Map.MerchantX + Balance.MannequinOffsetX;
        int my = World.Map.MerchantY + Balance.MannequinOffsetY;
        mx = Math.Clamp(mx, 0, World.Map.Width - 1);
        my = Math.Clamp(my, 0, World.Map.Height - 1);

        var mannequin = new Monster
        {
            Name = "Манекен",
            TemplateId = "MANNEQUIN",
            X = mx,
            Y = my,
            SpawnX = mx,
            SpawnY = my,
            WanderRadius = 0,
            Health = Balance.MannequinHealth,
            MaxHealth = Balance.MannequinHealth,
            XpReward = 0,
            GoldReward = 0,
            Symbol = 'D',
            Level = 1,
            Endurance = 10,
            MoveIntervalMs = 999999,
            IsMannequin = true,
            AggroRange = 0,
            CritChance = 0,
            EvadeChance = 0,
        };
        World.AddMonster(mannequin);
    }

    public static void SpawnOneMonsterPublic() => SpawnOneMonster();

    private static MonsterTemplate PickTemplateByDistance(int dist)
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
                        int dmgToPlayer = Math.Max(1, (int)(GetEffectiveAttack(m) - GetEffectiveDefense(m.AggroTarget)));
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

            // Блуждание строго по 4 сторонам: один случайный ортогональный шаг.
            int dir = World.NextRandom(0, 4); // 0=вверх, 1=вниз, 2=влево, 3=вправо
            int dx = dir == 2 ? -1 : dir == 3 ? 1 : 0;
            int dy = dir == 0 ? -1 : dir == 1 ? 1 : 0;

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
            Level = m.Level,
            IsMannequin = m.IsMannequin
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

    /// <summary>
    /// Универсальный расчёт боя между двумя бойцами (Player/Monster/...).
    /// Заложен как фундамент для PvP: в будущем attacker/defender могут быть
    /// любыми ICombatant (например, игрок против игрока).
    /// </summary>
    public static (int damageToTarget, int damageToAttacker, bool targetDead, bool isCrit, bool isEvaded)
        CalculateCombat(ICombatant attacker, ICombatant defender, bool applyDefenderDamage = true)
    {
        var rng = new Random();

        double effectiveAttackerAttack = GetEffectiveAttack(attacker, attacker.RollAttackDamage());
        double effectiveDefenderDefense = GetEffectiveDefense(defender);
        double accuracyReduction = DebuffManager.GetDebuffValue(attacker, DebuffType.AccuracyReduction);

        bool defenderEvaded = rng.Next(Balance.ChanceRollMax) < (defender.GetEvadeChance() + accuracyReduction * 100);
        int attackerDamage = 0;
        bool isCrit = false;
        if (!defenderEvaded)
        {
            isCrit = rng.Next(Balance.ChanceRollMax) < attacker.GetCritChance();
            int baseDamage = Math.Max(Balance.MinDamage, (int)(effectiveAttackerAttack - effectiveDefenderDefense));
            attackerDamage = isCrit ? (int)(baseDamage * attacker.GetCritDamage()) : baseDamage;
            attackerDamage = ApplyDmgReduction(attacker, attackerDamage);
            if (applyDefenderDamage && defender is Monster mon)
            {
                mon.Health -= attackerDamage;
                mon.LastDamagedTime = DateTime.UtcNow;
                if (attacker is Player pl)
                    mon.DamageTracker[pl.Id] = mon.DamageTracker.GetValueOrDefault(pl.Id) + attackerDamage;
            }
        }
        bool targetDead = defender.Health <= 0;

        return (attackerDamage, 0, targetDead, isCrit, false);
    }

    /// <summary>
    /// Расчёт урона off-hand оружия (dual wield). Без контр-удара монстра.
    /// Возвращает (damage, isCrit, isEvaded). Урон уже умножен на OffHandDamageFraction.
    /// Off-hand rolled damage = off-hand weapon roll + stats/equip bonuses.
    /// </summary>
    public static (int damage, bool isCrit, bool isEvaded)
        CalculateOffHandAttack(Player attacker, Monster target)
    {
        var rng = new Random();
        if (!attacker.Equipment.IsDualWielding()) return (0, false, false);

        bool evaded = rng.Next(Balance.ChanceRollMax) < target.GetEvadeChance();
        if (evaded) return (0, false, true);

        bool crit = rng.Next(Balance.ChanceRollMax) < attacker.GetCritChance();
        double effectiveAttack = GetEffectiveAttack(attacker, attacker.RollOffHandDamage());
        int baseDmg = Math.Max(Balance.MinDamage, (int)(effectiveAttack - GetEffectiveDefense(target)));
        int finalDmg = crit ? (int)(baseDmg * attacker.GetCritDamage()) : baseDmg;
        finalDmg = Math.Max(Balance.MinDamage, (int)(finalDmg * Equipment.OffHandDamageFraction));
        return (finalDmg, crit, false);
    }

    public static void CalculateCleave(Player attacker, Monster primaryTarget)
    {
        var positions = GetCleavePositions(attacker.X, attacker.Y, attacker.Facing);
        double effectiveAttack = GetEffectiveAttack(attacker, attacker.GetMaxAttackDamage());
        int cleaveDmg = Math.Max(Balance.MinDamage,
            (int)((effectiveAttack - GetEffectiveDefense(primaryTarget)) * Balance.CleaveDamageFraction));

        foreach (var (cx, cy) in positions)
        {
            var monster = FindMonsterAt(cx, cy);
            if (monster == null || monster.Id == primaryTarget.Id || monster.Health <= 0) continue;

            bool evaded = new Random().Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
            if (evaded) continue;

            bool crit = new Random().Next(Balance.ChanceRollMax) < attacker.GetCritChance();
            int dmg = crit ? (int)(cleaveDmg * attacker.GetCritDamage()) : cleaveDmg;
            dmg = Math.Max(Balance.MinDamage, dmg);
            monster.Health -= dmg;
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker[attacker.Id] = monster.DamageTracker.GetValueOrDefault(attacker.Id) + dmg;
        }
    }

    private static List<(int x, int y)> GetCleavePositions(int px, int py, string facing)
    {
        return facing switch
        {
            "up"    => new List<(int, int)> { (px - 1, py - 1), (px, py - 1), (px + 1, py - 1) },
            "down"  => new List<(int, int)> { (px - 1, py + 1), (px, py + 1), (px + 1, py + 1) },
            "left"  => new List<(int, int)> { (px - 1, py - 1), (px - 1, py), (px - 1, py + 1) },
            "right" => new List<(int, int)> { (px + 1, py - 1), (px + 1, py), (px + 1, py + 1) },
            _       => new List<(int, int)>()
        };
    }
}
