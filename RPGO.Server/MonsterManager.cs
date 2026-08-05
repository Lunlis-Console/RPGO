using RPGGame.Shared;
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

    private readonly List<(int X, int Y)> _mannequinPositions = new();

    public MonsterManager(GameWorld world)
    {
        _world = world;
    }

    public void SetServices(GameServices svc) => _svc = svc;

    /// <summary>Позиции манекенов из Tiled-карты (type="dummy"), приоритет над офсетом от торговца.</summary>
    public void AddMannequinPosition(int x, int y)
    {
        _mannequinPositions.Add((x, y));
    }

    public double GetEffectiveAttack(ICombatant attacker, int baseAttack)
        => _svc.MonsterCombat.GetEffectiveAttack(attacker, baseAttack);

    public double GetEffectiveAttack(ICombatant attacker)
        => _svc.MonsterCombat.GetEffectiveAttack(attacker);

    public double GetEffectiveDefense(ICombatant defender)
        => _svc.MonsterCombat.GetEffectiveDefense(defender);

    public int ApplyDmgReduction(ICombatant attacker, int baseDamage)
        => _svc.MonsterCombat.ApplyDmgReduction(attacker, baseDamage);

    public List<(Monster Monster, Player Player, int Damage)> DrainPendingAttacks()
        => _world.DrainMonsterAttacks();

    public void Initialize(List<TiledSpawn>? spawns = null)
    {
        _world.SetMonsterTemplates(DatabaseManager.LoadMonsterTemplates());
        _world.ClearMonsters();
        lock (_respawnLock) _pendingRespawns.Clear();

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

        SpawnMannequins();
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
            LastMoveTime = DateTime.UtcNow.AddMilliseconds(-_world.NextRandom(0, Balance.MonsterMoveMaxMs))
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

    public void SpawnMannequins()
    {
        if (_mannequinPositions.Count > 0)
        {
            foreach (var (x, y) in _mannequinPositions)
                AddMannequinAt(Math.Clamp(x, 0, _world.Map.Width - 1), Math.Clamp(y, 0, _world.Map.Height - 1));
            return;
        }
        AddMannequinAt(Math.Clamp(_world.Map.MerchantX + Balance.MannequinOffsetX, 0, _world.Map.Width - 1),
            Math.Clamp(_world.Map.MerchantY + Balance.MannequinOffsetY, 0, _world.Map.Height - 1));
    }

    private void AddMannequinAt(int mx, int my)
    {
        var template = _world.GetMonsterTemplates().FirstOrDefault(t => t.Id == "MANNEQUIN");
        Monster mannequin;
        if (template != null)
        {
            // Манекен создаётся как обычный моб по шаблону из content.db.
            mannequin = CreateMonster(template, mx, my, 1.0);
            mannequin.IsMannequin = true;
            mannequin.AggroRange = 0;
            mannequin.WanderRadius = 0;
            mannequin.XpReward = 0;
            mannequin.GoldReward = 0;
            mannequin.MoveIntervalMs = 999999;
        }
        else
        {
            // Запасной вариант: шаблон MANNEQUIN не найден в БД (старая БД до миграции 1030).
            mannequin = new Monster
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
        }
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
        var ctx = new MonsterMoveContext(
            _world.Map.Width, _world.Map.Height,
            IsBlocked: (x, y) => _world.Map.IsObstacle(x, y) || IsNearMerchant(x, y),
            IsOccupied: (x, y) => IsOccupiedByMonster(x, y));
        return StepMonsters(monsters, players, ctx);
    }

    public void WanderStepForInstances(List<Monster> monsters, List<Player> players, GameMap map)
    {
        var ctx = new MonsterMoveContext(
            map.Width, map.Height,
            IsBlocked: (x, y) => map.IsObstacle(x, y) || map.GetTile(x, y) == 0 || map.GetTile(x, y) == 255,
            IsOccupied: (x, y) => InstanceOccupied(monsters, x, y));
        StepMonsters(monsters, players, ctx);
    }

    private readonly record struct MonsterMoveContext(
        int MapWidth, int MapHeight,
        Func<int, int, bool> IsBlocked,
        Func<int, int, bool> IsOccupied);

    private bool StepMonsters(List<Monster> monsters, List<Player> players, MonsterMoveContext ctx)
    {
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

            // LEASH: возврат на спавн
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
                    RemoveReturningDebuff(m);
                    anyMoved = true;
                    continue;
                }
                int stepX = Math.Sign(m.SpawnX - m.X), stepY = Math.Sign(m.SpawnY - m.Y);
                int mx = stepX != 0 && stepY != 0 ? stepX : (stepX != 0 ? stepX : 0);
                int my = stepX != 0 && stepY != 0 ? 0 : (stepY != 0 ? stepY : 0);
                int nx = m.X + mx, ny = m.Y + my;
                if (nx >= 0 && nx < ctx.MapWidth && ny >= 0 && ny < ctx.MapHeight
                    && !ctx.IsBlocked(nx, ny) && !ctx.IsOccupied(nx, ny))
                { m.X = nx; m.Y = ny; }
                m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
                continue;
            }

            // АГРО
            Player? target = null; int bestDist = int.MaxValue;
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
                if (m.X != m.SpawnX || m.Y != m.SpawnY) { m.ReturningToSpawn = true; ApplyReturningDebuff(m); }
                continue;
            }

            // ПОГОНЯ / АТАКА
            if (m.AggroTarget != null && m.AggroTarget.Health > 0)
            {
                int dist = Math.Abs(m.AggroTarget.X - m.X) + Math.Abs(m.AggroTarget.Y - m.Y);
                if (dist > m.AggroRange)
                {
                    m.AggroTarget = null; m.StuckTicks = 0;
                    if (m.X != m.SpawnX || m.Y != m.SpawnY) { m.ReturningToSpawn = true; ApplyReturningDebuff(m); }
                    continue;
                }
                if (dist <= 1)
                {
                    if ((now - m.LastMoveTime).TotalMilliseconds >= moveMs)
                    {
                        m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 2);
                        m.StuckTicks = 0;
                        int dmg = Math.Max(1, (int)(GetEffectiveAttack(m) - GetEffectiveDefense(m.AggroTarget)));
                        _world.QueueMonsterAttack(m, m.AggroTarget, dmg);
                    }
                    continue;
                }
                int stpX = Math.Sign(m.AggroTarget.X - m.X), stpY = Math.Sign(m.AggroTarget.Y - m.Y);
                int chmx = 0, chmy = 0;
                if (stpX != 0 && stpY != 0) { chmx = stpX; chmy = 0; }
                else if (stpX != 0) chmx = stpX;
                else if (stpY != 0) chmy = stpY;

                if ((now - m.LastMoveTime).TotalMilliseconds < moveMs) continue;

                bool moved = false;
                if ((chmx != 0 && (m.X + chmx != m.AggroTarget.X || m.Y != m.AggroTarget.Y))
                    || (chmy != 0 && (m.Y + chmy != m.AggroTarget.Y || m.X != m.AggroTarget.X)))
                {
                    int tnx = m.X + chmx, tny = m.Y + chmy;
                    if (tnx >= 0 && tnx < ctx.MapWidth && tny >= 0 && tny < ctx.MapHeight
                        && Math.Abs(tnx - m.SpawnX) <= m.WanderRadius && Math.Abs(tny - m.SpawnY) <= m.WanderRadius
                        && !ctx.IsBlocked(tnx, tny) && !ctx.IsOccupied(tnx, tny))
                    { m.X = tnx; m.Y = tny; moved = true; }
                }
                if (moved) { m.StuckTicks = 0; anyMoved = true; }
                else
                {
                    m.StuckTicks++;
                    if (m.StuckTicks >= Balance.MonsterLeashStuckTicks)
                    { m.ReturningToSpawn = true; m.AggroTarget = null; m.StuckTicks = 0; ApplyReturningDebuff(m); }
                }
                m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
                continue;
            }

            // БЛУЖДАНИЕ
            if ((now - m.LastMoveTime).TotalMilliseconds < m.MoveIntervalMs) continue;
            if (_world.NextRandom(0, 100) < Balance.MonsterWanderSkipChance) continue;
            int dir = _world.NextRandom(0, 4);
            int wdx = dir == 2 ? -1 : dir == 3 ? 1 : 0;
            int wdy = dir == 0 ? -1 : dir == 1 ? 1 : 0;
            int wnx = m.X + wdx, wny = m.Y + wdy;
            if (wnx < 0 || wnx >= ctx.MapWidth || wny < 0 || wny >= ctx.MapHeight) continue;
            if (Math.Abs(wnx - m.SpawnX) > m.WanderRadius || Math.Abs(wny - m.SpawnY) > m.WanderRadius) continue;
            if (ctx.IsBlocked(wnx, wny) || ctx.IsOccupied(wnx, wny)) continue;
            m.X = wnx; m.Y = wny;
            m.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.MonsterSpawnJitterMaxMs) / 3);
            anyMoved = true;
        }

        return anyMoved;
    }

    private static bool InstanceOccupied(List<Monster> monsters, int x, int y)
        => monsters.Any(mm => mm.X == x && mm.Y == y);

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
        return _world.GetMonstersSnapshot().Select(m =>
        {
            var debuffs = m.GetDebuffsSnapshot();
            return new MonsterPosition
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
                ZoneId = m.ZoneId,
                MoveIntervalMs = m.MoveIntervalMs,
                ActiveDebuffTypes = debuffs.Count > 0 ? debuffs.Select(d => d.Type.ToString()).ToList() : null
            };
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
        => _svc.MonsterCombat.CalculateCombat(attacker, defender, applyDefenderDamage, isMelee);

    public (int damage, bool isCrit, bool isEvaded, bool isParried, bool isBlocked)
        CalculateOffHandAttack(Player attacker, Monster target)
        => _svc.MonsterCombat.CalculateOffHandAttack(attacker, target);

    public void CalculateCleave(Player attacker, Monster primaryTarget)
        => _svc.MonsterCombat.CalculateCleave(attacker, primaryTarget, FindMonsterAt);

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

    private void ApplyReturningDebuff(Monster m)
    {
        if (_svc == null) return;
        lock (m.DebuffsLock)
        {
            if (m.ActiveDebuffs.Any(d => d.Type == DebuffType.Returning)) return;
            var debuff = ActiveDebuff.Create(DebuffType.Returning, 0, int.MaxValue, "leash",
                "Возвращение", "Возвращается на точку спавна");
            m.ActiveDebuffs.Add(debuff);
        }
        Task.Run(() => _svc.Combat.SendTargetDebuffUpdateAsync(m));
    }

    private void RemoveReturningDebuff(Monster m)
    {
        if (_svc == null) return;
        lock (m.DebuffsLock) m.ActiveDebuffs.RemoveAll(d => d.Type == DebuffType.Returning);
        Task.Run(() => _svc.Combat.SendTargetDebuffUpdateAsync(m));
    }
}
