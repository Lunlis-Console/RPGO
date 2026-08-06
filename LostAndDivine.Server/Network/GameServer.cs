using System.Collections.Concurrent;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.Network;

/// <summary>
/// Реализация сетевого слоя сервера. Инкапсулирует отправку и рассылку
/// сообщений клиентам.
/// </summary>
public sealed class GameServer : INetworkHub
{
    private readonly GameWorld _world;
    private GameServices _svc = null!;
    private List<NpcPosition>? _npcCache;

    // Zone-level dirty tracking: only broadcast to clients in zones that changed
    private readonly ConcurrentDictionary<string, byte> _dirtyZones = new();

    public void MarkZoneDirty(string zoneId) => _dirtyZones[zoneId] = 0;

    public GameServer(GameWorld world)
    {
        _world = world;
    }

    public void SetServices(GameServices svc) => _svc = svc;

    public void LoadNpcCache()
    {
        _npcCache = BuildNpcCache(_svc);
    }

    /// <summary>
    /// Объединяет контент NPC из БД (имена, типы, диалоги) с позициями из Tiled-карт.
    /// Позиция берётся из Tiled-объекта (name = id записи npcs), иначе из БД как fallback.
    /// </summary>
    public static List<NpcPosition> BuildNpcCache(GameServices svc)
    {
        var result = new List<NpcPosition>();
        if (svc == null) return result;

        var tiled = svc.Zones.GetAllTiledNpcs();
        var tiledById = new Dictionary<string, TiledNpc>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tiled)
            if (!string.IsNullOrEmpty(t.Name) && !tiledById.ContainsKey(t.Name))
                tiledById[t.Name] = t;

