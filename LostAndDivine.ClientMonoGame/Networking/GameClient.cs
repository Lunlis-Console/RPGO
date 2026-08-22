using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.ClientMonoGame.Windows;
using LostAndDivine.ClientMonoGame.Screens;
using System.Text.Json;

namespace LostAndDivine.ClientMonoGame.Networking;

public static class GameMessageExtensions
{
    public static T? Deserialize<T>(this GameMessage message)
    {
        if (message.Data is JsonElement el)
            return JsonSerializer.Deserialize<T>(el.GetRawText());
        if (message.Data is T t)
            return t;
        return default;
    }
}

/// <summary>
/// Игровой клиент: держит состояние игрока и диспетчеризует сообщения сервера.
/// Не зависит от UI — UI подписывается на события.
/// Порт из Avalonia ClientGameClient с заменой Dispatcher.UIThread.Post на Action колбэк.
/// </summary>
public sealed class GameClient
{
    private Action? _uiAction;
    internal int _skillPoints;

    public int SkillPoints => _skillPoints;

    // Состояние игрока
    public string PlayerName { get; internal set; } = "Игрок";
    public string PlayerClass { get; internal set; } = "";
    public int PlayerLevel { get; internal set; } = 1;
    public WorldMap? CurrentMap { get; internal set; }
    public StatusData? Status { get; internal set; }
    public InventoryData? Inventory { get; internal set; }
    public List<QuestInfo> AvailableQuests { get; internal set; } = new();
    public List<QuestInfo> ActiveQuests { get; internal set; } = new();
    public List<QuestInfo> HistoryQuests { get; internal set; } = new();

    public bool IsConnected { get; set; }
    public string? SessionToken { get; internal set; }
    public Guid PlayerId { get; internal set; }

    /// <summary>Последний полученный список изменений «Что нового» (для показа после входа).</summary>
    public ChangelogData? LastChangelog { get; internal set; }

    // События (UI подписывается)
    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<string>? SystemMessage;
    public event Action<string, string, string, bool>? ChatReceived;
    public event Action? WelcomeReceived;
    public event Action<ChangelogData>? ChangelogReceived;
    public event Action<WorldMap>? MapUpdated;
    public event Action<EntityStateMessage>? EntityStateReceived;
    public event Action<StatusData>? StatusUpdated;
    public event Action<InventoryData>? InventoryUpdated;
    public event Action<List<QuestInfo>, List<QuestInfo>, List<QuestInfo>>? QuestLogUpdated;
    public event Action<string>? ErrorReceived;
    public event Action<GameMessage>? UnknownMessage;
    public event Action<int, int, string, uint, bool>? FloatingTextReceived;
    public event Action<ShopData>? ShopUpdated;
    public event Action<TradeOpenData>? TradeOpened;
    public event Action<TradeOfferData>? TradeOfferUpdated;
    public event Action<TradeConfirmData>? TradeConfirmUpdated;
    public event Action<TradeCompleteData>? TradeCompleted;
    public event Action<string>? TradeClosed;

    // Диалоги
    public event Action<string, string, string, List<(string Text, int Index)>>? DialogueOpened;
    public event Action? DialogueClosed;

    // Друзья
    public event Action<List<FriendInfo>>? FriendListUpdated;
    public event Action<bool, string>? FriendResultReceived;

    // HUD
    public event Action<bool, string?, int, int, string?>? CombatStateUpdated;
    public event Action<List<DebuffInfo>?>? TargetDebuffsUpdated;
    public event Action<PartyInfo>? PartyUpdated;
    public event Action? PartyDisbanded;
    public event Action<string, string>? PartyInviteReceived;
    public event Action<string>? TradeRequestReceived;
    public event Action<List<ClientSkillInfo>>? SkillsUpdated;

    // Инстансы
    public event Action? InstanceWindowOpened;
    public event Action<List<InstanceInfo>>? InstanceListReceived;
    public event Action<string, string, string>? InstanceInviteReceived; // (leaderName, templateName, templateId)
    public event Action<string, List<InstanceMemberInfo>>? InstanceInviteUpdate; // (templateName, members)
    public event Action<string, string>? InstanceStarted; // (templateName, mode)

    // Зоны
    public event Action<string, string, bool>? ZoneChanged;
    public event Action<byte[], int, int, string, int>? TileDataReceived; // data, width, height, tilesetId, tileSize
    public event Action<byte[], int, int>? ObstacleDataReceived; // data, width, height
    public event Action<byte[], int, int, string, int>? ObjectLayerDataReceived; // data, width, height, tilesetId, tileSize
    public event Action<SectorData>? SectorDataReceived; // сектор открытого мира (main)
    public event Action? SectorsReloaded; // сервер перезагрузил секторы (/reload, /reloadmap)
    public event Action<string?[]>? HotbarUpdated;
    public event Action<string>? TargetCleared;
    public event Action<string, int, int>? AttackCooldownUpdated;
    public event Action<string, double, double, double, double, string, int>? ProjectileSpawned;
    public event Action<string, double, double>? ProjectileHit;

