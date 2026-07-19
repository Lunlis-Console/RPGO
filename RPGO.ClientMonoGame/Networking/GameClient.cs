using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.ClientMonoGame.Networking;

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

    // Состояние игрока
    public string PlayerName { get; private set; } = "Игрок";
    public int PlayerLevel { get; private set; } = 1;
    public WorldMap? CurrentMap { get; private set; }
    public StatusData? Status { get; private set; }
    public InventoryData? Inventory { get; private set; }
    public List<QuestInfo> AvailableQuests { get; private set; } = new();
    public List<QuestInfo> ActiveQuests { get; private set; } = new();

    public bool IsConnected { get; set; }
    public string? SessionToken { get; private set; }
    public Guid PlayerId { get; private set; }

    // События (UI подписывается)
    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<string>? SystemMessage;
    public event Action<string, string, string>? ChatReceived;
    public event Action? WelcomeReceived;
    public event Action<WorldMap>? MapUpdated;
    public event Action<StatusData>? StatusUpdated;
    public event Action<InventoryData>? InventoryUpdated;
    public event Action<List<QuestInfo>, List<QuestInfo>>? QuestLogUpdated;
    public event Action<string>? ErrorReceived;
    public event Action<GameMessage>? UnknownMessage;
    public event Action<int, int, string, uint>? FloatingTextReceived;
    public event Action<ShopData>? ShopUpdated;
    public event Action<TradeOpenData>? TradeOpened;
    public event Action<TradeOfferData>? TradeOfferUpdated;
    public event Action<TradeConfirmData>? TradeConfirmUpdated;
    public event Action<TradeCompleteData>? TradeCompleted;
    public event Action<string>? TradeClosed;

    // HUD
    public event Action<bool, string?, int, int>? CombatStateUpdated;
    public event Action<PartyData>? PartyUpdated;
    public event Action? PartyDisbanded;
    public event Action<string, string>? PartyInviteReceived;
    public event Action<string>? TradeRequestReceived;
    public event Action<List<ClientSkillInfo>>? SkillsUpdated;
    public event Action<string?[]>? HotbarUpdated;
    public event Action<string>? TargetCleared;
    public event Action<string, int, int>? AttackCooldownUpdated;

    // Окна
    public event Action<StatusData>? StatusDetailsUpdated;
    public event Action<string, string, int, List<LootItemInfo>, int>? LootReceived;
    public event Action? BoardOpened;

    public void Initialize(Action uiCallback)
    {
        _uiAction = uiCallback;
    }

    private void Ui(Action action)
    {
        // В MonoGame все события уже на UI-потоке, просто вызываем
        try { action(); }
        catch (Exception ex) { Logger.Error("UI action failed", ex); }
    }

    public Task SendAsync(string type, object? data)
    {
        // Используем NetworkManager для отправки
        var msg = new GameMessage { Type = type, Data = data };
        return GameMain.Instance?.Network.SendAsync(msg) ?? Task.CompletedTask;
    }

    public void Authenticate(string login, string password)
    {
        _ = SendAsync("login_auth", new { Login = login, Password = password });
    }

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
        Status = new StatusData
        {
            Name = player.Name, Level = player.Level,
            Health = player.Health, MaxHealth = player.MaxHealth,
            Mana = player.Mana, MaxMana = player.MaxMana,
            Gold = player.Gold, Experience = (int)player.Experience,
            AttributePoints = player.AttributePoints,
            Strength = player.Strength, Stamina = player.Stamina,
            Agility = player.Agility, Cunning = player.Cunning,
            Wisdom = player.Wisdom, Will = player.Will,
            TotalAttack = player.Attack, TotalDefense = player.Defense,
            X = player.X, Y = player.Y
        };
        Ui(() => StatusUpdated?.Invoke(Status));
    }

    public void HandleMessage(GameMessage message)
    {
        Logger.Debug($"<< {message.Type}");
        try
        {
            switch (message.Type)
            {
                case "auth_response":
                    if (message.Data is JsonElement authEl)
                    {
                        bool success = authEl.TryGetProperty("Success", out var s) && s.GetBoolean();
                        string msg = authEl.TryGetProperty("Message", out var m) ? (m.GetString() ?? "") : "";
                        Ui(() => SystemMessage?.Invoke(success ? msg : $"Ошибка: {msg}"));
                        if (success)
                        {
                            string? token = authEl.TryGetProperty("session_token", out var stEl) ? stEl.GetString() : null;
                            Guid pid = authEl.TryGetProperty("player_id", out var pidEl) && pidEl.ValueKind == JsonValueKind.String ? Guid.Parse(pidEl.GetString() ?? Guid.Empty.ToString()) : Guid.Empty;
                            SessionToken = token;
                            PlayerId = pid;
                            GameMain.Instance?.Network.SetSession(token ?? "", pid);
                            _ = SendAsync("skills_request", null);
                        }
                    }
                    break;

                case "welcome":
                    var wel = message.Deserialize<WelcomeData>();
                    PlayerName = wel?.PlayerName ?? "Игрок";
                    Ui(() => WelcomeReceived?.Invoke());
                    break;

                case "map_update":
                    var map = message.Deserialize<WorldMap>();
                    if (map != null)
                    {
                        CurrentMap = map;
                        Ui(() => MapUpdated?.Invoke(map));
                    }
                    break;

                case "chat":
                    var chat = message.Deserialize<ChatData>();
                    if (chat != null)
                    {
                        string channel = chat.Channel ?? "System";
                        Ui(() => ChatReceived?.Invoke(channel, chat.Name ?? "Система", chat.Text ?? ""));
                    }
                    break;

                case "error":
                    if (message.Data is JsonElement errEl)
                    {
                        string text = errEl.TryGetProperty("Message", out var m2) ? (m2.GetString() ?? "Неизвестная ошибка") : "Неизвестная ошибка";
                        Ui(() => ErrorReceived?.Invoke(text));
                        Ui(() => SystemMessage?.Invoke($"[Ошибка] {text}"));
                    }
                    break;

                case "status_response":
                    var st = message.Deserialize<StatusData>();
                    if (st != null)
                    {
                        Status = st;
                        PlayerLevel = st.Level;
                        Ui(() => StatusUpdated?.Invoke(st));
                        Ui(() => StatusDetailsUpdated?.Invoke(st));
                    }
                    break;

                case "inventory_response":
                    var inv = message.Deserialize<InventoryData>();
                    if (inv != null)
                    {
                        Inventory = inv;
                        Ui(() => InventoryUpdated?.Invoke(inv));
                    }
                    break;

                case "quest_log":
                    var log = message.Deserialize<QuestLogData>();
                    AvailableQuests = log?.Available ?? new List<QuestInfo>();
                    ActiveQuests = log?.Active ?? new List<QuestInfo>();
                    Ui(() => QuestLogUpdated?.Invoke(AvailableQuests, ActiveQuests));
                    break;

                case "shop_response":
                    var shop = message.Deserialize<ShopData>();
                    if (shop != null)
                        Ui(() => ShopUpdated?.Invoke(shop));
                    break;

                case "trade_open":
                    var open = message.Deserialize<TradeOpenData>();
                    if (open != null)
                        Ui(() => TradeOpened?.Invoke(open));
                    break;

                case "trade_offer_update":
                    var offer = message.Deserialize<TradeOfferData>();
                    if (offer != null)
                        Ui(() => TradeOfferUpdated?.Invoke(offer));
                    break;

                case "trade_confirm_update":
                    var conf = message.Deserialize<TradeConfirmData>();
                    if (conf != null)
                        Ui(() => TradeConfirmUpdated?.Invoke(conf));
                    break;

                case "trade_complete":
                    var done = message.Deserialize<TradeCompleteData>();
                    if (done != null)
                        Ui(() => TradeCompleted?.Invoke(done));
                    break;

                case "trade_close":
                    {
                        string msg = "Обмен отменён.";
                        if (message.Data is JsonElement el && el.TryGetProperty("Message", out var mEl))
                            msg = mEl.GetString() ?? msg;
                        Ui(() => TradeClosed?.Invoke(msg));
                    }
                    break;

                case "trade_declined":
                    {
                        string msg = "Игрок отказался от обмена.";
                        if (message.Data is JsonElement el && el.TryGetProperty("Message", out var mEl))
                            msg = mEl.GetString() ?? msg;
                        Ui(() => TradeClosed?.Invoke(msg));
                    }
                    break;

                case "damage":
                    if (message.Data is JsonElement dmgEl)
                    {
                        int amount = dmgEl.TryGetProperty("Amount", out var am) ? am.GetInt32() : 0;
                        bool isCrit = dmgEl.TryGetProperty("IsCrit", out var ic) && ic.GetBoolean();
                        int x = dmgEl.TryGetProperty("X", out var xp) ? xp.GetInt32() : 0;
                        int y = dmgEl.TryGetProperty("Y", out var yp) ? yp.GetInt32() : 0;
                        string target = dmgEl.TryGetProperty("Target", out var tg) ? (tg.GetString() ?? "") : "";
                        uint color = target == "monster" ? 0xFF32CD32u : 0xFFDC143Cu;
                        string text = (amount > 0 ? "-" : "") + amount + (isCrit ? "!" : "");
                        Ui(() => FloatingTextReceived?.Invoke(x, y, text, color));
                    }
                    break;

                case "heal":
                    if (message.Data is JsonElement healEl)
                    {
                        int amount = healEl.TryGetProperty("Amount", out var ham) ? ham.GetInt32() : 0;
                        int x = healEl.TryGetProperty("X", out var hxp) ? hxp.GetInt32() : 0;
                        int y = healEl.TryGetProperty("Y", out var hyp) ? hyp.GetInt32() : 0;
                        Ui(() => FloatingTextReceived?.Invoke(x, y, "+" + amount, 0xFF32CD32u));
                    }
                    break;

                case "combat_state":
                    if (message.Data is JsonElement cs)
                    {
                        bool inCombat = cs.TryGetProperty("InCombat", out var ic2) && ic2.GetBoolean();
                        string? tName = cs.TryGetProperty("TargetName", out var tn) ? tn.GetString() : null;
                        int tHp = cs.TryGetProperty("TargetHp", out var th) ? th.GetInt32() : 0;
                        int tMaxHp = cs.TryGetProperty("TargetMaxHp", out var tmh) ? tmh.GetInt32() : 0;
                        Ui(() => CombatStateUpdated?.Invoke(inCombat, tName, tHp, tMaxHp));
                    }
                    break;

                case "cancel_target":
                case "target_cleared":
                    Ui(() => TargetCleared?.Invoke("cleared"));
                    break;

                case "party_invite_sent":
                    if (message.Data is JsonElement pis && pis.TryGetProperty("TargetName", out var ptn))
                        Ui(() => ChatReceived?.Invoke("Party", "Пати", $"Приглашение отправлено {ptn.GetString()}"));
                    break;

                case "party_invite_declined":
                    if (message.Data is JsonElement pdec && pdec.TryGetProperty("TargetName", out var pdn))
                        Ui(() => ChatReceived?.Invoke("Party", "Пати", $"{pdn.GetString()} отказал(а) от приглашения"));
                    break;

                case "trade_request_sent":
                    if (message.Data is JsonElement trs && trs.TryGetProperty("TargetName", out var trn))
                        Ui(() => ChatReceived?.Invoke("System", "Трейд", $"Запрос обмена отправлен {trn.GetString()}"));
                    break;

                case "party_update":
                    var party = message.Deserialize<PartyData>();
                    if (party != null)
                        Ui(() => PartyUpdated?.Invoke(party));
                    break;

                case "party_disbanded":
                    Ui(() => PartyDisbanded?.Invoke());
                    break;

                case "party_invite_received":
                    if (message.Data is JsonElement pir)
                    {
                        string? inviterName = pir.TryGetProperty("InviterName", out var invEl) ? invEl.GetString() : null;
                        if (inviterName != null)
                            Ui(() => PartyInviteReceived?.Invoke(inviterName, ""));
                    }
                    break;

                case "trade_request_received":
                    if (message.Data is JsonElement trEl)
                    {
                        string? inviterName = trEl.TryGetProperty("InviterName", out var invN) ? invN.GetString() : null;
                        if (inviterName != null)
                            Ui(() => TradeRequestReceived?.Invoke(inviterName));
                    }
                    break;

                case "skills_response":
                    if (message.Data is JsonElement sk)
                    {
                        var list = new List<ClientSkillInfo>();
                        if (sk.TryGetProperty("Skills", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var e in arr.EnumerateArray())
                            {
                                list.Add(new ClientSkillInfo
                                {
                                    Id = e.TryGetProperty("Id", out var id) ? (id.GetString() ?? "") : "",
                                    Name = e.TryGetProperty("Name", out var nm) ? (nm.GetString() ?? "") : "",
                                    Description = e.TryGetProperty("Description", out var ds) ? (ds.GetString() ?? "") : "",
                                    Type = e.TryGetProperty("Type", out var ty) ? (ty.GetString() ?? "") : "",
                                    MpCost = e.TryGetProperty("MpCost", out var mp) ? mp.GetInt32() : 0,
                                    CooldownMs = e.TryGetProperty("CooldownMs", out var cd) ? cd.GetInt32() : 0,
                                    DamageMultiplier = e.TryGetProperty("DamageMultiplier", out var dm) ? dm.GetDouble() : 1,
                                    MinLevel = e.TryGetProperty("MinLevel", out var ml) ? ml.GetInt32() : 1
                                });
                            }
                        }
                        Ui(() => SkillsUpdated?.Invoke(list));
                    }
                    break;

                case "hotbar_update":
                case "hotbar_response":
                    if (message.Data is JsonElement hb)
                    {
                        var slots = new string?[10];
                        if (hb.TryGetProperty("Slots", out var sarr) && sarr.ValueKind == JsonValueKind.Array)
                        {
                            int i = 0;
                            foreach (var e in sarr.EnumerateArray())
                            {
                                if (i >= 10) break;
                                slots[i++] = e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                            }
                        }
                        Ui(() => HotbarUpdated?.Invoke(slots));
                    }
                    break;

                case "loot_corpse":
                    if (message.Data is JsonElement lootEl)
                    {
                        string corpseId = lootEl.TryGetProperty("CorpseId", out var cid) ? (cid.GetString() ?? "") : "";
                        string monsterName = lootEl.TryGetProperty("MonsterName", out var mn) ? (mn.GetString() ?? "") : "";
                        int gold = lootEl.TryGetProperty("Gold", out var g) ? g.GetInt32() : 0;
                        int dmgPct = lootEl.TryGetProperty("DamagePercent", out var dp) ? dp.GetInt32() : 0;
                        var items = new List<LootItemInfo>();
                        if (lootEl.TryGetProperty("Items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var iel in itemsEl.EnumerateArray())
                            {
                                items.Add(new LootItemInfo
                                {
                                    Id = iel.TryGetProperty("Id", out var idP) ? (idP.GetString() ?? "") : "",
                                    Name = iel.TryGetProperty("Name", out var nmP) ? (nmP.GetString() ?? "") : "",
                                    Type = iel.TryGetProperty("Type", out var tpP) ? (tpP.GetString() ?? "") : "",
                                    Value = iel.TryGetProperty("Value", out var vP) ? vP.GetInt32() : 0,
                                    Description = iel.TryGetProperty("Description", out var dP) ? (dP.GetString() ?? "") : ""
                                });
                            }
                        }
                        Ui(() => LootReceived?.Invoke(corpseId, monsterName, dmgPct, items, gold));
                    }
                    break;

                case "board_open":
                case "open_board":
                    Ui(() => BoardOpened?.Invoke());
                    break;

                case "attack_cooldown":
                case "skill_cooldown":
                    if (message.Data is JsonElement ac)
                    {
                        string? sid = ac.TryGetProperty("SkillId", out var sidEl) ? sidEl.GetString() : null;
                        int rem = ac.TryGetProperty("RemainingMs", out var remEl) ? remEl.GetInt32() : 0;
                        int total = ac.TryGetProperty("TotalMs", out var totEl) ? totEl.GetInt32() : 0;
                        if (sid != null)
                            Ui(() => AttackCooldownUpdated?.Invoke(sid, rem, total));
                    }
                    break;

                default:
                    Ui(() => UnknownMessage?.Invoke(message));
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"HandleMessage failed for type '{message.Type}'", ex);
        }
    }
}
