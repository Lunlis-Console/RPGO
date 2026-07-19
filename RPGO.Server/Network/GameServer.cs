using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.Network;

/// <summary>
/// Реализация сетевого слоя сервера. Инкапсулирует отправку и рассылку
/// сообщений клиентам, ранее размазанные по статическому классу Program.
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
        List<ClientConnection> clientsCopy = _world.GetClientsSnapshot()
            .Where(c => c.Player != null).ToList();

        var allMonsters = MonsterManager.GetMonsterPositions();
        var allCollectibles = CollectibleManager.GetPositions();
        var allCorpses = CorpseManager.GetCorpsePositions();
        var merchant = new MerchantPosition
        {
            X = MerchantManager.MerchantX,
            Y = MerchantManager.MerchantY,
            Name = "Торговец"
        };
        var board = QuestManager.Board;

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
                Corpses = nearbyCorpses
            };

            await SendToClient(client, new GameMessage
            {
                Type = "map_update",
                Data = mapData
            });
        }
    }

    public async Task SendQuestLog(ClientConnection connection, Player player)
    {
        var quests = player.ActiveQuests.Select(q =>
        {
            var def = QuestManager.FindQuest(q.QuestId);
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
                Available = QuestManager.GetAvailableQuests().Select(d => new
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
        await BroadcastAsync(new GameMessage
        {
            Type = "chat",
            Data = new { Name = playerName, Text = text }
        });
    }

    public async Task BroadcastChatAsync(ChatChannel channel, string from, string text)
    {
        await BroadcastAsync(new GameMessage
        {
            Type = "chat",
            Data = new { Channel = channel.ToString(), Name = from, Text = text }
        });
    }

    public async Task SendChatToAsync(ClientConnection connection, ChatChannel channel, string from, string text, string? to = null)
    {
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Channel = channel.ToString(), Name = from, Text = text, To = to }
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
                WeaponName = player.Equipment.Weapon?.Name ?? "нет",
                ArmorName = player.Equipment.Armor?.Name ?? "нет",
                AccessoryName = player.Equipment.Accessory?.Name ?? "нет",
                player.Strength,
                player.Stamina,
                player.Agility,
                player.Cunning,
                player.Wisdom,
                player.Will,
                player.AttributePoints,
                player.Speed,
                MoveIntervalMs = Balance.MoveIntervalMs(player.Speed),
                AttackSpeed = GetAttackSpeed(player),
                AttackIntervalMs = Balance.AttackIntervalMs(GetAttackSpeed(player)),
                Breakdown = BuildBreakdown(player)
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
                    Weapon = player.Equipment.Weapon,
                    Armor = player.Equipment.Armor,
                    Accessory = player.Equipment.Accessory
                },
                BonusAttack = player.Equipment.GetBonusAttack(),
                BonusDefense = player.Equipment.GetBonusDefense(),
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
                WeaponName = player.Equipment.Weapon?.Name ?? "нет",
                ArmorName = player.Equipment.Armor?.Name ?? "нет",
                AccessoryName = player.Equipment.Accessory?.Name ?? "нет",
                player.Strength,
                player.Stamina,
                player.Agility,
                player.Cunning,
                player.Wisdom,
                player.Will,
                player.AttributePoints,
                player.Speed,
                MoveIntervalMs = Balance.MoveIntervalMs(player.Speed),
                AttackSpeed = GetAttackSpeed(player),
                AttackIntervalMs = Balance.AttackIntervalMs(GetAttackSpeed(player)),
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

    public StatsBreakdown BuildBreakdown(Player player)
    {
        return new StatsBreakdown
        {
            Attack = new BreakdownPart
            {
                Base = player.GetBaseDamage(),
                AttrBonus = (player.GetEffStrength() - 1) * 2,
                EquipBonus = player.Equipment.GetBonusAttack(),
                Total = player.GetTotalAttack()
            },
            Defense = new BreakdownPart
            {
                Base = player.GetBaseDefense(),
                AttrBonus = (player.GetEffStamina() - 1) * 1,
                EquipBonus = player.Equipment.GetBonusDefense(),
                Total = player.GetTotalDefense()
            },
            Crit = new BreakdownPart
            {
                Base = player.BaseCritChance,
                AttrBonus = (player.GetEffAgility() - 1) * 1.0,
                EquipBonus = player.Equipment.GetBonusCritChance(),
                Total = Math.Round(player.GetCritChance(), 2)
            },
            CritDmg = new BreakdownPart
            {
                Base = player.BaseCritDamage,
                AttrBonus = (player.GetEffStrength() - 1) * 0.05,
                EquipBonus = player.Equipment.GetBonusCritDamage(),
                Total = Math.Round(player.GetCritDamage(), 2)
            },
            Evade = new BreakdownPart
            {
                Base = player.BaseEvadeChance,
                AttrBonus = (player.GetEffAgility() - 1) * 1.0,
                EquipBonus = player.Equipment.GetBonusEvadeChance(),
                Total = Math.Round(player.GetEvadeChance(), 2)
            },
            Effective = new EffectiveAttrs
            {
                Strength = player.GetEffStrength(),
                Stamina = player.GetEffStamina(),
                Agility = player.GetEffAgility(),
                Cunning = player.GetEffCunning(),
                Wisdom = player.GetEffWisdom(),
                Will = player.GetEffWill()
            }
        };
    }

    private static int GetAttackSpeed(Player player)
    {
        // Делегируем существующей логике Program, чтобы не дублировать формулу.
        return Program.GetAttackSpeed(player);
    }
}
