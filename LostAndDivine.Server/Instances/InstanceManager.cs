using System.Text.RegularExpressions;
using LostAndDivine.Server.Repositories;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.Instances;

public class InstanceManager
{
    private readonly IGameServices _svc;
    private readonly Dictionary<Guid, ActiveInstance> _instances = new();
    private readonly object _lock = new();
    private readonly List<InstanceTemplate> _templates = new();
    private readonly List<InstancePortal> _portals = new();
    private readonly Dictionary<(string Zone, int X, int Y), InstancePortal> _portalLookup = new();
    private readonly List<InstanceInviteSession> _inviteSessions = new();
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

    /// <summary>Диапазон уровней данжа из названия шаблона («Подземелье (ур. 41-45)»).
    /// Если разобрать не удалось — ограничений нет.</summary>
    public static (int Min, int Max)? ParseLevelBracket(InstanceTemplate template)
    {
        var m = Regex.Match(template.Name, @"ур\.\s*(\d+)\s*-\s*(\d+)");
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }

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

    private Item? RollRewardWeapon(int playerLevel, int dungeonLevel, bool betterDrop = false)
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
        // Групповой инстанс: выше шанс лучшей экипировки (эпик/редкое)
        string qualityLabel = betterDrop
            ? (roll < 25 ? "Эпический" : roll < 55 ? "Редкий" : roll < 80 ? "Необычный" : "Обычный")
            : (roll < 15 ? "Эпический" : roll < 40 ? "Редкий" : roll < 70 ? "Необычный" : "Обычный");
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
    public async Task<bool> TryEnter(Player player, string templateId, ClientConnection conn, InstanceMode mode = InstanceMode.Solo)
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