    // Атака
    public event Action<string, string?, int?, int?>? PlayerAttackPerformed; // (hand, skillId, targetX, targetY)
    public event Action<string, string, string?, int?, int?, int?>? RemotePlayerAttack; // (playerName, hand, skillId, targetX, targetY, buffDurationMs)
    public event Action<string, Facing>? RemotePlayerFacing; // (playerName, facing)

    // Окна
    public event Action<StatusData>? StatusDetailsUpdated;
    public event Action<string, string, int, List<LootItemInfo>, int>? LootReceived;
    public event Action? BoardOpened;

    // Заточка/усиление у Кузнеца
    public event Action? EnhancementOpened;

    // Склад
    public event Action<StorageData>? StorageOpened;
    public event Action<StorageData>? StorageUpdated;

    // Осмотр другого игрока
    public event Action<StatusData>? InspectReceived;

    // Смерть
    public bool IsDead { get; set; }
    public int DeathLostGold { get; set; }
    public event Action<int>? PlayerDeathReceived;

    // Почта
    public event Action<string, List<MailEntry>>? MailListReceived;
    public event Action<MailEntry>? MailDetailReceived;
    public event Action<bool, string>? MailResultReceived;
    public event Action<int>? MailUnreadReceived;

    public event Action<CharacterSlot[]>? CharacterListUpdated;

    public void Initialize(Action uiCallback)
    {
        _uiAction = uiCallback;
    }

    internal void Ui(Action action)
    {
        // В MonoGame все события уже на UI-потоке, просто вызываем
        try { action(); }
        catch (Exception ex) { Logger.Error("UI action failed", ex); }
    }

