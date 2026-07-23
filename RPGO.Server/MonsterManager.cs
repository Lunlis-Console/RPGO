using RPGGame.Shared.Models;

namespace RPGGame.Server;

/// <summary>
/// Тонкая обёртка над GameWorld для логики монстров.
/// Состояние (список монстров, шаблоны, очередь атак) хранится в GameWorld.
/// </summary>
public class MonsterManager
{
    private readonly GameWorld _world;

    public MonsterManager(GameWorld world)
    {
        _world = world;
    }

    public double GetEffectiveAttack(ICombatant attacker, int baseAttack)
    {
        double dmgBonus = Program.Services.Debuffs.GetDebuffValue(attacker, DebuffType.DamageBonus);
        return baseAttack * (1.0 + dmgBonus);
    }

    public double GetEffectiveAttack(ICombatant attacker)
        => GetEffectiveAttack(attacker, attacker.GetTotalAttack());

    public double GetEffectiveDefense(ICombatant defender)
    {
        double armorPen = Program.Services.Debuffs.GetDebuffValue(defender, DebuffType.ArmorPenetration);
        return defender.GetTotalDefense() * (1.0 - Math.Min(armorPen, 1.0));
    }

    public int ApplyDmgReduction(ICombatant attacker, int baseDamage)
    {
        double dmgReduction = Program.Services.Debuffs.GetDebuffValue(attacker, DebuffType.DamageReduction);
        return Math.Max(Balance.MinDamage, (int)(baseDamage * (1.0 - Math.Min(dmgReduction, 1.0))));
    }

    public List<(Monster Monster, Player Player, int Damage)> DrainPendingAttacks()
        => _world.DrainMonsterAttacks();

    public void Initialize()
    {
        _world.SetMonsterTemplates(DatabaseManager.LoadMonsterTemplates());
        _world.ClearMonsters();
        SpawnMonsters(Balance.MonsterSpawnCount);
        SpawnMannequin();
    }

    public void SpawnMonsters(int count)
    {
        for (int i = 0; i < count; i++)
            SpawnOneMonster();
    }

    private void SpawnOneMonster()
    {
        int x, y;
        int attempts = 0;
        do
        {
            x = _world.NextRandom(0, _world.Map.Width);
            y = _world.NextRandom(0, _world.Map.Height);
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
            MoveIntervalMs = _world.NextRandom(Balance.MonsterMoveMinMs, Balance.MonsterMoveMaxMs),
            LastMoveTime = DateTime.UtcNow.AddMilliseconds(-_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs))
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
        _world.AddMonster(monster);
    }

    public void SpawnMannequin()
    {
        int mx = _world.Map.MerchantX + Balance.MannequinOffsetX;
        int my = _world.Map.MerchantY + Balance.MannequinOffsetY;
        mx = Math.Clamp(mx, 0, _world.Map.Width - 1);
        my = Math.Clamp(my, 0, _world.Map.Height - 1);

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
        _world.AddMonster(mannequin);
    }

    public void SpawnOneMonsterPublic() => SpawnOneMonster();

    private MonsterTemplate PickTemplateByDistance(int dist)
    {
        int tier = Balance.MonsterTierByDistance(dist);
        var templates = _world.GetMonsterTemplates();
        var pool = templates.Where(t => t.Tier == tier).ToList();
        if (pool.Count == 0) pool = templates;
        return pool[_world.NextRandom(0, pool.Count)];
    }

    private int GetDistance(int x, int y)
    {
        int dx = x - _world.Map.MerchantX;
        int dy = y - _world.Map.MerchantY;
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }

    private bool IsNearMerchant(int x, int y) => GetDistance(x, y) < Balance.SpawnSafeRadiusFromMerchant;

    public void RespawnMonster(Monster dead)
    {
        _world.RemoveMonster(dead);
        SpawnOneMonster();
    }

    public void RemoveMonster(Monster monster)
    {
        _world.RemoveMonster(monster);
    }

