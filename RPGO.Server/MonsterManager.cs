using RPGGame.Shared.Models;
using RPGGame.Server.Services;

namespace RPGGame.Server;

/// <summary>
/// Тонкая обёртка над GameWorld для логики монстров.
/// Состояние (список монстров, шаблоны, очередь атак) хранится в GameWorld.
/// </summary>
public class MonsterManager
{
    private readonly GameWorld _world;
    private GameServices _svc = null!;

    // Очередь респавна убитых монстров: каждый монстр привязан к своей точке спавна.
    private readonly object _respawnLock = new();
    private readonly List<(int X, int Y, string TemplateId, DateTime RespawnAt)> _pendingRespawns = new();

    private int? _mannequinX;
    private int? _mannequinY;

    public MonsterManager(GameWorld world)
    {
        _world = world;
    }

    public void SetServices(GameServices svc) => _svc = svc;

    /// <summary>Позиция манекена из Tiled-карты (type="dummy"), приоритет над офсетом от торговца.</summary>
    public void SetMannequinPosition(int x, int y)
    {
        _mannequinX = x;
        _mannequinY = y;
    }

    public double GetEffectiveAttack(ICombatant attacker, int baseAttack)
    {
        double dmgBonus = _svc.Debuffs.GetDebuffValue(attacker, DebuffType.DamageBonus);
        double mult = 1.0 + dmgBonus;
        if (attacker is Player p) mult *= p.GetBerserkMultiplier();
        return baseAttack * mult;
    }

    public double GetEffectiveAttack(ICombatant attacker)
        => GetEffectiveAttack(attacker, attacker.GetTotalAttack());

    public double GetEffectiveDefense(ICombatant defender)
    {
        double armorPen = _svc.Debuffs.GetDebuffValue(defender, DebuffType.ArmorPenetration);
        return defender.GetTotalDefense() * (1.0 - Math.Min(armorPen, 1.0));
    }

    public int ApplyDmgReduction(ICombatant attacker, int baseDamage)
    {
        double dmgReduction = _svc.Debuffs.GetDebuffValue(attacker, DebuffType.DamageReduction);
        return Math.Max(Balance.MinDamage, (int)(baseDamage * (1.0 - Math.Min(dmgReduction, 1.0))));
    }

    public List<(Monster Monster, Player Player, int Damage)> DrainPendingAttacks()
        => _world.DrainMonsterAttacks();

    public void Initialize(List<TiledSpawn>? spawns = null, int? mannequinX = null, int? mannequinY = null)
    {
        _world.SetMonsterTemplates(DatabaseManager.LoadMonsterTemplates());
        _world.ClearMonsters();
        lock (_respawnLock) _pendingRespawns.Clear();

        if (mannequinX.HasValue && mannequinY.HasValue)
            SetMannequinPosition(mannequinX.Value, mannequinY.Value);

        if (spawns != null && spawns.Count > 0)
        {
            int spawned = 0;
            foreach (var s in spawns)
                if (SpawnNamedMonster(s.X, s.Y, s.Name))
                    spawned++;
            Log.Info($"Спавн монстров из точек: {spawned}/{spawns.Count}");
        }
        else
        {
            Log.Warn("Точки спавна монстров не заданы в Tiled — мир запущен без монстров");
        }

        SpawnMannequin();
    }

    /// <summary>Спавн монстра по точке из Tiled (точный шаблон, без масштабирования от дистанции).</summary>
    private bool SpawnNamedMonster(int x, int y, string name)
    {
        if (_world.Map.IsObstacle(x, y))
        {
            Log.Warn($"Точка спавна монстра '{name}' на непроходимой клетке ({x},{y}), пропускаю");
            return false;
        }

        var template = _world.GetMonsterTemplates()
            .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            Log.Warn($"Точка спавна: шаблон монстра '{name}' не найден, пропускаю");
            return false;
        }