    // Internal event raisers for ClientMessageHandlerRegistry
    internal void RaiseSystemMessage(string msg) => Ui(() => SystemMessage?.Invoke(msg));
    internal void RaiseWelcomeReceived() => Ui(() => WelcomeReceived?.Invoke());
    internal void RaiseChangelogReceived(ChangelogData data) => Ui(() =>
    {
        LastChangelog = data;
        ChangelogReceived?.Invoke(data);
    });
    internal void RaiseMapUpdated(WorldMap map) => Ui(() => MapUpdated?.Invoke(map));
    internal void RaiseEntityStateReceived(EntityStateMessage msg) => Ui(() => EntityStateReceived?.Invoke(msg));
    internal void RaiseChatReceived(string channel, string name, string text, bool isAdmin) => Ui(() => ChatReceived?.Invoke(channel, name, text, isAdmin));
    internal void RaiseErrorReceived(string text) => Ui(() => ErrorReceived?.Invoke(text));
    internal void RaiseStatusUpdated(StatusData st) => Ui(() => StatusUpdated?.Invoke(st));
    internal void RaiseStatusDetailsUpdated(StatusData st) => Ui(() => StatusDetailsUpdated?.Invoke(st));
    internal void RaiseInventoryUpdated(InventoryData inv) => Ui(() => InventoryUpdated?.Invoke(inv));
    internal void RaiseQuestLogUpdated(List<QuestInfo> available, List<QuestInfo> active, List<QuestInfo> history) => Ui(() => QuestLogUpdated?.Invoke(available, active, history));
    internal void RaiseZoneChanged(string zoneId, string zoneName, bool pvp) => Ui(() => ZoneChanged?.Invoke(zoneId, zoneName, pvp));
    internal void RaiseTileDataReceived(byte[] data, int width, int height, string tilesetId, int tileSize) => Ui(() => TileDataReceived?.Invoke(data, width, height, tilesetId, tileSize));
    internal void RaiseObstacleDataReceived(byte[] data, int width, int height) => Ui(() => ObstacleDataReceived?.Invoke(data, width, height));
    internal void RaiseObjectLayerDataReceived(byte[] data, int width, int height, string tilesetId, int tileSize) => Ui(() => ObjectLayerDataReceived?.Invoke(data, width, height, tilesetId, tileSize));
    internal void RaiseSectorDataReceived(SectorData sector) => Ui(() => SectorDataReceived?.Invoke(sector));
    internal void RaiseSectorsReloaded() => Ui(() => SectorsReloaded?.Invoke());
    internal void RaiseShopUpdated(ShopData shop) => Ui(() => ShopUpdated?.Invoke(shop));
    internal void RaiseTradeOpened(TradeOpenData open) => Ui(() => TradeOpened?.Invoke(open));
    internal void RaiseTradeOfferUpdated(TradeOfferData offer) => Ui(() => TradeOfferUpdated?.Invoke(offer));
    internal void RaiseTradeConfirmUpdated(TradeConfirmData conf) => Ui(() => TradeConfirmUpdated?.Invoke(conf));
    internal void RaiseTradeCompleted(TradeCompleteData done) => Ui(() => TradeCompleted?.Invoke(done));
    internal void RaiseTradeClosed(string msg) => Ui(() => TradeClosed?.Invoke(msg));
    internal void RaiseDialogueOpened(string npcId, string speaker, string text, List<(string Text, int Index)> choices) => Ui(() => DialogueOpened?.Invoke(npcId, speaker, text, choices));
    internal void RaiseDialogueClosed() => Ui(() => DialogueClosed?.Invoke());
    internal void RaiseFloatingText(int x, int y, string text, uint color, bool crit) => Ui(() => FloatingTextReceived?.Invoke(x, y, text, color, crit));
    internal void RaisePlayerDeath(int lostGold) => Ui(() => PlayerDeathReceived?.Invoke(lostGold));
    internal void RaiseCombatStateUpdated(bool inCombat, string? targetName, int targetHp, int targetMaxHp, string? targetId) => Ui(() => CombatStateUpdated?.Invoke(inCombat, targetName, targetHp, targetMaxHp, targetId));
    internal void RaiseTargetDebuffsUpdated(List<DebuffInfo>? debuffs) => Ui(() => TargetDebuffsUpdated?.Invoke(debuffs));
    internal void RaiseTargetCleared(string reason) => Ui(() => TargetCleared?.Invoke(reason));
    internal void RaisePartyUpdated(PartyInfo party) => Ui(() => PartyUpdated?.Invoke(party));
    internal void RaisePartyDisbanded() => Ui(() => PartyDisbanded?.Invoke());
    internal void RaisePartyInviteReceived(string inviterName, string msg) => Ui(() => PartyInviteReceived?.Invoke(inviterName, msg));
    internal void RaiseTradeRequestReceived(string inviterName) => Ui(() => TradeRequestReceived?.Invoke(inviterName));
    internal void RaiseSkillsUpdated(List<ClientSkillInfo> list) => Ui(() => SkillsUpdated?.Invoke(list));
    internal void RaiseInstanceWindowOpened() => Ui(() => InstanceWindowOpened?.Invoke());
    internal void RaiseInstanceListReceived(List<InstanceInfo> list) => Ui(() => InstanceListReceived?.Invoke(list));
    internal void RaiseInstanceInviteReceived(string leaderName, string templateName, string templateId) => Ui(() => InstanceInviteReceived?.Invoke(leaderName, templateName, templateId));
    internal void RaiseInstanceInviteUpdate(string templateName, List<InstanceMemberInfo> members) => Ui(() => InstanceInviteUpdate?.Invoke(templateName, members));
    internal void RaiseInstanceStarted(string templateName, string mode) => Ui(() => InstanceStarted?.Invoke(templateName, mode));
    internal void RaiseHotbarUpdated(string?[] slots) => Ui(() => HotbarUpdated?.Invoke(slots));
    internal void RaiseLootReceived(string corpseId, string monsterName, int dmgPct, List<LootItemInfo> items, int gold) => Ui(() => LootReceived?.Invoke(corpseId, monsterName, dmgPct, items, gold));
    internal void RaiseBoardOpened() => Ui(() => BoardOpened?.Invoke());
    internal void RaiseEnhancementOpened() => Ui(() => EnhancementOpened?.Invoke());
    internal void RaiseStorageOpened(StorageData data) => Ui(() => StorageOpened?.Invoke(data));
    internal void RaiseStorageUpdated(StorageData data) => Ui(() => StorageUpdated?.Invoke(data));

    internal void RaiseInspectReceived(StatusData data) => Ui(() => InspectReceived?.Invoke(data));
    internal void RaiseFriendListUpdated(List<FriendInfo> friends) => Ui(() => FriendListUpdated?.Invoke(friends));
    internal void RaiseFriendResultReceived(bool ok, string msg) => Ui(() => FriendResultReceived?.Invoke(ok, msg));
    internal void RaiseAttackCooldownUpdated(string sid, int rem, int total) => Ui(() => AttackCooldownUpdated?.Invoke(sid, rem, total));
    internal void RaiseProjectileSpawned(string id, double sx, double sy, double tx, double ty, string vt, int fm) => Ui(() => ProjectileSpawned?.Invoke(id, sx, sy, tx, ty, vt, fm));
    internal void RaiseRemotePlayerAttack(string playerName, string hand, string? skillId, int? targetX, int? targetY, int? buffDurationMs) => Ui(() => RemotePlayerAttack?.Invoke(playerName, hand, skillId, targetX, targetY, buffDurationMs));
    internal void RaisePlayerAttackPerformed(string hand, string? skillId, int? targetX = null, int? targetY = null) => Ui(() => PlayerAttackPerformed?.Invoke(hand, skillId, targetX, targetY));
    internal void RaiseRemotePlayerFacing(string playerName, Facing facing) => Ui(() => RemotePlayerFacing?.Invoke(playerName, facing));
    internal void RaiseProjectileHit(string id, double x, double y) => Ui(() => ProjectileHit?.Invoke(id, x, y));
    internal void RaiseCharacterListUpdated(CharacterSlot[] chars) => Ui(() => CharacterListUpdated?.Invoke(chars));

