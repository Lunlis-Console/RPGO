using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.Network;

/// <summary>
/// Реализация сетевого слоя сервера. Инкапсулирует отправку и рассылку
/// сообщений клиентам.
/// </summary>
public sealed class GameServer : INetworkHub
{
    private readonly GameWorld _world;

    public GameServer(GameWorld world)
    {
        _world = world;
    }

    public async Task BroadcastMapAsync()
    {
        var svc = Program.Services;
        List<ClientConnection> clientsCopy = _world.GetClientsSnapshot()
            .Where(c => c.Player != null).ToList();

        var allMonsters = svc.Monsters.GetMonsterPositions();
        var allCollectibles = svc.Collectibles.GetPositions();
        var allCorpses = svc.Corpses.GetCorpsePositions();
        var allNpcs = DatabaseManager.LoadNpcs().Select(n => new NpcPosition
        {
            Id = n.Id, Name = n.Name, Type = n.Type, X = n.X, Y = n.Y,
            HasDialogue = svc.Dialogue.GetTree(n.Id) != null
        }).ToList();
        var merchant = new MerchantPosition
        {
            X = svc.Merchant.MerchantX,
            Y = svc.Merchant.MerchantY,
            Name = "Торговец"
        };
        var board = svc.Quests.Board;

        foreach (var client in clientsCopy)
        {
            var player = client.Player!;
            int viewRadius = _world.Map.ViewRadius;
            var nearbyMonsters = allMonsters.Where(m =>
                Math.Abs(m.X - player.X) <= viewRadius &&
                Math.Abs(m.Y - player.Y) <= viewRadius
            ).ToList();

            var nearbyCollectibles = allCollectibles.Where(c =>
                Math.Abs(c.X - player.X) <= viewRadius &&
                Math.Abs(c.Y - player.Y) <= viewRadius
            ).ToList();

            var nearbyCorpses = allCorpses.Where(c =>
                Math.Abs(c.X - player.X) <= viewRadius &&
                Math.Abs(c.Y - player.Y) <= viewRadius
            ).ToList();

            var allPlayerPositions = clientsCopy.Select(c => new PlayerPosition
            {
                Id = c.Player!.Id,
                Name = c.Player!.Name,
                X = c.Player!.X,
                Y = c.Player!.Y,
                Level = c.Player!.Level,
                Health = c.Player!.Health,
                MaxHealth = c.Player!.MaxHealth
            }).ToList();

            var mapData = new WorldMap
            {
                Width = _world.Map.Width,
                Height = _world.Map.Height,
                Players = allPlayerPositions,
                Merchant = merchant,
                Board = board,
                Monsters = nearbyMonsters,
                Collectibles = nearbyCollectibles,
                Corpses = nearbyCorpses,
                Npcs = allNpcs.Where(n =>
                    Math.Abs(n.X - player.X) <= viewRadius &&
                    Math.Abs(n.Y - player.Y) <= viewRadius
                ).Select(n => { n.QuestIndicator = GetQuestIndicator(n.Id, player); return n; }).ToList()
            };

            await SendToClient(client, new GameMessage
            {
                Type = "map_update",
                Data = mapData
            });
        }
    }