        // Ограничение по уровню: доступны данж своего диапазона и на один ниже.
        // Например, игрок 50 уровня войдёт в 46-50 и 41-45, но не ниже.
        var bracket = ParseLevelBracket(template);
        if (bracket.HasValue)
        {
            int ownMin = ((player.Level - 1) / 5) * 5 + 1;
            if (bracket.Value.Min > player.Level || bracket.Value.Min < ownMin - 5)
            {
                await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система",
                    $"«{template.Name}» — для уровня {bracket.Value.Min}-{bracket.Value.Max}. Доступны данжи вашего уровня и на один ниже.");
                return false;
            }
        }

        ActiveInstance? existing;
        lock (_lock)
        {
            // Ищем существующий инстанс для пати игрока (только того же режима)
            existing = null;
            if (player.PartyId != null)
            {
                existing = _instances.Values.FirstOrDefault(inst =>
                    inst.Template.Id == templateId && inst.Mode == mode
                    && inst.Players.Any(p => p.PartyId == player.PartyId));
            }
            // Или инстанс, созданный этим игроком ранее
            existing ??= _instances.Values.FirstOrDefault(inst =>
                inst.Template.Id == templateId && inst.Mode == mode && inst.Players.Contains(player));

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
        return await CreateAndEnter(player, template, conn, mode);
    }

    /// <summary>Список шаблонов инстансов для окна выбора.</summary>
    public List<(string Id, string Name, int MinLevel, int MaxLevel)> GetInstanceList()
    {
        var list = new List<(string Id, string Name, int MinLevel, int MaxLevel)>();
        foreach (var t in _templates)
        {
            var b = ParseLevelBracket(t);
            list.Add((t.Id, t.Name, b?.Min ?? 0, b?.Max ?? 0));
        }
        return list;
    }

    private static bool IsLevelAllowed(InstanceTemplate template, Player player)
    {
        var bracket = ParseLevelBracket(template);
        if (!bracket.HasValue) return true;
        int ownMin = ((player.Level - 1) / 5) * 5 + 1;
        return bracket.Value.Min <= player.Level && bracket.Value.Min >= ownMin - 5;
    }

    /// <summary>Лидер группы приглашает всех членов в групповой инстанс.</summary>
    public async Task<bool> InviteParty(Player leader, string templateId, ClientConnection conn)
    {
        if (leader.PartyId == null)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Вы не в группе.");
            return false;
        }
        var party = _svc.Party.GetParty(leader.PartyId.Value);
        if (party == null || party.LeaderId != leader.Id)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Только лидер группы может запустить групповой инстанс.");
            return false;
        }
        var template = FindTemplate(templateId);
        if (template == null)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Этот инстанс не найден.");
            return false;
        }
        if (!IsLevelAllowed(template, leader))
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система",
                $"«{template.Name}» — не по вашему уровню. Доступны данжи вашего уровня и на один ниже.");
            return false;
        }

        var members = party.Members
            .Select(id => _svc.World.TryGetPlayer(id, out var m) && m != null ? m : null)
            .Where(m => m != null && m.Id != leader.Id)
            .ToList();
        if (members.Count == 0)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "В группе нет других игроков.");
            return false;
        }

        var session = new InstanceInviteSession(leader, templateId, template.Name);
        foreach (var m in members)
            session.Statuses[m.Id] = InstanceInviteStatus.Waiting;

        lock (_lock)
        {
            _inviteSessions.RemoveAll(s => s.Leader.Id == leader.Id && s.TemplateId == templateId);
            _inviteSessions.Add(session);
        }

        foreach (var m in members)
        {
            var mc = _svc.World.GetConnectionByPlayerName(m.Name);
            if (mc != null)
            {
                await _svc.Hub.SendToClient(mc, new GameMessage
                {
                    Type = "instance_invite_received",
                    Data = new { LeaderName = leader.Name, TemplateName = template.Name, TemplateId = templateId }
                });
            }
        }

        await SendInviteUpdate(leader, session);
        return true;
    }

    /// <summary>Ответ члена группы на приглашение (готов/отказ).</summary>
    public async Task RespondInvite(Player player, bool ready, ClientConnection conn)
    {
        InstanceInviteSession? session;
        lock (_lock)
        {
            session = _inviteSessions.FirstOrDefault(s => !s.Started && s.Statuses.ContainsKey(player.Id));
        }
        if (session == null)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Нет активного приглашения в инстанс.");
            return;
        }

        session.Statuses[player.Id] = ready ? InstanceInviteStatus.Ready : InstanceInviteStatus.Declined;
        await SendInviteUpdate(session.Leader, session);

        // Автостарт: все неотказавшиеся готовы
        if (session.Statuses.Values.All(s => s != InstanceInviteStatus.Waiting)
            && session.Statuses.Values.Any(s => s == InstanceInviteStatus.Ready))
        {
            await StartGroupSession(session);
        }
    }

    /// <summary>Ручной запуск группового инстанса лидером.</summary>
    public async Task StartGroup(Player leader, ClientConnection conn)
    {
        InstanceInviteSession? session;
        lock (_lock)
        {
            session = _inviteSessions.FirstOrDefault(s => !s.Started && s.Leader.Id == leader.Id);
        }
        if (session == null)
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Нет активного приглашения в инстанс.");
            return;
        }
        if (session.Statuses.Values.All(s => s != InstanceInviteStatus.Ready))
        {
            await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", "Никто из группы ещё не готов.");
            return;
        }
        await StartGroupSession(session);
    }

    private async Task StartGroupSession(InstanceInviteSession session)
    {
        lock (_lock) { session.Started = true; _inviteSessions.Remove(session); }

        var template = FindTemplate(session.TemplateId);
        if (template == null) return;

        var readyMembers = new List<Player>();
        foreach (var (mid, status) in session.Statuses)
        {
            if (status != InstanceInviteStatus.Ready) continue;
            if (!_svc.World.TryGetPlayer(mid, out var m) || m == null) continue;
            if (!IsLevelAllowed(template, m))
            {
                var mc = _svc.World.GetConnectionByPlayerName(m.Name);
                if (mc != null)
                    await _svc.Hub.SendChatToAsync(mc, ChatChannel.System, "Система",
                        $"«{template.Name}» — не по вашему уровню. Доступны данжи вашего уровня и на один ниже.");
                continue;
            }
            readyMembers.Add(m);
        }

        if (readyMembers.Count == 0)
        {
            await _svc.Hub.SendChatToAsync(
                _svc.World.GetConnectionByPlayerName(session.Leader.Name), ChatChannel.System, "Система",
                "Никто из готовых игроков не прошёл проверку уровня.");
            return;
        }

        var leaderConn = _svc.World.GetConnectionByPlayerName(session.Leader.Name);
        if (leaderConn == null) return;

        // Создаём один групповой инстанс и телепортируем всех готовых
        var instance = CreateInstance(session.Leader, template, InstanceMode.Group);
        foreach (var member in readyMembers)
        {
            lock (_lock)
            {
                if (!instance.Players.Contains(member))
                    instance.Players.Add(member);
            }
            var mc = _svc.World.GetConnectionByPlayerName(member.Name);
            if (mc == null) continue;
            await TeleportInto(member, instance, mc);
            await _svc.Hub.SendChatToAsync(mc, ChatChannel.System, "Система",
                $"Вход в групповой инстанс «{template.Name}». У вас {template.TimeLimitSeconds / 60} мин.");
        }
    }

    private async Task SendInviteUpdate(Player leader, InstanceInviteSession session)
    {
        var leaderConn = _svc.World.GetConnectionByPlayerName(leader.Name);
        if (leaderConn == null) return;
        var members = new List<object>();
        foreach (var (mid, status) in session.Statuses)
        {
            string name = _svc.World.TryGetPlayer(mid, out var m) && m != null ? m.Name : "?";
            members.Add(new { Name = name, Status = status.ToString().ToLowerInvariant() });
        }
        await _svc.Hub.SendToClient(leaderConn, new GameMessage
        {
            Type = "instance_invite_update",
            Data = new { TemplateName = session.TemplateName, Members = members }
        });
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

    private async Task<bool> CreateAndEnter(Player player, InstanceTemplate template, ClientConnection conn, InstanceMode mode = InstanceMode.Solo)
    {
        var instance = CreateInstance(player, template, mode);

        await TeleportInto(player, instance, conn);
        await _svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", $"Вход в «{template.Name}». У вас {template.TimeLimitSeconds / 60} мин.");
        return true;
    }

    /// <summary>Создаёт и регистрирует новый инстанс (без телепорта игрока).</summary>
    private ActiveInstance CreateInstance(Player player, InstanceTemplate template, InstanceMode mode)
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

        var instance = new ActiveInstance(template, map, mode);
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
        return instance;
    }

    private async Task TeleportInto(Player player, ActiveInstance instance, ClientConnection conn)
    {
        player.CurrentZoneId = instance.InstanceZoneId;
        player.X = instance._spawnX > 0 ? instance._spawnX : instance.Template.SpawnX + instance.OffsetX;
        player.Y = instance._spawnY > 0 ? instance._spawnY : instance.Template.SpawnY + instance.OffsetY;
        player.Combat.Cancel();
        player.Movement.Stop();

        await _svc.Hub.SendZoneTransition(conn, player);

        // Клиент закрывает окна (диалог стража, окно инстансов) после входа
        await _svc.Hub.SendToClient(conn, new GameMessage
        {
            Type = "instance_started",
            Data = new
            {
                TemplateName = instance.Template.Name,
                Mode = instance.Mode == InstanceMode.Group ? "group" : "solo"
            }
        });
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
        bool isGroup = instance.Mode == InstanceMode.Group;

        void DoSpawn(string monId, int x, int y, bool isBoss)
        {
            var tpl = _svc.World.GetMonsterTemplates().FirstOrDefault(t => t.Id == monId);
            if (tpl == null) return;

            int lvl = isBoss ? scaledLevel + 2 : scaledLevel;
            float scale = 1f + (lvl - 1) * 0.3f;
            // Групповой инстанс: босс заметно жирнее (HP ×2.5) и бьёт сильнее (сила ×1.5)
            float bossMult = isBoss && isGroup ? 2.5f : 1f;
            int hp = (int)(tpl.Health * scale * (isBoss ? 3 : 1) * bossMult);

            var monster = new Monster
            {
                TemplateId = tpl.Id, Name = tpl.Name,
                X = x, Y = y,
                Health = hp, MaxHealth = hp,
                Level = lvl,
                XpReward = (int)(tpl.XpReward * scale * (isBoss ? 3 : 1) * (isBoss && isGroup ? 1.5 : 1)),
                GoldReward = RollGold(tpl, scale * (isBoss ? 3 : 1) * (isBoss && isGroup ? 1.5 : 1)),
                ZoneId = instance.InstanceZoneId,
                Symbol = tpl.Symbol,
                Strength = (int)(tpl.Strength * scale * (isBoss && isGroup ? 1.5 : 1)) + (isBoss ? 3 : 0),
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
                // Групповой инстанс: обычных мобов в 2.5 раза больше (чередуем 3/2 копии)
                int copies = isGroup ? (i % 2 == 0 ? 3 : 2) : 1;
                for (int c = 0; c < copies; c++)
                {
                    int ox = c == 0 ? 0 : (c % 2 == 0 ? -1 : 1);
                    int oy = c == 0 ? 0 : (c < 3 ? 1 : -1);
                    DoSpawn(tpl.Id, Math.Clamp(sx + ox, 0, map.Width - 1), Math.Clamp(sy + oy, 0, map.Height - 1), false);
                }
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
            int mobCount = isGroup ? 15 : 6;
            int step = Math.Max(1, walkable.Count / (mobCount + 1));

            for (int i = 0; i < mobCount; i++)
            {
                int idx = i * step + rng.Next(step);
                if (idx >= walkable.Count) idx = walkable.Count - 1;
                while (spawnedIds.Contains(walkable[idx].ToString()) && idx < walkable.Count - 1) idx++;
                if (spawnedIds.Contains(walkable[idx].ToString())) continue;
                spawnedIds.Add(walkable[idx].ToString());
                var (sx, sy) = walkable[idx];
                var (monId, isBoss) = i == mobCount - 1 ? (template.BossMonsterId, true) : (_svc.World.GetMonsterTemplates()[rng.Next(_svc.World.GetMonsterTemplates().Count)].Id, false);
                DoSpawn(monId, sx, sy, isBoss);
            }
        }
    }

    /// <summary>Случайное золото за убийство в диапазоне [gold_reward, gold_max] с масштабом mult. gold_max=0 → ровно gold_reward.</summary>
    private static int RollGold(MonsterTemplate tpl, double mult)
    {
        int min = (int)(tpl.GoldReward * mult);
        int max = (int)(tpl.GoldMax * mult);
        if (tpl.GoldMax <= 0 || max <= min) return min;
        return Random.Shared.Next(min, max + 1);
    }

    /// <summary>Тик таймеров, ИИ монстров и очистка истёкших инстансов.</summary>
    public async Task TickAsync()
    {
        // Истёкшие приглашения в групповые инстансы
        List<InstanceInviteSession> expiredSessions;
        lock (_lock)
        {
            expiredSessions = _inviteSessions.Where(s => s.IsExpired).ToList();
            foreach (var s in expiredSessions)
                _inviteSessions.Remove(s);
        }
        foreach (var s in expiredSessions)
        {
            var lc = _svc.World.GetConnectionByPlayerName(s.Leader.Name);
            if (lc != null)
                await _svc.Hub.SendChatToAsync(lc, ChatChannel.System, "Система",
                    $"Приглашение в «{s.TemplateName}» истекло.");
        }

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
                    Items = inst.ChestLootItems.Select(i => ItemPayload(i)).ToList()
                }
            });
            return true;
        }

        // Награда — оружие, подобранное под уровень подземелья
        bool betterDrop = inst.Mode == InstanceMode.Group;
        var selectedItem = RollRewardWeapon(player.Level, ExtractLevelFromTemplateId(inst.Template.Id), betterDrop);
        if (selectedItem != null)
            inst.ChestLootItems.Add(selectedItem);

        // Золото (групповой инстанс — в 1.5 раза больше)
        int goldReward = (int)((50 + player.Level * 10 + Random.Shared.Next(51)) * (betterDrop ? 1.5 : 1));
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
                Items = inst.ChestLootItems.Select(i => ItemPayload(i)).ToList()
            }
        });
        Log.Info($"{player.Name} открыл сундук в инстансе {inst.Id}");
        return true;
    }

    private static object ItemPayload(Item i) => new
    {
        i.Id, i.TemplateId, i.Name, i.Type, i.WeaponSubtype, i.Quantity, i.Value,
        i.MaxHealthBonus, i.HealAmount, i.RestoreMana, i.Description, i.MaxStack,
        BonusStrength = i.BonusStrength, BonusEndurance = i.BonusEndurance,
        BonusAgility = i.BonusAgility, BonusCunning = i.BonusCunning,
        BonusIntellect = i.BonusIntellect, BonusWisdom = i.BonusWisdom,
        BonusPhysAttack = i.BonusPhysAttack, BonusMagAttack = i.BonusMagAttack,
        BonusDefense = i.BonusDefense, BonusResistance = i.BonusResistance,
        BonusCritChance = i.BonusCritChance, BonusCritDamage = i.BonusCritDamage,
        BonusEvadeChance = i.BonusEvadeChance, BonusAttackSpeed = i.BonusAttackSpeed,
        BonusBlockChance = i.BonusBlockChance, BonusParryChance = i.BonusParryChance,
        i.DamageType, i.RequiredLevel, i.DamageMin, i.DamageMax,
        i.AttackSpeedModifier, i.TwoHanded, i.AttackRange
    };
}
