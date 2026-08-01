using RPGGame.Server.Repositories;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.Instances;

public class InstanceManager
{
    private readonly Lazy<GameServices> _svcLazy;
    private GameServices _svc => _svcLazy.Value;
    private readonly Dictionary<Guid, ActiveInstance> _instances = new();
    private readonly object _lock = new();
    private readonly List<InstanceTemplate> _templates = new();
    private readonly List<InstancePortal> _portals = new();
    private readonly Dictionary<(string Zone, int X, int Y), InstancePortal> _portalLookup = new();

    public IReadOnlyList<InstanceTemplate> Templates => _templates;

    public InstanceManager(Lazy<GameServices> svc)
    {
        _svcLazy = svc;
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

    public ActiveInstance? FindInstanceByZoneId(string zoneId)
    {
        if (!zoneId.StartsWith("instance:")) return null;
        lock (_lock)
            return _instances.Values.FirstOrDefault(inst => inst.InstanceZoneId == zoneId);
    }

    public GameMap? GetInstanceMap(string zoneId)
    {
        var inst = FindInstanceByZoneId(zoneId);
        return inst?.Map;
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
        var map = GenerateCorridorMap(template);

        var instance = new ActiveInstance(template, map);
        SpawnMonsters(instance, template);

        lock (_lock)
        {
            _instances[instance.Id] = instance;
            instance.Players.Add(player);
        }

        // Регистрируем карту инстанса в ZoneManager
        _svc.Zones.RegisterInstanceZone(instance.InstanceZoneId, map);

        await TeleportInto(player, instance, conn);
        await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", $"Вход в «{template.Name}». У вас {template.TimeLimitSeconds / 60} мин.");
        return true;
    }

    private async Task TeleportInto(Player player, ActiveInstance instance, ClientConnection conn)
    {
        player.CurrentZoneId = instance.InstanceZoneId;
        player.X = instance.Template.SpawnX + instance.OffsetX;
        player.Y = instance.Template.SpawnY + instance.OffsetY;
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

    /// <summary>Спавн монстров из шаблона.</summary>
    private void SpawnMonsters(ActiveInstance instance, InstanceTemplate template)
    {
        using var conn = Db.OpenContent();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT monster_template_id, x, y, is_boss FROM instance_spawns WHERE instance_template_id = $id ORDER BY y";
        cmd.Parameters.AddWithValue("$id", template.Id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string monId = reader.GetString(0);
            int x = reader.GetInt32(1) + instance.OffsetX;
            int y = reader.GetInt32(2) + instance.OffsetY;
            bool isBoss = reader.GetBoolean(3);

            var tpl = _svc.World.GetMonsterTemplates().FirstOrDefault(t => t.Id == monId);
            if (tpl == null) continue;

            var monster = new Monster
            {
                TemplateId = tpl.Id,
                Name = tpl.Name,
                X = x,
                Y = y,
                Health = isBoss ? tpl.Health * 5 : tpl.Health,
                MaxHealth = isBoss ? tpl.Health * 5 : tpl.Health,
                XpReward = tpl.XpReward * (isBoss ? 3 : 1),
                GoldReward = tpl.GoldReward * (isBoss ? 3 : 1),
                Level = isBoss ? 15 : 5,
                ZoneId = instance.InstanceZoneId,
                Symbol = tpl.Symbol,
                Strength = tpl.Strength + (isBoss ? 4 : 0),
                Endurance = tpl.Endurance + (isBoss ? 3 : 0),
                Agility = tpl.Agility,
                Cunning = tpl.Cunning,
                Intellect = tpl.Intellect,
                Wisdom = tpl.Wisdom,
                CritChance = tpl.CritChance + (isBoss ? 2 : 0),
                CritDamage = tpl.CritDamage + (isBoss ? 0.5 : 0),
                EvadeChance = tpl.EvadeChance,
                SpawnX = x,
                SpawnY = y,
                WanderRadius = Balance.MonsterWanderRadius,
                AggroRange = isBoss ? 10 : 5,
                MoveIntervalMs = 1500,
                LastMoveTime = DateTime.UtcNow.AddMilliseconds(-Random.Shared.Next(0, 500))
            };
            instance.Monsters.Add(monster);
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

                _svc.Monsters.WanderStepForInstances(monsters, players, inst.Map.Width, inst.Map.Height);

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
        string exitZone = portal?.FromZone ?? "main";

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
        string exitZone = portal?.FromZone ?? "main";

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
                        Level = m.Level
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

        if (player.X != inst.Template.ChestX + inst.OffsetX || player.Y != inst.Template.ChestY + inst.OffsetY)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Подойдите к сундуку.");
            return false;
        }

        if (inst.ChestLocked)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Сундук заперт. Убейте босса.");
            return false;
        }

        // Ролл лута
        var templates = _svc.World.GetMonsterTemplates();
        var randomTpl = templates.Count > 0 ? templates[Random.Shared.Next(templates.Count)] : null;
        var loot = randomTpl != null ? _svc.Loot.RollLoot(randomTpl.Id) : new List<Item>();

        foreach (var item in loot)
        {
            InventoryHelper.AddItem(player, item);
        }

        // Золото
        int goldReward = 50 + Random.Shared.Next(51); // 50-100
        player.Gold += goldReward;

        await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система",
            $"Сундук открыт! Получено: {loot.Count} предметов, {goldReward} золота.");
        await _svc.Hub.SendStatusAsync(conn, player);
        Log.Info($"{player.Name} открыл сундук в инстансе {inst.Id}");
        return true;
    }
}