    public void WanderStep()
    {
        var players = _world.GetPlayersSnapshot();
        var monsters = _world.GetMonstersSnapshot();
        var now = DateTime.UtcNow;

        foreach (var m in monsters)
        {
            if (m.IsMannequin) continue;

            // === LEASH: моб возвращается на спавн ===
            if (m.ReturningToSpawn)
            {
                if ((now - m.LastMoveTime).TotalMilliseconds < m.MoveIntervalMs) continue;

                int distToSpawn = Math.Abs(m.X - m.SpawnX) + Math.Abs(m.Y - m.SpawnY);
                if (distToSpawn <= 1)
                {
                    m.X = m.SpawnX;
                    m.Y = m.SpawnY;
                    m.Health = m.MaxHealth;
                    m.ReturningToSpawn = false;
                    m.StuckTicks = 0;
                    m.AggroTarget = null;
                    m.DamageTracker.Clear();
                    m.LastMoveTime = now;
                    continue;
                }

                int stepX = Math.Sign(m.SpawnX - m.X);
                int stepY = Math.Sign(m.SpawnY - m.Y);
                int mx = 0, my = 0;
                if (stepX != 0 && stepY != 0)
                {
                    mx = stepX;
                    my = 0;
                }
                else if (stepX != 0) mx = stepX;
                else if (stepY != 0) my = stepY;

                int nx = m.X + mx;
                int ny = m.Y + my;
                if (nx >= 0 && nx < _world.Map.Width && ny >= 0 && ny < _world.Map.Height)
                {
                    if (!IsOccupiedByMonster(nx, ny))
                    {
                        m.X = nx;
                        m.Y = ny;
                    }
                }
                m.LastMoveTime = now;
                continue;
            }

            // === АГРО ===
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
                m.StuckTicks = 0;
                // Начинаем возврат на спавн если не на месте
                if (m.X != m.SpawnX || m.Y != m.SpawnY)
                    m.ReturningToSpawn = true;
                continue;
            }

            // === ПОГОНЯ / АТАКА ===
            if (m.AggroTarget != null && m.AggroTarget.Health > 0)
            {
                int dist = Math.Abs(m.AggroTarget.X - m.X) + Math.Abs(m.AggroTarget.Y - m.Y);
                if (dist <= 1)
                {
                    if ((now - m.LastMoveTime).TotalMilliseconds >= m.MoveIntervalMs)
                    {
                        m.LastMoveTime = now;
                        m.StuckTicks = 0;
                        int dmgToPlayer = Math.Max(1, (int)(GetEffectiveAttack(m) - GetEffectiveDefense(m.AggroTarget)));
                        _world.QueueMonsterAttack(m, m.AggroTarget, dmgToPlayer);
                    }
                    continue;
                }
                int stepX = Math.Sign(m.AggroTarget.X - m.X);
                int stepY = Math.Sign(m.AggroTarget.Y - m.Y);

                int mx = 0, my = 0;
                if (stepX != 0 && stepY != 0)
                {
                    if (m.X + stepX != m.AggroTarget.X || m.Y != m.AggroTarget.Y)
                        mx = stepX;
                    else
                        my = stepY;
                }
                else if (stepX != 0)
                    mx = stepX;
                else if (stepY != 0)
                    my = stepY;

                if ((now - m.LastMoveTime).TotalMilliseconds < m.MoveIntervalMs) continue;

                bool moved = false;
                if ((mx != 0 && (m.X + mx != m.AggroTarget.X || m.Y != m.AggroTarget.Y))
                    || (my != 0 && (m.Y + my != m.AggroTarget.Y || m.X != m.AggroTarget.X)))
                {
                    moved = TryMoveTowards(m, mx, my);
                }

                if (moved)
                {
                    m.StuckTicks = 0;
                }
                else
                {
                    m.StuckTicks++;
                    if (m.StuckTicks >= Balance.MonsterLeashStuckTicks)
                    {
                        m.ReturningToSpawn = true;
                        m.AggroTarget = null;
                        m.StuckTicks = 0;
                    }
                }
                m.LastMoveTime = now;
                continue;
            }

            // === БЛУЖДАНИЕ ===
            if ((now - m.LastMoveTime).TotalMilliseconds < m.MoveIntervalMs) continue;

            if (_world.NextRandom(0, 100) < Balance.MonsterWanderSkipChance) continue;

            int dir = _world.NextRandom(0, 4);
            int dx = dir == 2 ? -1 : dir == 3 ? 1 : 0;
            int dy = dir == 0 ? -1 : dir == 1 ? 1 : 0;

            int wnx = m.X + dx;
            int wny = m.Y + dy;

            if (wnx < 0 || wnx >= _world.Map.Width || wny < 0 || wny >= _world.Map.Height) continue;
            if (Math.Abs(wnx - m.SpawnX) > m.WanderRadius || Math.Abs(wny - m.SpawnY) > m.WanderRadius) continue;
            if (IsNearMerchant(wnx, wny)) continue;
            if (IsOccupiedByMonster(wnx, wny)) continue;

            m.X = wnx;
            m.Y = wny;
            m.LastMoveTime = now;
        }
    }

    private bool TryMoveTowards(Monster m, int dx, int dy)
    {
        if (dx == 0 && dy == 0) return false;
        int nx = m.X + dx;
        int ny = m.Y + dy;
        if (nx < 0 || nx >= _world.Map.Width || ny < 0 || ny >= _world.Map.Height) return false;
        if (Math.Abs(nx - m.SpawnX) > m.WanderRadius || Math.Abs(ny - m.SpawnY) > m.WanderRadius) return false;
        if (IsNearMerchant(nx, ny)) return false;
        m.X = nx;
        m.Y = ny;
        return true;
    }

    private bool IsOccupied(int x, int y)
        => _world.GetMonstersSnapshot().Any(m => m.X == x && m.Y == y);

    private bool IsOccupiedByMonster(int x, int y)
        => _world.FindMonsterAt(x, y) != null;

    public Monster? FindMonsterAt(int x, int y) => _world.FindMonsterAt(x, y);

    public Monster? FindMonsterById(Guid id) => _world.FindMonsterById(id);

    public List<Monster> GetAllMonsters() => _world.GetMonstersSnapshot();

    public List<MonsterPosition> GetMonsterPositions()
    {
        return _world.GetMonstersSnapshot().Select(m => new MonsterPosition
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

    public int GetMonsterCount() => _world.GetMonsterCount();

    public void RegenStep()
    {
        const int fullHealDelayMs = Balance.MonsterRegenFullHealDelayMs;
        const int inCombatDelayMs = Balance.MonsterRegenInCombatDelayMs;
        const int outOfCombatHeal = Balance.MonsterRegenOutOfCombatHeal;
        const int outOfCombatDelayMs = Balance.MonsterRegenOutOfCombatTickMs;
        const int inCombatTickMs = Balance.MonsterRegenInCombatTickMs;
        const double inCombatFraction = Balance.MonsterRegenInCombatFraction;

        var now = DateTime.UtcNow;
        foreach (var m in _world.GetMonstersSnapshot())
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

    public (int damageToTarget, int damageToAttacker, bool targetDead, bool isCrit, bool isEvaded)
        CalculateCombat(ICombatant attacker, ICombatant defender, bool applyDefenderDamage = true)
    {
        var rng = new Random();

        double effectiveAttackerAttack = GetEffectiveAttack(attacker, attacker.RollAttackDamage());
        double effectiveDefenderDefense = GetEffectiveDefense(defender);
        double accuracyReduction = Program.Services.Debuffs.GetDebuffValue(attacker, DebuffType.AccuracyReduction);

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

    public (int damage, bool isCrit, bool isEvaded)
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

    public void CalculateCleave(Player attacker, Monster primaryTarget)
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

    private List<(int x, int y)> GetCleavePositions(int px, int py, string facing)
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