    private static string? GetQuestIndicator(string npcId, Player player)
    {
        var svc = Program.Services;
        string? result = null;
        foreach (var def in svc.Quests.GetAvailableQuests())
        {
            if (def.TargetNpcId != npcId) continue;
            var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == def.Id);
            if (prog == null)
            {
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
        var svc = Program.Services;
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
                Available = svc.Quests.GetAvailableQuests().Select(d => new
                {
                    QuestId = d.Id, d.Title, d.Description, d.Type, d.Target, d.XpReward, d.GoldReward
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
        var skills = DatabaseManager.LoadSkills();
        await SendToClient(connection, new GameMessage
        {
            Type = "skills_response",
            Data = new
            {
                Skills = skills.Select(s => new
                {
                    s.Id, s.Name, s.Description, s.Type,
                    s.MpCost, s.CooldownMs, s.DamageMultiplier, s.MinLevel
                }).ToList()
            }
        });
    }

    public async Task BroadcastChatAsync(string playerName, string text)
    {
        bool isAdmin = _world.TryGetPlayerByName(playerName, out var sender) && sender.IsAdmin;
        await BroadcastAsync(new GameMessage
        {
            Type = "chat",
            Data = new { Name = playerName, Text = text, IsAdmin = isAdmin }
        });
    }

    public async Task BroadcastChatAsync(ChatChannel channel, string from, string text)
    {
        bool isAdmin = _world.TryGetPlayerByName(from, out var sender) && sender.IsAdmin;
        await BroadcastAsync(new GameMessage
        {
            Type = "chat",
            Data = new { Channel = channel.ToString(), Name = from, Text = text, IsAdmin = isAdmin }
        });
    }

    public async Task SendChatToAsync(ClientConnection connection, ChatChannel channel, string from, string text, string? to = null)
    {
        bool isAdmin = _world.TryGetPlayerByName(from, out var sender) && sender.IsAdmin;
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
                BaseAttack = player.GetBaseDamage(),
                BaseDefense = player.GetBaseDefense(),
                TotalAttack = player.GetTotalAttack(),
                TotalDefense = player.GetTotalDefense(),
                CritChance = Math.Round(player.GetCritChance(), 2),
                CritDamage = Math.Round(player.GetCritDamage(), 2),
                EvadeChance = Math.Round(player.GetEvadeChance(), 2),
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
                player.Speed,
                MoveIntervalMs = Balance.MoveIntervalMs(player.Speed),
                AttackSpeed = GetAttackSpeed(player),
                AttackIntervalMs = Balance.AttackIntervalMs(Balance.GetAttackSpeed(player.Agility), player.Equipment.GetWeaponSpeedModifier()),
                WeaponDamageType = player.Equipment.GetWeaponDamageType(),
                WeaponSpeedModifier = player.Equipment.GetWeaponSpeedModifier(),
                Breakdown = BuildBreakdown(player),
                ActiveDebuffs = player.ActiveDebuffs.Select(d => new
                {
                    Type = d.Type.ToString(),
                    d.DisplayName,
                    Value = Math.Round(d.Value, 2),
                    d.RemainingMs,
                    DurationMs = d.DurationMs
                }).ToList()
            }
        });
    }

    public async Task SendInventoryAndStatus(ClientConnection connection, Player player)
    {
        await SendToClient(connection, new GameMessage
        {
            Type = "inventory_response",
            Data = new
            {
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
                BonusMaxHealth = player.Equipment.GetBonusMaxHealth()
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
                PhysAttack = player.GetTotalAttack(),
                MagAttack = player.GetMagAttack(),
                Defense = player.GetDefense(),
                Resistance = player.GetResistance(),
                CritChance = Math.Round(player.GetCritChance(), 2),
                CritDamage = Math.Round(player.GetCritDamage(), 2),
                EvadeChance = Math.Round(player.GetEvadeChance(), 2),
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
                player.Speed,
                MoveIntervalMs = Balance.MoveIntervalMs(player.Speed),
                AttackSpeed = GetAttackSpeed(player),
                AttackIntervalMs = Balance.AttackIntervalMs(Balance.GetAttackSpeed(player.Agility), player.Equipment.GetWeaponSpeedModifier()),
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

    private static int GetAttackSpeed(Player player)
    {
        return Balance.GetAttackSpeedWithWeapon(player.Agility, player.Equipment.GetWeaponSpeedModifier());
    }

    public async Task KickPlayer(ClientConnection connection, string reason)
    {
        await SendToClient(connection, new GameMessage
        {
            Type = "disconnect",
            Data = new { Reason = reason }
        });
        _world.DisconnectPlayer(connection);
    }
}