        var tiledPortals = tiled
            .Where(t => string.Equals(t.Type, "instance_portal", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var dbNpcs = DatabaseManager.LoadNpcs();
        foreach (var n in dbNpcs)
        {
            TiledNpc? tiledNpc = null;
            if (string.Equals(n.Type, "instance_portal", StringComparison.OrdinalIgnoreCase))
                tiledNpc = tiledPortals.FirstOrDefault(); // позиция портала из Tiled (name там — id шаблона)
            else
                tiledById.TryGetValue(n.Id, out tiledNpc);

            result.Add(new NpcPosition
            {
                Id = n.Id,
                Name = n.Name,
                Type = n.Type,
                X = tiledNpc?.X ?? n.X,
                Y = tiledNpc?.Y ?? n.Y,
                ZoneId = tiledNpc?.ZoneId ?? Balance.MainZoneId,
                HasDialogue = svc.Dialogue.GetTree(n.Id) != null
            });
        }

        // Tiled-объекты без записи в БД (например, instance_portal без NPC-стражника)
        foreach (var t in tiled)
        {
            if (string.IsNullOrEmpty(t.Name)) continue;
            if (string.Equals(t.Type, "dummy", StringComparison.OrdinalIgnoreCase)) continue;

            bool matched = string.Equals(t.Type, "instance_portal", StringComparison.OrdinalIgnoreCase)
                ? dbNpcs.Any(n => string.Equals(n.Type, "instance_portal", StringComparison.OrdinalIgnoreCase))
                : result.Any(r => string.Equals(r.Id, t.Name, StringComparison.OrdinalIgnoreCase));
            if (matched) continue;

            if (string.Equals(t.Type, "instance_portal", StringComparison.OrdinalIgnoreCase))
            {
                var template = svc.Instances.FindTemplate(t.Name);
                result.Add(new NpcPosition
                {
                    Id = "portal_" + t.Name,
                    Name = template?.Name ?? "Вход в подземелье",
                    Type = "instance_portal",
                    X = t.X,
                    Y = t.Y,
                    ZoneId = t.ZoneId,
                    HasDialogue = false
                });
            }
            else
            {
                Log.Warn($"Tiled-объект NPC '{t.Name}' (type={t.Type}) не найден в таблице npcs — пропущен");
            }
        }

        return result;
    }

    /// <summary>Поиск NPC в кеше по зоне и позиции (позиции берутся из Tiled).</summary>
    public NpcPosition? FindNpcAt(string zoneId, int x, int y)
    {
        var cache = _npcCache;
        return cache?.FirstOrDefault(n => n.ZoneId == zoneId && n.X == x && n.Y == y);
    }

    public async Task BroadcastMapAsync()
    {
        var svc = _svc;
        List<ClientConnection> clientsCopy = _world.GetClientsSnapshot()
            .Where(c => c.Player != null && c.WelcomeSent).ToList();

        var allMonsters = svc.Monsters.GetMonsterPositions();
        allMonsters.AddRange(svc.Instances.GetAllMonstersPositions());
        var allCollectibles = svc.Collectibles.GetPositions();
        var allCorpses = svc.Corpses.GetCorpsePositions();
        var allNpcs = _npcCache ?? new List<NpcPosition>();
        var allHazards = svc.World.GetHazardsSnapshot();
        var allPlayers = clientsCopy.Where(c => c.Player != null)
            .Select(c => c.Player!).ToList();

        var merchant = new MerchantPosition
        {
            X = svc.Merchant.MerchantX,
            Y = svc.Merchant.MerchantY,
            Name = "Торговец"
        };
        var board = svc.Quests.Board;

        // Группировка по зонам (один раз, а не на клиента)
        var monstersByZone = allMonsters.GroupBy(m => m.ZoneId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var collectiblesByZone = allCollectibles.GroupBy(c => c.ZoneId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var corpsesByZone = allCorpses.GroupBy(c => c.ZoneId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var hazardsByZone = allHazards.GroupBy(h => h.ZoneId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var playersByZone = allPlayers.GroupBy(p => p.CurrentZoneId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var portalsByZone = svc.Zones.GetAllPortalsByZone();
        var npcsByZone = allNpcs.GroupBy(n => n.ZoneId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sendTasks = new List<Task>(clientsCopy.Count);
        bool hasDirtyZones = !_dirtyZones.IsEmpty;

        foreach (var client in clientsCopy)
        {
            var player = client.Player!;
            string zoneId = player.CurrentZoneId;

            // Skip clients in zones with no changes (unless first send)
            if (hasDirtyZones && !_dirtyZones.ContainsKey(zoneId) && client.HasTilesSent(zoneId))
                continue;

            var zone = svc.Zones.GetZone(zoneId);
            var zoneMap = svc.Zones.GetOrCreateMap(zoneId);
            int viewRadius = zoneMap.ViewRadius;
            bool isPvp = zone?.PvpEnabled ?? false;

            if (!monstersByZone.TryGetValue(zoneId, out var zoneMonsters)) zoneMonsters = new();
            if (!collectiblesByZone.TryGetValue(zoneId, out var zoneCollectibles)) zoneCollectibles = new();
            if (!corpsesByZone.TryGetValue(zoneId, out var zoneCorpses)) zoneCorpses = new();
            if (!hazardsByZone.TryGetValue(zoneId, out var zoneHazards)) zoneHazards = new();
            if (!playersByZone.TryGetValue(zoneId, out var zonePlayers)) zonePlayers = new();
            if (!portalsByZone.TryGetValue(zoneId, out var zonePortals)) zonePortals = new();
            if (!npcsByZone.TryGetValue(zoneId, out var zoneNpcs)) zoneNpcs = new();

            var nearbyMonsters = zoneMonsters.Where(m =>
                Math.Abs(m.X - player.X) <= viewRadius &&
                Math.Abs(m.Y - player.Y) <= viewRadius
            ).ToList();

            var nearbyCollectibles = zoneCollectibles.Where(c =>
                Math.Abs(c.X - player.X) <= viewRadius &&
                Math.Abs(c.Y - player.Y) <= viewRadius
            ).ToList();

            var nearbyCorpses = zoneCorpses.Where(c =>
                Math.Abs(c.X - player.X) <= viewRadius &&
                Math.Abs(c.Y - player.Y) <= viewRadius
            ).ToList();

            var sameZonePlayers = zonePlayers
                .Select(p => new PlayerPosition
                {
                    Id = p.Id,
                    Name = p.Name,
                    X = p.X,
                    Y = p.Y,
                    Level = p.Level,
                    Health = p.Health,
                    MaxHealth = p.MaxHealth,
                    Facing = p.Facing,
                    WeaponSubtype = p.Equipment.Slots.TryGetValue("rhand", out var rh) ? (rh?.WeaponSubtype ?? "") : "",
                    OffWeaponSubtype = p.Equipment.Slots.TryGetValue("lhand", out var lh) ? (lh?.WeaponSubtype ?? "") : "",
                    ShieldSubtype = (p.Equipment.Slots.TryGetValue("lhand", out var lsh) && lsh != null && lsh.Type == "shield" && !Equipment.IsCasterOffhand(lsh)) ? "shield" : "",
                    IsTwoHanded = p.Equipment.Slots.TryGetValue("rhand", out var rh2) && rh2 != null && rh2.TwoHanded,
                    IsDead = p.IsDead
                }).ToList();

            var portals = zonePortals
                .Select(p => new PortalPosition
                {
                    X = p.FromX,
                    Y = p.FromY,
                    TargetZone = p.ToZone,
                    TargetZoneName = svc.Zones.GetZone(p.ToZone)?.Name ?? p.ToZone
                })
                .ToList();

            var nearbyHazards = zoneHazards
                .Where(h => Math.Abs(h.X - player.X) <= viewRadius &&
                            Math.Abs(h.Y - player.Y) <= viewRadius)
                .Select(h => new HazardPosition
                {
                    Id = h.Id,
                    X = h.X,
                    Y = h.Y,
                    Kind = h.Kind.ToString(),
                    IsTriggered = h.AffectedIds.Count > 0,
                    ExpiresAtMs = new DateTimeOffset(h.ExpiresAt).ToUnixTimeMilliseconds()
                }).ToList();

            var             mapData = new WorldMap
            {
                Width = zoneMap.Width,
                Height = zoneMap.Height,
                Players = sameZonePlayers,
                Merchant = BuildMerchantForZone(zoneId, merchant),
                Board = BuildBoardForZone(zoneId, board),
                StorageChest = BuildStorageForZone(zoneId),
                Monsters = nearbyMonsters,
                Collectibles = nearbyCollectibles,
                Corpses = nearbyCorpses,
                Hazards = nearbyHazards,
                Npcs = !zoneId.StartsWith("instance:")
                    ? zoneNpcs.Where(n =>
                        Math.Abs(n.X - player.X) <= viewRadius &&
                        Math.Abs(n.Y - player.Y) <= viewRadius
                      ).Select(n => { n.QuestIndicator = GetQuestIndicator(n.Id, player); return n; }).ToList()
                    : new List<NpcPosition>(),
                ZoneId = zoneId,
                ZoneName = zone?.Name ?? zoneId,
                PvPEnabled = isPvp,
                Portals = portals,
                TileMapId = zoneId,
                TileData = client.HasTilesSent(zoneId) ? null : zoneMap.GetTiles(),
                ObstacleData = client.HasTilesSent(zoneId) ? null : zoneMap.GetObstacleData(),
                TileWidth = svc.Zones.GetTileConfig(zoneId).TileWidth,
                TileHeight = svc.Zones.GetTileConfig(zoneId).TileWidth,
                TilesetId = svc.Zones.GetTileConfig(zoneId).TilesetId,
                ObjectData = client.HasTilesSent(zoneId) ? null : zoneMap.GetObjectTiles(),
                ObjectTilesetId = svc.Zones.GetTileConfig(zoneId).ObjectTilesetId,
                ObjectTileWidth = svc.Zones.GetTileConfig(zoneId).ObjectTileWidth
            };
            client.MarkTilesSent(zoneId);

            if (zoneId.StartsWith("instance:"))
            {
                var inst = svc.Instances.FindInstanceByZoneId(zoneId);
                if (inst != null)
                {
                    mapData.InstanceExitPortal = new PortalPosition
                    {
                        X = inst.EffectiveExitX,
                        Y = inst.EffectiveExitY,
                        TargetZone = "",
                        TargetZoneName = svc.Zones.GetZone(Balance.MainZoneId)?.Name ?? "Главный мир"
                    };
                    mapData.InstanceChest = new ChestPosition
                    {
                        X = inst.EffectiveChestX,
                        Y = inst.EffectiveChestY,
                        IsLocked = inst.ChestLocked
                    };
                    mapData.InstanceExpiresAtUtcMs = new DateTimeOffset(inst.ExpiresAt).ToUnixTimeMilliseconds();
                }
            }

            sendTasks.Add(SendToClientSafe(client, new GameMessage
            {
                Type = "map_update",
                Data = mapData
            }));
        }

        await Task.WhenAll(sendTasks);
        _dirtyZones.Clear();
    }

    private async Task SendToClientSafe(ClientConnection client, GameMessage msg)
    {
        try { await SendToClient(client, msg); }
        catch { /* client disconnected — ignore */ }
    }

    private string? GetQuestIndicator(string npcId, Player player)
    {
        var svc = _svc;
        string? result = null;
        foreach (var def in svc.Quests.GetAvailableQuests())
        {
            if (def.TargetNpcId != npcId) continue;
            var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == def.Id);
            if (prog == null)
            {
                // «!» — квест можно взять (предусловия цепочки выполнены, и NPC реально его выдаёт)
                if (result == null && svc.Quests.CanTakeQuest(player, def) &&
                    svc.Dialogue.OffersQuest(npcId, def.Id))
                    result = "available";
            }
            else if (prog.Completed && result != "available")
            {
                result = "ready";
            }
            else if (result == null)
            {
                result = "active";
            }
        }
        return result;
    }

    public async Task SendQuestLog(ClientConnection connection, Player player)
    {
        var svc = _svc;
        var quests = player.ActiveQuests.Select(q =>
        {
            var def = svc.Quests.FindQuest(q.QuestId);
            return new
            {
                q.QuestId,
                Title = def?.Title ?? q.QuestId,
                Description = def?.Description ?? "",
                Type = def?.Type ?? "kill",
                Target = def?.Target ?? 0,
                XpReward = def?.XpReward ?? 0,
                GoldReward = def?.GoldReward ?? 0,
                q.Current,
                q.Completed
            };
        }).ToList();

        await SendToClient(connection, new GameMessage
        {
            Type = "quest_log",
            Data = new
            {
                Available = svc.Quests.GetAvailableQuests(player).Select(d => new
                {
                    QuestId = d.Id, d.Title, d.Description, d.Type, d.Target, d.XpReward, d.GoldReward,
                    d.ChainId, d.Step, d.PrerequisiteQuestId, d.MinLevel
                }).ToList(),
                Active = quests
            }
        });
    }

    public async Task SendHotbar(ClientConnection connection, Player player)
    {
        await SendToClient(connection, new GameMessage
        {
            Type = "hotbar_response",
            Data = new { Slots = player.HotbarSlots }
        });
    }

    public async Task SendSkills(ClientConnection connection)
    {
        var player = connection.Player;
        var skills = DatabaseManager.LoadSkills();
        await SendToClient(connection, new GameMessage
        {
            Type = "skills_response",
            Data = new
            {
                Skills = skills.Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Description,
                    s.Type,
                    s.MpCost,
                    s.CooldownMs,
                    s.DamageMultiplier,
                    s.MinLevel,
                    s.SkillPointCost,
                    s.ParentId,
                    s.Tier,
                    s.IconName,
                    s.MaxRank
                }).ToList(),
                LearnedSkills = player?.LearnedSkills ?? new(),
                SkillRanks = player?.SkillRanks ?? new(),
                SkillPoints = player?.SkillPoints ?? 0
            }
        });
    }

    public async Task BroadcastChatAsync(string playerName, string text)
    {
        bool isAdmin = _world.TryGetPlayerByName(playerName, out var sender) && sender!.IsAdmin;
        await BroadcastAsync(new GameMessage
        {
            Type = "chat",
            Data = new { Name = playerName, Text = text, IsAdmin = isAdmin }
        });
    }

    public async Task BroadcastChatAsync(ChatChannel channel, string from, string text)
    {
        bool isAdmin = _world.TryGetPlayerByName(from, out var sender) && sender!.IsAdmin;
        await BroadcastAsync(new GameMessage
        {
            Type = "chat",
            Data = new { Channel = channel.ToString(), Name = from, Text = text, IsAdmin = isAdmin }
        });
    }

    public async Task SendChatToAsync(ClientConnection connection, ChatChannel channel, string from, string text, string? to = null)
    {
        bool isAdmin = _world.TryGetPlayerByName(from, out var sender) && sender!.IsAdmin;
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Channel = channel.ToString(), Name = from, Text = text, To = to, IsAdmin = isAdmin }
        });
    }

    public async Task SendStatusAsync(ClientConnection connection, Player player)
    {
        await SendToClient(connection, new GameMessage
        {
            Type = "status_response",
            Data = new
            {
                player.Name,
                player.Level,
                player.Health,
                MaxHealth = player.MaxHealth + player.Equipment.GetBonusMaxHealth(),
                Mana = player.Mana,
                MaxMana = player.MaxMana,
                PhysAttack = GetBuffedPhysAttack(player, _svc.Debuffs),
                MagAttack = GetBuffedMagAttack(player, _svc.Debuffs),
                Defense = player.GetDefense(),
                Resistance = player.GetResistance(),
                CritChance = Math.Round(player.GetCritChance(), 2),
                CritDamage = Math.Round(player.GetCritDamage(), 2),
                EvadeChance = Math.Round(player.GetEvadeChance(), 2),
                BlockChance = Math.Round(player.GetBlockChance(), 2),
                ParryChance = Math.Round(player.GetParryChance(), 2),
                player.Gold,
                player.X,
                player.Y,
                player.Experience,
                Equipped = BuildEquipped(player),
                player.Strength,
                player.Endurance,
                player.Agility,
                player.Cunning,
                player.Intellect,
                player.Wisdom,
                player.AttributePoints,
                player.SkillPoints,
                MoveIntervalMs = Balance.MoveIntervalMs(player.Speed),
                AttackSpeed = GetAttackSpeed(player, _svc.Debuffs),
                AttackIntervalMs = GetAttackIntervalMs(player, _svc.Debuffs),
                AttackRange = player.GetEffectiveAttackRange(),
                WeaponDamageType = player.Equipment.GetWeaponDamageType(),
                WeaponSpeedModifier = player.Equipment.GetWeaponSpeedModifier(),
                Breakdown = BuildBreakdown(player),
                ActiveDebuffs = player.GetDebuffsSnapshot().Select(d => new
                {
                    Type = d.Type.ToString(),
                    d.DisplayName,
                    d.Description,
                    Value = Math.Round(d.Value, 2),
                    d.RemainingMs,
                    DurationMs = d.DurationMs
                }).ToList()
            }
        });
    }

    public async Task SendInventoryAndStatus(ClientConnection connection, Player player, bool fromUnequip = false)
    {
        _svc.Debuffs.RefreshDualWieldBuff(player);
        await SendToClient(connection, new GameMessage
        {
            Type = "inventory_response",
            Data = new
            {
                PlayerLevel = player.Level,
                Items = player.Inventory,
                Gold = player.Gold,
                Equipment = new
                {
                    Slots = BuildEquipSlots(player)
                },
                BonusPhysAttack = player.Equipment.GetBonusPhysAttack(),
                BonusMagAttack = player.Equipment.GetBonusMagAttack(),
                BonusDefense = player.Equipment.GetBonusDefense(),
                BonusResistance = player.Equipment.GetBonusResistance(),
                BonusMaxHealth = player.Equipment.GetBonusMaxHealth(),
                FromUnequip = fromUnequip
            }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "status_response",
            Data = new
            {
                player.Name,
                player.Level,
                player.Health,
                MaxHealth = player.MaxHealth + player.Equipment.GetBonusMaxHealth(),
                Mana = player.Mana,
                MaxMana = player.MaxMana,
                PhysAttack = GetBuffedPhysAttack(player, _svc.Debuffs),
                MagAttack = GetBuffedMagAttack(player, _svc.Debuffs),
                Defense = player.GetDefense(),
                Resistance = player.GetResistance(),
                CritChance = Math.Round(player.GetCritChance(), 2),
                CritDamage = Math.Round(player.GetCritDamage(), 2),
                EvadeChance = Math.Round(player.GetEvadeChance(), 2),
                BlockChance = Math.Round(player.GetBlockChance(), 2),
                ParryChance = Math.Round(player.GetParryChance(), 2),
                player.Gold,
                player.X,
                player.Y,
                player.Experience,
                Equipped = BuildEquipped(player),
                player.Strength,
                Endurance = player.Endurance,
                player.Agility,
                player.Cunning,
                Intellect = player.Intellect,
                player.Wisdom,
                player.AttributePoints,
                player.SkillPoints,
                MoveIntervalMs = Balance.MoveIntervalMs(player.Speed),
                AttackSpeed = GetAttackSpeed(player, _svc.Debuffs),
                AttackIntervalMs = GetAttackIntervalMs(player, _svc.Debuffs),
                WeaponDamageType = player.Equipment.GetWeaponDamageType(),
                WeaponSpeedModifier = player.Equipment.GetWeaponSpeedModifier(),
                Breakdown = BuildBreakdown(player)
            }
        });
    }

    public async Task SendToClient(ClientConnection connection, GameMessage message)
    {
        try
        {
            if (!connection.Client.Connected) return;
            await connection.WriteLock.WaitAsync();
            try
            {
                await NetworkHelper.SendAsync(connection.Client.GetStream(), message);
            }
            finally
            {
                connection.WriteLock.Release();
            }
        }
        catch { /* client disconnected or send failed — expected */ }
    }

    public async Task SendToAllAsync(GameMessage message)
    {
        foreach (var client in _world.GetClientsSnapshot())
        {
            await SendToClient(client, message);
        }
    }

    public Task SendError(ClientConnection connection, string code, string message)
        => SendToClient(connection, new GameMessage
        {
            Type = "error",
            Data = new { Code = code, Message = message }
        });

    private async Task BroadcastAsync(GameMessage message)
    {
        List<ClientConnection> clientsCopy = _world.GetClientsSnapshot();

        foreach (var client in clientsCopy)
        {
            await SendToClient(client, message);
        }
    }

    public async Task SendDamageNearbyAsync(int x, int y, GameMessage damageMsg, Player? exclude)
    {
        int viewRadius = _world.Map.ViewRadius;
        List<ClientConnection> clientsCopy = _world.GetClientsSnapshot()
            .Where(c => c.Player != null && c.Player != exclude
                && Math.Abs(c.Player!.X - x) <= viewRadius
                && Math.Abs(c.Player.Y - y) <= viewRadius).ToList();

        foreach (var client in clientsCopy)
        {
            await SendToClient(client, damageMsg);
        }
    }

    public async Task SendFriendListToAsync(ClientConnection connection, Player player)
    {
        var names = DatabaseManager.GetFriendNames(player.Name);
        var onlineNames = new HashSet<string>(
            _world.GetPlayersSnapshot().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        var friends = new List<FriendInfo>();
        foreach (var name in names)
        {
            var info = new FriendInfo { Name = name, Online = onlineNames.Contains(name) };
            var pl = _world.GetPlayersSnapshot().FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (pl != null) info.Level = pl.Level;
            friends.Add(info);
        }

        await SendToClient(connection, new GameMessage
        {
            Type = "friend_list",
            Data = new FriendListData { Friends = friends }
        });
    }

    private static Dictionary<string, string> BuildEquipped(Player player) =>
        player.Equipment.Slots
            .Where(kv => kv.Value != null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!.Name);

    private static Dictionary<string, Item> BuildEquipSlots(Player player) =>
        player.Equipment.Slots
            .Where(kv => kv.Value != null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);

    public StatsBreakdown BuildBreakdown(Player player)
    {
        return new StatsBreakdown
        {
            PhysAttack = new BreakdownPart
            {
                Base = player.GetBaseDamage(),
                AttrBonus = (player.GetEffStrength() - 1) * BalanceStatic.AttackPerStrength
                           + (player.GetEffAgility() - 1) * BalanceStatic.AttackPerAgility,
                EquipBonus = player.Equipment.GetBonusPhysAttack(),
                WeaponDamageMin = player.Equipment.GetWeaponDamageRange().min,
                WeaponDamageMax = player.Equipment.GetWeaponDamageRange().max,
                Total = player.GetTotalAttack()
            },
            MagAttack = new BreakdownPart
            {
                Base = player.GetBaseDamage(),
                AttrBonus = (player.GetEffIntellect() - 1) * BalanceStatic.AttackPerIntellect,
                EquipBonus = player.Equipment.GetBonusMagAttack(),
                Total = player.GetMagAttack()
            },
            Defense = new BreakdownPart
            {
                Base = player.GetBaseDefense(),
                AttrBonus = (player.GetEffEndurance() - 1) * BalanceStatic.DefensePerEndurance,
                EquipBonus = player.Equipment.GetBonusDefense(),
                Total = player.GetDefense()
            },
            Resistance = new BreakdownPart
            {
                Base = player.GetBaseDefense(),
                AttrBonus = (player.GetEffWisdom() - 1) * BalanceStatic.ResistancePerWisdom,
                EquipBonus = player.Equipment.GetBonusResistance(),
                Total = player.GetResistance()
            },
            Crit = new BreakdownPart
            {
                Base = player.BaseCritChance,
                AttrBonus = (player.GetEffCunning() - 1) * BalanceStatic.CritChancePerCunning,
                EquipBonus = player.Equipment.GetBonusCritChance(),
                Total = Math.Round(player.GetCritChance(), 2)
            },
            CritDmg = new BreakdownPart
            {
                Base = player.BaseCritDamage,
                AttrBonus = (player.GetEffStrength() - 1) * BalanceStatic.CritDamagePerStrength,
                EquipBonus = player.Equipment.GetBonusCritDamage(),
                Total = Math.Round(player.GetCritDamage(), 2)
            },
            Evade = new BreakdownPart
            {
                Base = player.BaseEvadeChance,
                AttrBonus = (player.GetEffCunning() - 1) * BalanceStatic.EvadeChancePerCunning,
                EquipBonus = player.Equipment.GetBonusEvadeChance(),
                Total = Math.Round(player.GetEvadeChance(), 2)
            },
            Block = new BreakdownPart
            {
                Base = player.BaseBlockChance,
                AttrBonus = (player.GetEffEndurance() - 1) * BalanceStatic.BlockChancePerEndurance,
                EquipBonus = player.Equipment.GetBonusBlockChance(),
                Total = Math.Round(player.GetBlockChance(), 2)
            },
            Parry = new BreakdownPart
            {
                Base = player.BaseParryChance,
                AttrBonus = (player.GetEffAgility() - 1) * BalanceStatic.ParryChancePerAgility,
                EquipBonus = player.Equipment.GetBonusParryChance(),
                SkillBonus = player.GetReflexesParryBonus(),
                Total = Math.Round(player.GetParryChance(), 2)
            },
            Effective = new EffectiveAttrs
            {
                Strength = player.GetEffStrength(),
                Endurance = player.GetEffEndurance(),
                Agility = player.GetEffAgility(),
                Cunning = player.GetEffCunning(),
                Intellect = player.GetEffIntellect(),
                Wisdom = player.GetEffWisdom()
            }
        };
    }

    internal static double GetAttackSpeed(Player player, DebuffManager debuffs)
    {
        double baseSpeed = Balance.GetAttackSpeedWithWeapon(player.Agility, player.Equipment.GetWeaponSpeedModifier());
        double speedBuff = 1.0 + debuffs.GetDebuffValue(player, DebuffType.AttackSpeedBonus);
        return baseSpeed * speedBuff;
    }

    internal static int GetAttackIntervalMs(Player player, DebuffManager debuffs)
    {
        int baseInterval = Balance.AttackIntervalMs(
            Balance.GetAttackSpeed(player.Agility), player.Equipment.GetWeaponSpeedModifier());
        double speedBuff = 1.0 + debuffs.GetDebuffValue(player, DebuffType.AttackSpeedBonus);
        return (int)(baseInterval / speedBuff);
    }

    internal static int GetBuffedPhysAttack(Player player, DebuffManager debuffs)
    {
        int base_ = player.GetTotalAttack();
        double dmgBonus = debuffs.GetDebuffValue(player, DebuffType.DamageBonus);
        return (int)(base_ * (1.0 + dmgBonus));
    }

    internal static int GetBuffedMagAttack(Player player, DebuffManager debuffs)
    {
        int base_ = player.GetMagAttack();
        double dmgBonus = debuffs.GetDebuffValue(player, DebuffType.DamageBonus);
        return (int)(base_ * (1.0 + dmgBonus));
    }

    public async Task KickPlayer(ClientConnection connection, string reason)
    {
        if (connection.SessionToken != null)
            SessionManager.Revoke(connection.SessionToken);
        connection.SessionToken = null;
        await SendToClient(connection, new GameMessage
        {
            Type = "kick",
            Data = new { Reason = reason }
        });
        _world.DisconnectPlayer(connection);
    }

    public async Task SendZoneTransition(ClientConnection connection, Player player)
    {
        // Клиент хранит один буфер тайлов; при смене зоны всегда должны прийти полные
        // тайлы новой зоны. Сброс кэша исключает гонку с BroadcastMapAsync: иначе при
        // возврате в уже посещённую зону клиент недополучал тайлы (белые «куски»).
        connection.ResetTilesSent();
        var zone = _svc.Zones.GetZone(player.CurrentZoneId);
        var zoneMap = _svc.Zones.GetOrCreateMap(player.CurrentZoneId);
        await SendToClient(connection, new GameMessage
        {
            Type = "zone_transition",
            Data = new
            {
                ZoneId = player.CurrentZoneId,
                ZoneName = zone?.Name ?? player.CurrentZoneId,
                X = player.X,
                Y = player.Y,
                PvPEnabled = zone?.PvpEnabled ?? false,
                Width = zoneMap.Width,
                Height = zoneMap.Height,
                TileData = zoneMap.GetTiles(),
                ObstacleData = zoneMap.GetObstacleData(),
                TileWidth = _svc.Zones.GetTileConfig(player.CurrentZoneId).TileWidth,
                TileHeight = _svc.Zones.GetTileConfig(player.CurrentZoneId).TileWidth,
                TilesetId = _svc.Zones.GetTileConfig(player.CurrentZoneId).TilesetId,
                ObjectData = zoneMap.GetObjectTiles(),
                ObjectTilesetId = _svc.Zones.GetTileConfig(player.CurrentZoneId).ObjectTilesetId,
                ObjectTileWidth = _svc.Zones.GetTileConfig(player.CurrentZoneId).ObjectTileWidth
            }
        });
    }

    private static MerchantPosition? BuildMerchantForZone(string zoneId, MerchantPosition defaultMerchant)
    {
        var svc = Program.Services;
        if (svc == null) return defaultMerchant;
        var tiled = svc.Zones.GetTiledNpcs(zoneId);
        var mt = tiled.FirstOrDefault(n =>
            string.Equals(n.Type, "merchant", StringComparison.OrdinalIgnoreCase));
        return mt != null
            ? new MerchantPosition { X = mt.X, Y = mt.Y, Name = defaultMerchant.Name }
            : defaultMerchant;
    }

    private static QuestBoardPosition? BuildBoardForZone(string zoneId, QuestBoardPosition? defaultBoard)
    {
        var svc = Program.Services;
        if (svc == null || defaultBoard == null) return defaultBoard;
        var tiled = svc.Zones.GetTiledNpcs(zoneId);
        var bt = tiled.FirstOrDefault(n =>
            string.Equals(n.Type, "board", StringComparison.OrdinalIgnoreCase));
        return bt != null
            ? new QuestBoardPosition { X = bt.X, Y = bt.Y, Name = defaultBoard.Name }
            : defaultBoard;
    }

    private static ChestPosition? BuildStorageForZone(string zoneId)
    {
        var svc = Program.Services;
        if (svc == null) return null;
        var tiled = svc.Zones.GetTiledNpcs(zoneId);
        var st = tiled.FirstOrDefault(n =>
            string.Equals(n.Type, "storage", StringComparison.OrdinalIgnoreCase));
        return st != null
            ? new ChestPosition { X = st.X, Y = st.Y, IsLocked = false }
            : null;
    }
}