        _world.AddMonster(CreateMonster(template, x, y, 1.0));
        return true;
    }

    private Monster CreateMonster(MonsterTemplate template, int x, int y, double mult)
    {
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
        monster.BlockChance = template.BlockChance;
        monster.ParryChance = template.ParryChance;
        monster.ShieldDefense = template.ShieldDefense;
        return monster;
    }

    public void SpawnMannequin()
    {
        int mx, my;
        if (_mannequinX.HasValue && _mannequinY.HasValue)
        {
            mx = _mannequinX.Value;
            my = _mannequinY.Value;
        }
        else
        {
            mx = _world.Map.MerchantX + Balance.MannequinOffsetX;
            my = _world.Map.MerchantY + Balance.MannequinOffsetY;
        }
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

    private int GetDistance(int x, int y)
    {
        int dx = x - _world.Map.MerchantX;
        int dy = y - _world.Map.MerchantY;
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }

    private bool IsNearMerchant(int x, int y) => GetDistance(x, y) < Balance.SpawnSafeRadiusFromMerchant;

    /// <summary>Удаляет монстра и ставит его в очередь респавна на своей точке спавна.</summary>
    public void RemoveMonster(Monster monster)
    {
        _world.RemoveMonster(monster);
        if (monster.IsMannequin) return;
        lock (_respawnLock)
            _pendingRespawns.Add((monster.SpawnX, monster.SpawnY, monster.TemplateId, DateTime.UtcNow.AddMilliseconds(Balance.MonsterRespawnDelayMs)));
    }

    /// <summary>Вызывается из игрового тика: респавнит убитых монстров на их точках.</summary>
    public void TickRespawns()
    {
        var now = DateTime.UtcNow;
        List<(int X, int Y, string TemplateId)> due = new();
        lock (_respawnLock)
        {
            for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
            {
                if (_pendingRespawns[i].RespawnAt <= now)
                {
                    due.Add((_pendingRespawns[i].X, _pendingRespawns[i].Y, _pendingRespawns[i].TemplateId));
                    _pendingRespawns.RemoveAt(i);
                }
            }
        }

        foreach (var d in due)
            RespawnAtPoint(d.X, d.Y, d.TemplateId);
    }

    private void RespawnAtPoint(int x, int y, string templateId)
    {
        if (_world.Map.IsObstacle(x, y))
        {
            // Точка временно занята препятствием — попробуем позже
            lock (_respawnLock)
                _pendingRespawns.Add((x, y, templateId, DateTime.UtcNow.AddMilliseconds(Balance.MonsterRespawnDelayMs)));
            return;
        }

        var template = _world.GetMonsterTemplates().FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            Log.Warn($"Респавн: шаблон монстра '{templateId}' не найден, пропускаю");
            return;
        }

        _world.AddMonster(CreateMonster(template, x, y, 1.0));
    }

    public bool WanderStep()
    {
        var players = _world.GetPlayersSnapshot();
        var monsters = _world.GetMonstersSnapshot();
        var now = DateTime.UtcNow;
        bool anyMoved = false;

        foreach (var m in monsters)
        {
            if (m.IsMannequin) continue;
            var debuffs = m.GetDebuffsSnapshot();
            if (debuffs.Any(d => d.Type == DebuffType.Stun)) continue;
            if (debuffs.Any(d => d.Type == DebuffType.Root)) continue;

            double slow = 1.0 + debuffs.Where(d => d.Type == DebuffType.Slow).Sum(d => d.Value);
            int moveMs = (int)(m.MoveIntervalMs * Math.Max(1.0, slow));

            // === LEASH: моб возвращается на спавн ===
            if (m.ReturningToSpawn)
            {
                if ((now - m.LastMoveTime).TotalMilliseconds < moveMs) continue;

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
                    m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
                    anyMoved = true;
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
                        anyMoved = true;
                    }
                }
                m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
                continue;
            }

            // === АГРО ===
            Player? target = null;
            int bestDist = int.MaxValue;
            foreach (var p in players)
            {
                if (p.Health <= 0) continue;
                if (p.CurrentZoneId != m.ZoneId) continue;
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
                if (m.X != m.SpawnX || m.Y != m.SpawnY)
                    m.ReturningToSpawn = true;
                continue;
            }

            // === ПОГОНЯ / АТАКА ===
            if (m.AggroTarget != null && m.AggroTarget.Health > 0)
            {
                int dist = Math.Abs(m.AggroTarget.X - m.X) + Math.Abs(m.AggroTarget.Y - m.Y);
                if (dist > m.AggroRange)
                {
                    m.AggroTarget = null;
                    m.StuckTicks = 0;
                    if (m.X != m.SpawnX || m.Y != m.SpawnY)
                        m.ReturningToSpawn = true;
                    continue;
                }
                if (dist <= 1)
                {
                    if ((now - m.LastMoveTime).TotalMilliseconds >= moveMs)
                    {
                        m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 2);
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

                if ((now - m.LastMoveTime).TotalMilliseconds < moveMs) continue;

                bool moved = false;
                if ((mx != 0 && (m.X + mx != m.AggroTarget.X || m.Y != m.AggroTarget.Y))
                    || (my != 0 && (m.Y + my != m.AggroTarget.Y || m.X != m.AggroTarget.X)))
                {
                    moved = TryMoveTowards(m, mx, my);
                }

                if (moved)
                {
                    m.StuckTicks = 0;
                    anyMoved = true;
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
                m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
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
            m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
            anyMoved = true;
        }

        return anyMoved;
    }

    /// <summary>Тик ИИ для монстров инстанса (адаптация WanderStep).</summary>
    public void WanderStepForInstances(List<Monster> monsters, List<Player> players, int mapW, int mapH)
    {
        var now = DateTime.UtcNow;
        foreach (var m in monsters)
        {
            if (m.IsMannequin) continue;
            var debuffs = m.GetDebuffsSnapshot();
            if (debuffs.Any(d => d.Type == DebuffType.Stun)) continue;
            if (debuffs.Any(d => d.Type == DebuffType.Root)) continue;

            double slow = 1.0 + debuffs.Where(d => d.Type == DebuffType.Slow).Sum(d => d.Value);
            int moveMs = (int)(m.MoveIntervalMs * Math.Max(1.0, slow));

            if (m.ReturningToSpawn)
            {
                if ((now - m.LastMoveTime).TotalMilliseconds < moveMs) continue;
                int distToSpawn = Math.Abs(m.X - m.SpawnX) + Math.Abs(m.Y - m.SpawnY);
                if (distToSpawn <= 1)
                {
                    m.X = m.SpawnX; m.Y = m.SpawnY;
                    m.Health = m.MaxHealth;
                    m.ReturningToSpawn = false; m.StuckTicks = 0;
                    m.AggroTarget = null; m.DamageTracker.Clear();
                    m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
                    continue;
                }
                int stepX = Math.Sign(m.SpawnX - m.X);
                int stepY = Math.Sign(m.SpawnY - m.Y);
                int mx = stepX != 0 && stepY != 0 ? stepX : (stepX != 0 ? stepX : 0);
                int my = stepX != 0 && stepY != 0 ? 0 : (stepY != 0 ? stepY : 0);
                int nx = m.X + mx, ny = m.Y + my;
                if (nx >= 0 && nx < mapW && ny >= 0 && ny < mapH && !InstanceOccupied(monsters, nx, ny))
                { m.X = nx; m.Y = ny; }
                m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
                continue;
            }

            Player? target = null;
            int bestDist = int.MaxValue;
            foreach (var p in players)
            {
                if (p.Health <= 0 || p.CurrentZoneId != m.ZoneId) continue;
                int d = Math.Abs(p.X - m.X) + Math.Abs(p.Y - m.Y);
                if (d <= m.AggroRange && d < bestDist) { bestDist = d; target = p; }
            }

            if (target != null) m.AggroTarget = target;
            else if (m.AggroTarget != null &&
                     (m.AggroTarget.Health <= 0 ||
                      Math.Abs(m.AggroTarget.X - m.X) + Math.Abs(m.AggroTarget.Y - m.Y) > m.AggroRange))
            {
                m.AggroTarget = null; m.StuckTicks = 0;
                if (m.X != m.SpawnX || m.Y != m.SpawnY) m.ReturningToSpawn = true;
                continue;
            }

            if (m.AggroTarget != null && m.AggroTarget.Health > 0)
            {
                int dist = Math.Abs(m.AggroTarget.X - m.X) + Math.Abs(m.AggroTarget.Y - m.Y);
                if (dist > m.AggroRange)
                {
                    m.AggroTarget = null; m.StuckTicks = 0;
                    if (m.X != m.SpawnX || m.Y != m.SpawnY) m.ReturningToSpawn = true;
                    continue;
                }
                if (dist <= 1)
                {
                    if ((now - m.LastMoveTime).TotalMilliseconds >= moveMs)
                    {
                        m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 2); m.StuckTicks = 0;
                        int dmg = Math.Max(1, (int)(GetEffectiveAttack(m) - GetEffectiveDefense(m.AggroTarget)));
                        _world.QueueMonsterAttack(m, m.AggroTarget, dmg);
                    }
                    continue;
                }
                int stepX = Math.Sign(m.AggroTarget.X - m.X);
                int stepY = Math.Sign(m.AggroTarget.Y - m.Y);
                int mx = 0, my = 0;
                if (stepX != 0 && stepY != 0)
                {
                    if (m.X + stepX != m.AggroTarget.X || m.Y != m.AggroTarget.Y) mx = stepX;
                    else my = stepY;
                }
                else if (stepX != 0) mx = stepX;
                else if (stepY != 0) my = stepY;

                if ((now - m.LastMoveTime).TotalMilliseconds < moveMs) continue;

                bool moved = false;
                if ((mx != 0 && (m.X + mx != m.AggroTarget.X || m.Y != m.AggroTarget.Y))
                    || (my != 0 && (m.Y + my != m.AggroTarget.Y || m.X != m.AggroTarget.X)))
                {
                    int nx = m.X + mx, ny = m.Y + my;
                    if (nx >= 0 && nx < mapW && ny >= 0 && ny < mapH
                        && Math.Abs(nx - m.SpawnX) <= m.WanderRadius && Math.Abs(ny - m.SpawnY) <= m.WanderRadius
                        && !InstanceOccupied(monsters, nx, ny))
                    { m.X = nx; m.Y = ny; moved = true; }
                }
                if (moved) m.StuckTicks = 0;
                else { m.StuckTicks++; if (m.StuckTicks >= Balance.MonsterLeashStuckTicks) { m.ReturningToSpawn = true; m.AggroTarget = null; m.StuckTicks = 0; } }
                m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
                continue;
            }

            if ((now - m.LastMoveTime).TotalMilliseconds < m.MoveIntervalMs) continue;
            if (_world.NextRandom(0, 100) < Balance.MonsterWanderSkipChance) continue;
            int dir = _world.NextRandom(0, 4);
            int wdx = dir == 2 ? -1 : dir == 3 ? 1 : 0;
            int wdy = dir == 0 ? -1 : dir == 1 ? 1 : 0;
            int wnx = m.X + wdx, wny = m.Y + wdy;
            if (wnx < 0 || wnx >= mapW || wny < 0 || wny >= mapH) continue;
            if (Math.Abs(wnx - m.SpawnX) > m.WanderRadius || Math.Abs(wny - m.SpawnY) > m.WanderRadius) continue;
            if (InstanceOccupied(monsters, wnx, wny)) continue;
            m.X = wnx; m.Y = wny;
            m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
        }
    }

    private static bool InstanceOccupied(List<Monster> monsters, int x, int y)
        => monsters.Any(mm => mm.X == x && mm.Y == y);

    private bool TryMoveTowards(Monster m, int dx, int dy)
    {
        if (dx == 0 && dy == 0) return false;
        int nx = m.X + dx;
        int ny = m.Y + dy;
        if (nx < 0 || nx >= _world.Map.Width || ny < 0 || ny >= _world.Map.Height) return false;
        if (_world.Map.IsObstacle(nx, ny)) return false;
        if (Math.Abs(nx - m.SpawnX) > m.WanderRadius || Math.Abs(ny - m.SpawnY) > m.WanderRadius) return false;
        if (IsNearMerchant(nx, ny)) return false;
        m.X = nx;
        m.Y = ny;
        return true;
    }

    private bool IsOccupiedByMonster(int x, int y)
        => _world.FindMonsterAt(x, y) != null;

    public Monster? FindMonsterAt(int x, int y)
    {
        var world = _world.FindMonsterAt(x, y);
        if (world != null) return world;
        return _svc.Instances.FindMonsterAt(x, y);
    }

    public Monster? FindMonsterById(Guid id)
    {
        var world = _world.FindMonsterById(id);
        if (world != null) return world;
        return _svc.Instances.FindMonsterById(id);
    }

    public List<Monster> GetAllMonsters() => _world.GetMonstersSnapshot();

    public List<Monster> GetAllMonstersIncludingInstances()
    {
        var result = _world.GetMonstersSnapshot();
        result.AddRange(_svc.Instances.GetAllMonsters());
        return result;
    }

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
            IsMannequin = m.IsMannequin,
            ZoneId = m.ZoneId
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

    public (int damageToTarget, int damageToAttacker, bool targetDead, bool isCrit, bool isEvaded, bool isParried, bool isBlocked)
        CalculateCombat(ICombatant attacker, ICombatant defender, bool applyDefenderDamage = true, bool isMelee = true)
    {
        var rng = Random.Shared;

        int rolledAttack = attacker.RollAttackDamage();
        double effectiveAttackerAttack = GetEffectiveAttack(attacker, rolledAttack);
        double effectiveDefenderDefense = GetEffectiveDefense(defender);
        double accuracyReduction = _svc.Debuffs.GetDebuffValue(attacker, DebuffType.AccuracyReduction);

        double passiveAccuracyBonus = 0;
        double passiveCritBonus = 0;
        double armorPenExtra = 0;
        if (attacker is Player plPassive)
        {
            if (plPassive.LearnedSkills.Contains("SK0006")
                && _svc.Debuffs.HasDebuff(defender, DebuffType.Stun))
            {
                passiveAccuracyBonus = 10;
                passiveCritBonus = 10;
            }
            passiveAccuracyBonus += plPassive.GetBowAccuracyBonus();
            passiveCritBonus += plPassive.GetHunterInstinctCritBonus(defender);
            // Для CalculateCombat дистанция неизвестна — берём 1 (макс. пробитие в упор)
            armorPenExtra = plPassive.GetCloseRangeArmorPen(isMelee ? 1 : 3);
        }

        double evadeChance = defender.GetEvadeChance() + accuracyReduction * 100 - passiveAccuracyBonus;
        if (isMelee && defender is Player plDef)
            evadeChance += plDef.GetMeleeEvadeBonus();

        bool defenderEvaded = rng.Next(Balance.ChanceRollMax) < evadeChance;
        if (defenderEvaded)
            return (0, 0, false, false, true, false, false);

        bool defenderParried = isMelee && rng.Next(Balance.ChanceRollMax) < defender.GetParryChance();
        if (defenderParried)
            return (0, 0, false, false, false, true, false);

        bool defenderBlocked = rng.Next(Balance.ChanceRollMax) < defender.GetBlockChance();
        int attackerDamage = 0;
        bool isCrit = rng.Next(Balance.ChanceRollMax) < (attacker.GetCritChance() + passiveCritBonus);
        double def = effectiveDefenderDefense * (1.0 - Math.Min(armorPenExtra, 1.0));
        int baseDamage = Math.Max(Balance.MinDamage, (int)(effectiveAttackerAttack - def));
        attackerDamage = isCrit ? (int)(baseDamage * attacker.GetCritDamage()) : baseDamage;
        attackerDamage = ApplyDmgReduction(attacker, attackerDamage);

        if (defenderBlocked)
        {
            int blockValue = defender.GetBlockValue();
            attackerDamage = Math.Max(Balance.MinDamage, attackerDamage - blockValue);
        }

        if (applyDefenderDamage && defender is Monster mon && !mon.ReturningToSpawn)
        {
            mon.Health -= attackerDamage;
            mon.LastDamagedTime = DateTime.UtcNow;
            if (attacker is Player pl)
                mon.DamageTracker.AddOrUpdate(pl.Id, attackerDamage, (k, old) => old + attackerDamage);
        }
        bool targetDead = defender.Health <= 0;

        return (attackerDamage, 0, targetDead, isCrit, false, false, defenderBlocked);
    }

    public (int damage, bool isCrit, bool isEvaded, bool isParried, bool isBlocked)
        CalculateOffHandAttack(Player attacker, Monster target)
    {
        var rng = Random.Shared;
        if (!attacker.Equipment.IsDualWielding()) return (0, false, false, false, false);

        bool evaded = rng.Next(Balance.ChanceRollMax) < target.GetEvadeChance();
        if (evaded) return (0, false, true, false, false);

        bool parried = rng.Next(Balance.ChanceRollMax) < target.GetParryChance();
        if (parried) return (0, false, false, true, false);

        bool blocked = rng.Next(Balance.ChanceRollMax) < target.GetBlockChance();

        bool crit = rng.Next(Balance.ChanceRollMax) < attacker.GetCritChance();
        double effectiveAttack = GetEffectiveAttack(attacker, attacker.RollOffHandDamage());
        int baseDmg = Math.Max(Balance.MinDamage, (int)(effectiveAttack - GetEffectiveDefense(target)));
        int finalDmg = crit ? (int)(baseDmg * attacker.GetCritDamage()) : baseDmg;
        double offHandFraction = attacker.GetOffHandDamageFraction();
        finalDmg = Math.Max(Balance.MinDamage, (int)(finalDmg * offHandFraction));

        if (blocked)
        {
            int blockValue = target.GetBlockValue();
            finalDmg = Math.Max(Balance.MinDamage, finalDmg - blockValue);
        }

        return (finalDmg, crit, false, false, blocked);
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

            bool evaded = Random.Shared.Next(Balance.ChanceRollMax) < monster.GetEvadeChance();
            if (evaded) continue;

            bool crit = Random.Shared.Next(Balance.ChanceRollMax) < attacker.GetCritChance();
            int dmg = crit ? (int)(cleaveDmg * attacker.GetCritDamage()) : cleaveDmg;
            dmg = Math.Max(Balance.MinDamage, dmg);
            monster.Health -= dmg;
            monster.LastDamagedTime = DateTime.UtcNow;
            monster.DamageTracker.AddOrUpdate(attacker.Id, dmg, (k, old) => old + dmg);
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
