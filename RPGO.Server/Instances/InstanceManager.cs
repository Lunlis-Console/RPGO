using RPGGame.Server.Repositories;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.Instances;

public class InstanceManager
{
    private readonly IGameServices _svc;
    private readonly Dictionary<Guid, ActiveInstance> _instances = new();
    private readonly object _lock = new();
    private readonly List<InstanceTemplate> _templates = new();
    private readonly List<InstancePortal> _portals = new();
    private readonly Dictionary<(string Zone, int X, int Y), InstancePortal> _portalLookup = new();
    private GameMap? _dungeonTemplate;
    private DungeonSpawnData? _dungeonSpawns;

    public IReadOnlyList<InstanceTemplate> Templates => _templates;

    public InstanceManager(IGameServices svc)
    {
        _svc = svc;
    }

    public void SetDungeonTemplate(GameMap map, DungeonSpawnData? spawns)
    {
        _dungeonTemplate = map;
        _dungeonSpawns = spawns;
    }

    public void LoadAll()
    {
        _templates.Clear();
        _portals.Clear();
        _portalLookup.Clear();

        using var conn = Db.OpenContent();
        // Загрузка шаблонов
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, zone_id, time_limit_seconds, spawn_x, spawn_y, boss_monster_id, chest_x, chest_y, exit_x, exit_y, corridor_length, corridor_width FROM instance_templates";
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                _templates.Add(new InstanceTemplate
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    ZoneId = reader.GetString(2),
                    TimeLimitSeconds = reader.GetInt32(3),
                    SpawnX = reader.GetInt32(4),
                    SpawnY = reader.GetInt32(5),
                    BossMonsterId = reader.GetString(6),
                    ChestX = reader.GetInt32(7),
                    ChestY = reader.GetInt32(8),
                    ExitX = reader.GetInt32(9),
                    ExitY = reader.GetInt32(10),
                    CorridorLength = reader.GetInt32(11),
                    CorridorWidth = reader.GetInt32(12)
                });
            }
        }

        // Загрузка порталов
        cmd.CommandText = "SELECT id, from_zone, from_x, from_y, instance_template_id FROM instance_portals";
        using (var reader2 = cmd.ExecuteReader())
        {
            while (reader2.Read())
        {
            var p = new InstancePortal
            {
                Id = reader2.GetString(0),
                FromZone = reader2.GetString(1),
                FromX = reader2.GetInt32(2),
                FromY = reader2.GetInt32(3),
                InstanceTemplateId = reader2.GetString(4)
            };
            _portals.Add(p);
            _portalLookup[(p.FromZone, p.FromX, p.FromY)] = p;
            }
        }

        Log.Info($"Загружено {_templates.Count} шаблонов инстансов, {_portals.Count} порталов");
    }

    public InstanceTemplate? FindTemplate(string id)
        => _templates.FirstOrDefault(t => t.Id == id);

    public InstancePortal? FindPortal(string zone, int x, int y)
        => _portalLookup.TryGetValue((zone, x, y), out var p) ? p : null;

    /// <summary>
    /// Применяет позиции порталов инстансов из Tiled-карт (type="instance_portal",
    /// name = id шаблона инстанса). Позиция входа/выхода берётся из Tiled, а связь
    /// шаблон → зона выхода остаётся из БД (instance_portals).
    /// </summary>
    public void ApplyTiledPortals(IEnumerable<TiledNpc> tiledObjects)
    {
        int applied = 0;
        foreach (var tp in tiledObjects)
        {
            if (!string.Equals(tp.Type, "instance_portal", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrEmpty(tp.Name)) continue;

            var existing = _portals.FirstOrDefault(p => p.InstanceTemplateId == tp.Name);
            if (existing != null)
            {
                _portalLookup.Remove((existing.FromZone, existing.FromX, existing.FromY));
                existing.FromZone = tp.ZoneId;
                existing.FromX = tp.X;
                existing.FromY = tp.Y;
                _portalLookup[(tp.ZoneId, tp.X, tp.Y)] = existing;
            }
            else
            {
                var portal = new InstancePortal
                {
                    Id = $"tiled_portal_{tp.Name}",
                    FromZone = tp.ZoneId,
                    FromX = tp.X,
                    FromY = tp.Y,
                    InstanceTemplateId = tp.Name
                };
                _portals.Add(portal);
                _portalLookup[(tp.ZoneId, tp.X, tp.Y)] = portal;
            }
            applied++;
        }

        if (applied > 0)
            Log.Info($"Применено позиций порталов инстансов из Tiled: {applied}");
    }

    public InstancePortal? FindPortalForTemplate(string templateId)
        => _portals.FirstOrDefault(p => p.InstanceTemplateId == templateId);

    public ActiveInstance? FindInstanceByPlayer(Player player)
    {
        lock (_lock)
            return _instances.Values.FirstOrDefault(inst => inst.Players.Contains(player));
    }

    private static int ExtractLevelFromTemplateId(string templateId)
    {
        int lastUnderscore = templateId.LastIndexOf('_');
        if (lastUnderscore >= 0 && int.TryParse(templateId.AsSpan(lastUnderscore + 1), out int lvl))
            return Math.Max(1, lvl);
        return 1;
    }

    public ActiveInstance? FindInstanceByZoneId(string zoneId)
    {
        if (!zoneId.StartsWith("instance:")) return null;
        lock (_lock)
            return _instances.Values.FirstOrDefault(inst => inst.InstanceZoneId == zoneId);
    }

    public ActiveInstance? FindInstanceById(Guid id)
    {
        lock (_lock)
            return _instances.TryGetValue(id, out var inst) ? inst : null;
    }

    public GameMap? GetInstanceMap(string zoneId)
    {
        var inst = FindInstanceByZoneId(zoneId);
        return inst?.Map;
    }

    private Item? RollRewardWeapon(int playerLevel, int dungeonLevel)
    {
        int maxLevel = Math.Min(playerLevel, dungeonLevel + 4);
        int minLevel = Math.Max(1, maxLevel - 3);
        var allWeapons = _svc.Merchant.ShopItems
            .Where(i => i.Type is "weapon" or "twohand" && i.RequiredLevel <= maxLevel && (i.RequiredLevel >= minLevel || i.RequiredLevel == 0))
            .ToList();

        if (allWeapons.Count == 0)
            allWeapons = _svc.Merchant.ShopItems.Where(i => i.Type is "weapon" or "twohand").ToList();

        if (allWeapons.Count == 0) return null;

        int roll = Random.Shared.Next(100);
        string qualityLabel = roll < 15 ? "Эпический" : roll < 40 ? "Редкий" : roll < 70 ? "Необычный" : "Обычный";
        var qualityWeapons = allWeapons.Where(w => w.Description.Contains(qualityLabel)).ToList();
        var picked = qualityWeapons.Count > 0
            ? qualityWeapons[Random.Shared.Next(qualityWeapons.Count)]
            : allWeapons[Random.Shared.Next(allWeapons.Count)];

        var clone = picked.Clone();
        clone.Id = Guid.NewGuid().ToString();
        clone.Stock = 1;
        clone.Quantity = 1;
        clone.IsBuyback = false;
        return clone;
    }

    /// <summary>Попытка входа игрока в инстанс.</summary>
    public async Task<bool> TryEnter(Player player, string templateId, ClientConnection conn)
    {
        var template = FindTemplate(templateId);
        if (template == null)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Этот инстанс не найден.");
            return false;
        }

        if (player.Combat.InCombat)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Бой", "Нельзя войти в инстанс в бою.");
            return false;
        }

        ActiveInstance? existing;
        lock (_lock)
        {
            // Ищем существующий инстанс для пати игрока
            existing = null;
            if (player.PartyId != null)
            {
                existing = _instances.Values.FirstOrDefault(inst =>
                    inst.Template.Id == templateId && inst.Players.Any(p => p.PartyId == player.PartyId));
            }
            // Или инстанс, созданный этим игроком ранее
            existing ??= _instances.Values.FirstOrDefault(inst =>
                inst.Template.Id == templateId && inst.Players.Contains(player));

            if (existing != null)
            {
                if (existing.Players.Count >= 5)
                {
                    _ = _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Инстанс переполнен.");
                    return false;
                }
            }
        }

        if (existing != null)
            return await EnterExisting(player, existing, conn);

        // Создаём новый инстанс
        return await CreateAndEnter(player, template, conn);
    }

    private async Task<bool> EnterExisting(Player player, ActiveInstance instance, ClientConnection conn)
    {
        lock (_lock)
        {
            instance.Players.Add(player);
        }
        await TeleportInto(player, instance, conn);
        return true;
    }

    private async Task<bool> CreateAndEnter(Player player, InstanceTemplate template, ClientConnection conn)
    {
        GameMap map;
        int spawnX, spawnY;
        if (_dungeonTemplate != null)
        {
            map = _dungeonTemplate.Clone();
            spawnX = _dungeonSpawns?.PlayerSpawn.X ?? FindWalkableSpot(map, preferTop: true).x;
            spawnY = _dungeonSpawns?.PlayerSpawn.Y ?? FindWalkableSpot(map, preferTop: true).y;
        }
        else
        {
            map = GenerateCorridorMap(template);
            spawnX = template.SpawnX;
            spawnY = template.SpawnY;
        }

        var instance = new ActiveInstance(template, map);
        instance._spawnX = spawnX;
        instance._spawnY = spawnY;
        if (_dungeonSpawns != null)
        {
            instance._chestX = _dungeonSpawns.Chest.X;
            instance._chestY = _dungeonSpawns.Chest.Y;
            instance._exitX = _dungeonSpawns.Exit.X;
            instance._exitY = _dungeonSpawns.Exit.Y;
        }
        SpawnMonsters(instance, template);

        lock (_lock)
        {
            _instances[instance.Id] = instance;
            instance.Players.Add(player);
        }

        // Регистрируем карту инстанса в ZoneManager
        _svc.Zones.RegisterInstanceZone(instance.InstanceZoneId, map);
        if (_dungeonTemplate != null)
            _svc.Zones.SetTileConfig(instance.InstanceZoneId, 64, "Dungeon-Tilemap");

        await TeleportInto(player, instance, conn);
        await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", $"Вход в «{template.Name}». У вас {template.TimeLimitSeconds / 60} мин.");
        return true;
    }

    private async Task TeleportInto(Player player, ActiveInstance instance, ClientConnection conn)
    {
        player.CurrentZoneId = instance.InstanceZoneId;
        player.X = instance._spawnX > 0 ? instance._spawnX : instance.Template.SpawnX + instance.OffsetX;
        player.Y = instance._spawnY > 0 ? instance._spawnY : instance.Template.SpawnY + instance.OffsetY;
        player.Combat.Cancel();
        player.Movement.Stop();

        await _svc.Hub.SendZoneTransition(conn, player);
    }

    /// <summary>Генерация карты-коридора на полной карте 100x100 с серой пустотой вокруг.</summary>
    private static GameMap GenerateCorridorMap(InstanceTemplate template)
    {
        int mapW = Balance.WorldWidth;
        int mapH = Balance.WorldHeight;
        int cw = template.CorridorWidth;
        int ch = template.CorridorLength + 3;
        int ox = (mapW - cw) / 2;
        int oy = (mapH - ch) / 2;

        var map = new GameMap(mapW, mapH);
        var tiles = new byte[mapW * mapH];
        for (int y = 0; y < mapH; y++)
            for (int x = 0; x < mapW; x++)
                tiles[y * mapW + x] = (byte)TileType.Null;

        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                int mx = ox + x;
                int my = oy + y;
                if (x == 0 || x == cw - 1)
                {
                    tiles[my * mapW + mx] = (byte)TileType.Wall;
                    map.AddObstacle(mx, my);
                }
                else
                {
                    tiles[my * mapW + mx] = (byte)TileType.Stone;
                }
            }
        }
        map.SetTiles(tiles);
        return map;
    }

    /// <summary>Смещение коридора на полной карте (для пересчёта координат спавнов).</summary>
    public static (int ox, int oy) GetCorridorOffset(InstanceTemplate template)
    {
        int mapW = Balance.WorldWidth;
        int mapH = Balance.WorldHeight;
        int cw = template.CorridorWidth;
        int ch = template.CorridorLength + 3;
        return ((mapW - cw) / 2, (mapH - ch) / 2);
    }

    private static (int x, int y) FindWalkableSpot(GameMap map, bool preferTop)
    {
        int startY = preferTop ? 0 : map.Height - 1;
        int endY = preferTop ? map.Height : -1;
        int step = preferTop ? 1 : -1;
        for (int y = startY; y != endY; y += step)
            for (int x = 0; x < map.Width; x++)
            {
                byte t = map.GetTile(x, y);
                if (t != 0 && t != 255 && !map.IsObstacle(x, y))
                    return (x, y);
            }
        return (map.Width / 2, map.Height / 2);
    }

    /// <summary>Спавн монстров на проходимых тайлах карты.</summary>
    private void SpawnMonsters(ActiveInstance instance, InstanceTemplate template)
    {
        int scaledLevel = ExtractLevelFromTemplateId(template.Id);
        var map = instance.Map;
        var rng = new Random();
        var spawnedIds = new HashSet<string>();

        void DoSpawn(string monId, int x, int y, bool isBoss)
        {
            var tpl = _svc.World.GetMonsterTemplates().FirstOrDefault(t => t.Id == monId);
            if (tpl == null) return;

            int lvl = isBoss ? scaledLevel + 2 : scaledLevel;
            float scale = 1f + (lvl - 1) * 0.3f;
            int hp = (int)(tpl.Health * scale * (isBoss ? 3 : 1));

            var monster = new Monster
            {
                TemplateId = tpl.Id, Name = tpl.Name,
                X = x, Y = y,
                Health = hp, MaxHealth = hp,
                Level = lvl,
                XpReward = (int)(tpl.XpReward * scale * (isBoss ? 3 : 1)),
                GoldReward = (int)(tpl.GoldReward * scale * (isBoss ? 3 : 1)),
                ZoneId = instance.InstanceZoneId,
                Symbol = tpl.Symbol,
                Strength = (int)(tpl.Strength * scale) + (isBoss ? 3 : 0),
                Endurance = (int)(tpl.Endurance * scale) + (isBoss ? 2 : 0),
                Agility = tpl.Agility, Cunning = tpl.Cunning,
                Intellect = tpl.Intellect, Wisdom = tpl.Wisdom,
                CritChance = tpl.CritChance + (isBoss ? 1 : 0),
                CritDamage = tpl.CritDamage + (isBoss ? 0.3 : 0),
                EvadeChance = tpl.EvadeChance,
                SpawnX = x, SpawnY = y,
                WanderRadius = Balance.MonsterWanderRadius,
                AggroRange = isBoss ? 10 : 5,
                MoveIntervalMs = 1500,
                LastMoveTime = DateTime.UtcNow.AddMilliseconds(-rng.Next(0, 500))
            };
            instance.Monsters.Add(monster);
        }

        if (_dungeonSpawns != null && _dungeonSpawns.MonsterSpawns.Count > 0)
        {
            // Спавним монстров из Tiled-точек
            var allTemplates = _svc.World.GetMonsterTemplates();
            for (int i = 0; i < _dungeonSpawns.MonsterSpawns.Count; i++)
            {
                var (sx, sy) = _dungeonSpawns.MonsterSpawns[i];
                var tpl = allTemplates[rng.Next(allTemplates.Count)];
                DoSpawn(tpl.Id, sx, sy, false);
            }
            // Босс
            DoSpawn(template.BossMonsterId, _dungeonSpawns.BossSpawn.X, _dungeonSpawns.BossSpawn.Y, true);
        }
        else
        {
            // Фолбэк: случайные проходимые позиции
            var walkable = new List<(int x, int y)>();
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    byte t = map.GetTile(x, y);
                    if (t != 0 && t != 255 && !map.IsObstacle(x, y))
                        walkable.Add((x, y));
                }
            if (walkable.Count == 0) return;
            walkable.Sort((a, b) => a.y.CompareTo(b.y));
            int step = Math.Max(1, walkable.Count / 6);

            for (int i = 0; i < 6; i++)
            {
                int idx = i * step + rng.Next(step);
                if (idx >= walkable.Count) idx = walkable.Count - 1;
                while (spawnedIds.Contains(walkable[idx].ToString()) && idx < walkable.Count - 1) idx++;
                if (spawnedIds.Contains(walkable[idx].ToString())) continue;
                spawnedIds.Add(walkable[idx].ToString());
                var (sx, sy) = walkable[idx];
                var (monId, isBoss) = i == 5 ? (template.BossMonsterId, true) : (_svc.World.GetMonsterTemplates()[rng.Next(_svc.World.GetMonsterTemplates().Count)].Id, false);
                DoSpawn(monId, sx, sy, isBoss);
            }
        }
    }

    /// <summary>Тик таймеров, ИИ монстров и очистка истёкших инстансов.</summary>
    public async Task TickAsync()
    {
        List<ActiveInstance> expired;
        List<ActiveInstance> active;
        lock (_lock)
        {
            expired = _instances.Values.Where(i => i.IsExpired).ToList();
            foreach (var inst in expired)
                _instances.Remove(inst.Id);
            active = _instances.Values.ToList();
        }

        foreach (var inst in expired)
        {
            await KickAllPlayers(inst, "Время вышло. Инстанс закрыт.");
            Log.Info($"Инстанс {inst.Id} закрыт по таймеру");
        }

        // ИИ монстров в инстансах
        if (active.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var inst in active)
            {
                List<Monster> monsters;
                List<Player> players;
                lock (_lock) { monsters = new List<Monster>(inst.Monsters); players = new List<Player>(inst.Players); }
                if (monsters.Count == 0 || players.Count == 0) continue;

                _svc.Monsters.WanderStepForInstances(monsters, players, inst.Map);

                // Реген монстров
                foreach (var m in monsters)
                {
                    if (m.Health >= m.MaxHealth) continue;
                    bool outOfCombat = m.AggroTarget == null && (now - m.LastDamagedTime).TotalMilliseconds > 5000;
                    if (outOfCombat) { m.Health = m.MaxHealth; continue; }
                    bool inCombat = (now - m.LastDamagedTime).TotalMilliseconds < 3000;
                    int tick = inCombat ? 2000 : 3000;
                    if ((now - m.LastRegenTime).TotalMilliseconds >= tick)
                    {
                        int heal = inCombat
                            ? Math.Max(1, (int)(m.MaxHealth * 0.05))
                            : 5;
                        m.Health = Math.Min(m.MaxHealth, m.Health + heal);
                        m.LastRegenTime = now;
                    }
                }

                // Дебаффы монстров
                foreach (var m in monsters)
                {
                    if (m.GetDebuffsSnapshot().Count > 0)
                        _svc.Debuffs.TickDebuffs(m);
                }
            }
        }
    }

    /// <summary>Выкинуть одного игрока из инстанса.</summary>
    public async Task KickPlayer(Player player, string reason)
    {
        var instance = FindInstanceByPlayer(player);
        if (instance == null) return;

        var portal = _portals.FirstOrDefault(p => p.InstanceTemplateId == instance.Template.Id);
        int exitX = portal?.FromX ?? 50;
        int exitY = portal?.FromY ?? 50;
        string exitZone = portal?.FromZone ?? Balance.MainZoneId;

        player.CurrentZoneId = exitZone;
        player.X = exitX;
        player.Y = exitY;
        player.Combat.Cancel();
        player.Movement.Stop();

        var conn = _svc.World.FindClientByPlayer(player);
        if (conn != null)
        {
            await _svc.Hub.SendZoneTransition(conn, player);
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", reason);
        }

        lock (_lock) instance.Players.Remove(player);
    }

    /// <summary>Выкинуть всех игроков из инстанса.</summary>
    public async Task KickAllPlayers(ActiveInstance instance, string reason)
    {
        var exit = instance.Template;
        // Находим портал выхода на основной карте
        var portal = _portals.FirstOrDefault(p => p.InstanceTemplateId == exit.Id);
        int exitX = portal?.FromX ?? 50;
        int exitY = portal?.FromY ?? 50;
        string exitZone = portal?.FromZone ?? Balance.MainZoneId;

        foreach (var pl in instance.Players.ToList())
        {
            pl.CurrentZoneId = exitZone;
            pl.X = exitX;
            pl.Y = exitY;
            pl.Combat.Cancel();
            pl.Movement.Stop();

            var conn = _svc.World.FindClientByPlayer(pl);
            if (conn != null)
            {
                await _svc.Hub.SendZoneTransition(conn, pl);
                await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", reason);
            }
        }
        instance.Players.Clear();
    }

    /// <summary>Получить всех монстров для зоны инстанса.</summary>
    public List<Monster> GetAllMonsters()
    {
        var result = new List<Monster>();
        lock (_lock)
        {
            foreach (var inst in _instances.Values)
                result.AddRange(inst.Monsters);
        }
        return result;
    }

    public List<Monster> GetMonsters(string zoneId)
    {
        var inst = FindInstanceByZoneId(zoneId);
        if (inst == null) return new List<Monster>();
        lock (_lock) return new List<Monster>(inst.Monsters);
    }

    public List<MonsterPosition> GetAllMonstersPositions()
    {
        var result = new List<MonsterPosition>();
        lock (_lock)
        {
            foreach (var inst in _instances.Values)
            {
                foreach (var m in inst.Monsters)
                {
                    result.Add(new MonsterPosition
                    {
                        Id = m.Id,
                        TemplateId = m.TemplateId,
                        Name = m.Name,
                        X = m.X,
                        Y = m.Y,
                        Health = m.Health,
                        MaxHealth = m.MaxHealth,
                        Symbol = m.Symbol,
                        ZoneId = m.ZoneId,
                        Level = m.Level,
                        MoveIntervalMs = m.MoveIntervalMs
                    });
                }
            }
        }
        return result;
    }

    /// <summary>Пометить сундук как разблокированный (вызывается при смерти босса).</summary>
    public Monster? FindMonsterById(Guid id)
    {
        lock (_lock)
        {
            foreach (var inst in _instances.Values)
            {
                var m = inst.Monsters.FirstOrDefault(x => x.Id == id);
                if (m != null) return m;
            }
        }
        return null;
    }

    public Monster? FindMonsterAt(int x, int y)
    {
        lock (_lock)
        {
            foreach (var inst in _instances.Values)
            {
                var m = inst.Monsters.FirstOrDefault(mm => mm.X == x && mm.Y == y);
                if (m != null) return m;
            }
        }
        return null;
    }

    public bool IsBossMonster(Monster monster)
    {
        var inst = FindInstanceByZoneId(monster.ZoneId);
        return inst != null && monster.TemplateId == inst.Template.BossMonsterId;
    }

    public void RemoveMonster(Monster monster)
    {
        var inst = FindInstanceByZoneId(monster.ZoneId);
        if (inst == null) return;
        lock (_lock) inst.Monsters.Remove(monster);
    }

    /// <summary>Удалить игрока из инстанса (при дисконнекте).</summary>
    public void RemovePlayer(Player player)
    {
        lock (_lock)
        {
            foreach (var inst in _instances.Values)
            {
                if (inst.Players.Remove(player))
                {
                    Log.Info($"Игрок {player.Name} удалён из инстанса {inst.Id}");
                    break;
                }
            }
        }
    }

    public void OnBossKilled(string instanceZoneId)
    {
        var inst = FindInstanceByZoneId(instanceZoneId);
        if (inst == null) return;
        inst.ChestLocked = false;
        Log.Info($"Сундук в инстансе {inst.Id} разблокирован");
    }

    /// <summary>Попытка открыть сундук.</summary>
    public async Task<bool> TryOpenChest(Player player, ClientConnection conn)
    {
        var inst = FindInstanceByPlayer(player);
        if (inst == null)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Вы не в инстансе.");
            return false;
        }

        if (Math.Abs(player.X - inst.EffectiveChestX) + Math.Abs(player.Y - inst.EffectiveChestY) > 1)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Подойдите к сундуку.");
            return false;
        }

        if (inst.ChestLocked)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Сундук заперт. Убейте босса.");
            return false;
        }

        if (inst.ChestOpened)
        {
            if (inst.ChestLootItems.Count == 0 && inst.ChestGold == 0)
            {
                await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Сундук уже опустошён.");
                return false;
            }
            await _svc.Hub.SendToClient(conn, new GameMessage
            {
                Type = "loot_corpse",
                Data = new
                {
                    CorpseId = "chest_" + inst.Id,
                    MonsterName = "Сундук подземелья",
                    DamagePercent = 100,
                    Gold = inst.ChestGold,
                    Items = inst.ChestLootItems.Select(i => new
                    {
                        i.Id, i.Name, i.Type, i.WeaponSubtype, i.Value, i.Description
                    }).ToList()
                }
            });
            return true;
        }

        // Награда — оружие, подобранное под уровень подземелья
        var selectedItem = RollRewardWeapon(player.Level, ExtractLevelFromTemplateId(inst.Template.Id));
        if (selectedItem != null)
            inst.ChestLootItems.Add(selectedItem);

        // Золото
        int goldReward = 50 + player.Level * 10 + Random.Shared.Next(51);
        inst.ChestGold = goldReward;
        inst.ChestOpened = true;

        await _svc.Hub.SendToClient(conn, new GameMessage
        {
            Type = "loot_corpse",
            Data = new
            {
                CorpseId = "chest_" + inst.Id,
                MonsterName = "Сундук подземелья",
                DamagePercent = 100,
                Gold = goldReward,
                Items = inst.ChestLootItems.Select(i => new
                {
                    i.Id, i.Name, i.Type, i.WeaponSubtype, i.Value, i.Description
                }).ToList()
            }
        });
        Log.Info($"{player.Name} открыл сундук в инстансе {inst.Id}");
        return true;
    }
}