    internal void RaiseMailListReceived(string folder, List<MailEntry> messages) => Ui(() => MailListReceived?.Invoke(folder, messages));
    internal void RaiseMailDetailReceived(MailEntry msg) => Ui(() => MailDetailReceived?.Invoke(msg));
    internal void RaiseMailUnreadReceived(int count) => Ui(() => MailUnreadReceived?.Invoke(count));
    internal void RaiseMailResultReceived(bool ok, string err) => Ui(() => MailResultReceived?.Invoke(ok, err));
    internal void RaiseUnknownMessage(GameMessage message) => Ui(() => UnknownMessage?.Invoke(message));

    public Task SendAsync(string type, object? data)
        => SendAsync(GameMessageTypeJsonConverter.FromWire(type), data);

    public Task SendAsync(GameMessageType type, object? data)
    {
        // Используем NetworkManager для отправки
        var msg = new GameMessage { Type = type, Data = data };
        return GameMain.Instance?.Network.SendAsync(msg) ?? Task.CompletedTask;
    }

    public void SelectCharacter(string name)
        => _ = SendAsync("character_select", new { Name = name });

    public void CreateCharacter(string name, int classVal)
        => _ = SendAsync("character_create", new { Name = name, Class = classVal });

    public void DeleteCharacter(string name)
        => _ = SendAsync("character_delete", new { Name = name });

    public void Authenticate(string login, string password)
    {
        _ = SendAsync("login_auth", new { Login = login, Password = password });
    }

    public void RequestFriendList()
        => _ = SendAsync("friend", new { Action = "list" });

    public void AddFriend(string targetName)
        => _ = SendAsync("friend", new { Action = "add", TargetName = targetName });

    public void RemoveFriend(string targetName)
        => _ = SendAsync("friend", new { Action = "remove", TargetName = targetName });

    public void OnConnected()
    {
        IsConnected = true;
        Ui(() => Connected?.Invoke());
    }

    public void OnDisconnected(string reason)
    {
        IsConnected = false;
        Ui(() => Disconnected?.Invoke(reason));
    }

    public void OnReconnectState(PlayerState player)
    {
        PlayerName = player.Name;
        PlayerLevel = player.Level;
        Rendering.ItemTooltip.PlayerLevel = player.Level;
        Status = new StatusData
        {
            Name = player.Name, Level = player.Level,
            Health = player.Health, MaxHealth = player.MaxHealth,
            Mana = player.Mana, MaxMana = player.MaxMana,
            Gold = player.Gold, Experience = (int)player.Experience,
            AttributePoints = player.AttributePoints,
            Strength = player.Strength, Endurance = player.Endurance,
            Agility = player.Agility, Cunning = player.Cunning,
            Intellect = player.Intellect, Wisdom = player.Wisdom,
            PhysAttack = player.Attack, Defense = player.Defense,
            X = player.X, Y = player.Y,
            ActiveDebuffs = player.ActiveDebuffs.Select(d => new DebuffInfo
            {
                Type = d.Type, DisplayName = d.DisplayName,
                Value = d.Value, RemainingMs = d.RemainingMs, DurationMs = d.DurationMs
            }).ToList()
        };
        Ui(() => StatusUpdated?.Invoke(Status));
    }

    public void HandleMessage(GameMessage message)
    {
        Logger.Debug($"<< {message.Type}");
        try
        {
            if (!ClientMessageHandlerRegistry.TryHandle(this, message))
                Ui(() => UnknownMessage?.Invoke(message));
        }
        catch (Exception ex)
        {
            Logger.Error($"HandleMessage failed for type '{message.Type}'", ex);
        }
    }

    /// <summary>
    /// Снимает ВСЕ подписки на события этого клиента, у которых обработчик принадлежит
    /// заданному объекту (например, GameScreen). Нужно вызывать при Dispose экрана,
    /// иначе синглтон GameClient удерживает экран в памяти (утечка при каждом входе в мир).
    /// </summary>
    public void UnsubscribeAll(object target)
    {
        foreach (var evt in typeof(GameClient).GetEvents(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            var field = typeof(GameClient).GetField(evt.Name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field?.GetValue(this) is not MulticastDelegate md) continue;
            foreach (var handler in md.GetInvocationList())
            {
                if (handler.Target == target)
                    evt.RemoveEventHandler(this, handler);
            }
        }
    }
}
