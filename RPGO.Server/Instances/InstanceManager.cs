using RPGGame.Server.Repositories;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.Instances;

public class InstanceManager
{
    private GameServices _svc;
    private readonly Dictionary<Guid, ActiveInstance> _instances = new();
    private readonly object _lock = new();
    private readonly List<InstanceTemplate> _templates = new();
    private readonly List<InstancePortal> _portals = new();
    private readonly Dictionary<(string Zone, int X, int Y), InstancePortal> _portalLookup = new();

    public IReadOnlyList<InstanceTemplate> Templates => _templates;

    public InstanceManager(GameServices svc)
    {
        _svc = svc;
    }

    public void SetServices(GameServices svc) => _svc = svc;

    public void LoadAll()
    {
        _templates.Clear();
        _portals.Clear();
        _portalLookup.Clear();

        using var conn = Db.Open();
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
        player.X = instance.Template.SpawnX;
        player.Y = instance.Template.SpawnY;
        player.Combat.Cancel();
        player.Movement.Stop();

        await _svc.Hub.SendZoneTransition(conn, player);
    }

    /// <summary>Генерация карты-коридора.</summary>
    private static GameMap GenerateCorridorMap(InstanceTemplate template)
    {
        int w = template.CorridorWidth;
        int h = template.CorridorLength + 3; // +3 для комнаты босса + выход
        var map = new GameMap(w, h);

        var tiles = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Стены по бокам
                if (x == 0 || x == w - 1)
                {
                    tiles[y * w + x] = (byte)TileType.Wall;
                    map.AddObstacle(x, y);
                }
                // Босс-комната (нижние 3 ряда — шире, во всю ширину)
                else if (y >= template.CorridorLength)
                {
                    tiles[y * w + x] = (byte)TileType.Stone;
                }
                // Пол коридора
                else
                {
                    tiles[y * w + x] = (byte)TileType.Stone;
                }
            }
        }
        map.SetTiles(tiles);
        return map;
    }

    /// <summary>Спавн монстров из шаблона.</summary>
    private void SpawnMonsters(ActiveInstance instance, InstanceTemplate template)
    {
        using var conn = Db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT monster_template_id, x, y, is_boss FROM instance_spawns WHERE instance_template_id = $id ORDER BY y";
        cmd.Parameters.AddWithValue("$id", template.Id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string monId = reader.GetString(0);
            int x = reader.GetInt32(1);
            int y = reader.GetInt32(2);
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
                AggroRange = isBoss ? 10 : 5,
                MoveIntervalMs = 1500
            };
            instance.Monsters.Add(monster);
        }
    }

    /// <summary>Тик таймеров и очистка истёкших инстансов.</summary>
    public async Task TickAsync()
    {
        List<ActiveInstance> expired;
        lock (_lock)
        {
            expired = _instances.Values.Where(i => i.IsExpired).ToList();
            foreach (var inst in expired)
                _instances.Remove(inst.Id);
        }

        foreach (var inst in expired)
        {
            await KickAllPlayers(inst, "Время вышло. Инстанс закрыт.");
            Log.Info($"Инстанс {inst.Id} закрыт по таймеру");
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
                        Name = m.Name,
                        X = m.X,
                        Y = m.Y,
                        Health = m.Health,
                        MaxHealth = m.MaxHealth,
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

        if (player.X != inst.Template.ChestX || player.Y != inst.Template.ChestY)
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
        var randomTpl = templates.Count > 0 ? templates[new Random().Next(templates.Count)] : null;
        var loot = randomTpl != null ? _svc.Loot.RollLoot(randomTpl.Id) : new List<Item>();

        foreach (var item in loot)
        {
            InventoryHelper.AddItem(player, item);
        }

        // Золото
        int goldReward = 50 + new Random().Next(51); // 50-100
        player.Gold += goldReward;

        await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система",
            $"Сундук открыт! Получено: {loot.Count} предметов, {goldReward} золота.");
        await _svc.Hub.SendStatusAsync(conn, player);
        Log.Info($"{player.Name} открыл сундук в инстансе {inst.Id}");
        return true;
    }
}
