using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Input;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.ClientMonoGame.Windows;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.ClientMonoGame.Screens;

public class GameScreen : IScreen
{

    private readonly MapRenderer _mapRenderer;
    private readonly HudRenderer _hudRenderer;
    private readonly ChatRenderer _chatRenderer;
    private readonly InputManager _inputManager;

    private readonly InventoryWindow _inventoryWindow = new();
    private readonly StatusWindow _statusWindow = new();
    private readonly SkillsWindow _skillsWindow = new();
    private readonly EquipmentWindow _equipmentWindow = new();
    private readonly QuestLogWindow _questLogWindow = new();
    private List<QuestInfo> _activeQuests = new();
    private readonly ShopWindow _shopWindow = new();
    private readonly LootWindow _lootWindow = new();
    private readonly QuestBoardWindow _questBoardWindow = new();
    private readonly TradeWindow _tradeWindow = new();
    private readonly QuantityDialog _quantityDialog = new();
    private readonly EntityPickDialog _entityPickDialog = new();
    private readonly SettingsWindow _settingsWindow = new();
    private readonly LogoutConfirmWindow _logoutConfirmWindow = new();
    private readonly PartyInviteWindow _partyInviteWindow = new();
    private readonly TradeRequestWindow _tradeRequestWindow = new();
    private readonly SocialWindow _socialWindow;
    private readonly HashSet<string> _lootedCorpses = new();
    // Авто-подход к игроку для обмена
    private string? _pendingTradeTarget;
    private const int TradeRequestCooldownMs = 500;
    private int _lastTradeRequestTime;
    private int _playerGoldCache;
    private Item? _dragOverlayItem;
    private ClientSkillInfo? _dragOverlaySkill;
    // Кулдауны хотбара: idx -> (время окончания, полная длительность мс)
    private readonly Dictionary<int, (DateTime End, int Total)> _hotbarCooldowns = new();
    // Заготовленный навык: ожидает клика по монстру для применения
    private string? _pendingSkillId;
    private int _pendingSlot = -1;
    private bool _pendingSent;

    private readonly WindowManager _windows = new();

    // Иконки в правом нижнем углу
    private Rectangle[] _iconRects = Array.Empty<Rectangle>();
    private const int IconSize = 40;
    private const int IconGap = 6;

    private KeyboardState _prevKeyboard;
    private MouseState _prevMouse;
    private Rectangle _invitePartyRect = Rectangle.Empty;
    private Rectangle _tradePlayerRect = Rectangle.Empty;
    private Rectangle _partyLeaveRect = Rectangle.Empty;
    private Rectangle _partyDisbandRect = Rectangle.Empty;
    private int _lastPartyMemberCount;
    private HashSet<Guid> _lastPartyMemberIds = new();

    // Отслеживание опыта/уровня для всплывающих подсказок
    private int _lastXp = -1;
    private int _lastLevel = -1;

    public GameScreen()
    {
        var client = GameMain.Instance!.Client;

        _mapRenderer = new MapRenderer();
        _hudRenderer = new HudRenderer();
        _chatRenderer = new ChatRenderer();
        _inputManager = new InputManager();
        _socialWindow = new SocialWindow(client);
        _socialWindow.WhisperRequested += name =>
        {
            _chatRenderer.IsTyping = true;
            _chatRenderer.TypedText = $"/w {name} ";
        };

        // Привязка событий GameClient → рендеринг
        client.MapUpdated += map =>
        {
            _mapRenderer.SetMap(map);
            _mapRenderer.SetPlayerName(client.PlayerName);
            _mapRenderer.SetPlayerLevel(client.PlayerLevel);
        };
        client.FloatingTextReceived += (x, y, text, argb, isCrit) =>
        {
            // argb — uint в формате 0xAARRGGBB. Важно явно работать как uint,
            // иначе неявное преобразование в int даёт арифметический сдвиг и белый цвет.
            uint a = argb;
            var color = new Color(
                (byte)((a >> 16) & 0xFFu),
                (byte)((a >> 8) & 0xFFu),
                (byte)(a & 0xFFu));
            Logger.Debug($"FLT screen argb={argb:X8} -> rgb=({color.R},{color.G},{color.B}) text={text}");
            _mapRenderer.SpawnFloatingText(x, y, text, color, isCrit);
        };
        client.CombatStateUpdated += (inCombat, targetName, hp, maxHp) =>
        {
            _hudRenderer.UpdateCombatState(inCombat, targetName, hp, maxHp);
            // Бой завершён — снимаем выбор цели на карте, иначе игрок
            // продолжает «смотреть» на уже неактуальную цель.
            if (!inCombat) _mapRenderer.ClearSelection();
        };
        client.AttackCooldownUpdated += (skillId, remainingMs, totalMs) =>
        {
            // Ставим/обновляем кулдаун хотбара только по реальному подтверждению от сервера,
            // когда навык действительно применился (игрок дошёл до цели).
            int slot = -1;
            var slots = _inputManager.HotbarSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == "skill:" + (_inputManager.GetSkillById(skillId)?.Name ?? ""))
                    slot = i;
            if (slot >= 0 && totalMs > 0)
                _hotbarCooldowns[slot] = (DateTime.UtcNow.AddMilliseconds(remainingMs), totalMs);

            // Навык реально применился — снимаем заготовку (жёлтую рамку)
            if (_pendingSkillId == skillId)
            {
                _pendingSkillId = null;
                _pendingSlot = -1;
                _pendingSent = false;
            }
        };
        client.TargetCleared += _ =>
        {
            _hudRenderer.ClearTarget();
            _mapRenderer.ClearSelection();
        };
        client.PartyUpdated += party =>
        {
            _hudRenderer.UpdateParty(party);

            // Подсветка ников группы на карте (без себя)
            var myName = GameMain.Instance!.Client.PlayerName;
            var groupNames = party.Members
                .Where(m => !string.Equals(m.Name, myName, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Name)
                .ToList();
            _mapRenderer.SetPartyMembers(groupNames);

            var myId = GameMain.Instance!.Client.PlayerId;
            if (_lastPartyMemberCount == 0 && party.Members.Count >= 2)
            {
                _chatRenderer.AddMessage(ChatChannel.Party, "Группа", "Группа сформирована!");
            }
            else
            {
                foreach (var m in party.Members)
                    if (m.PlayerId != myId && !_lastPartyMemberIds.Contains(m.PlayerId))
                        _chatRenderer.AddMessage(ChatChannel.Party, "Группа", $"{m.Name} присоединился к группе");
            }
            _lastPartyMemberCount = party.Members.Count;
            _lastPartyMemberIds = party.Members.Select(m => m.PlayerId).ToHashSet();
        };
        client.PartyDisbanded += () =>
        {
            if (_lastPartyMemberCount > 0)
                _chatRenderer.AddMessage(ChatChannel.Party, "Группа", "Группа распущена.");
            _lastPartyMemberCount = 0;
            _lastPartyMemberIds.Clear();
            _hudRenderer.ClearParty();
            _mapRenderer.SetPartyMembers(Array.Empty<string>());
        };
        client.PartyInviteReceived += (inviterName, _) =>
        {
            _partyInviteWindow.Show(inviterName);
            _windows.BringToFront(_partyInviteWindow);
        };
        client.StatusUpdated += status =>
        {
            _hudRenderer.UpdateStatus(status);
            _statusWindow.UpdateData(status);
            _playerGoldCache = status.Gold;
            _skillsWindow.SetPlayerLevel(status.Level);

            // Всплывающие подсказки: +XP и «Новый уровень!»
            if (_lastXp < 0) { _lastXp = status.Experience; _lastLevel = status.Level; }
            else
            {
                int xpGain = status.Experience - _lastXp;
                if (xpGain > 0)
                    _mapRenderer.SpawnFloatingTextAtPlayer($"+{xpGain} XP", new Color(120, 220, 255));
                if (status.Level > _lastLevel)
                    _mapRenderer.SpawnFloatingTextAtPlayer("Новый уровень!", Color.Gold, true);
                _lastXp = status.Experience;
                _lastLevel = status.Level;
            }
        };
        client.SkillsUpdated += skills =>
        {
            _inputManager.SetSkills(skills);
            _skillsWindow.UpdateData(skills);
        };
        client.InventoryUpdated += inv =>
        {
            _inputManager.SetInventory(inv);
            _inventoryWindow.UpdateData(inv);
            _equipmentWindow.UpdateData(inv.Equipment);
        };
        _equipmentWindow.UnequipItem += slot => _ = client.SendAsync("unequip", new { Slot = slot });
        _equipmentWindow.CloseRequested += () => _equipmentWindow.Visible = false;
        _inventoryWindow.DragStateChanged += item =>
        {
            _equipmentWindow.DraggingType = item?.Type;
            _dragOverlayItem = item;
        };
        // Перетаскивание предмета ИЗ слота экипировки (для снятия drag-n-drop)
        _equipmentWindow.DragStateChanged += item =>
        {
            _equipmentWindow.DraggingType = item?.Type;
            _dragOverlayItem = item;
        };
        _equipmentWindow.IsOverInventory = pt => _inventoryWindow.Contains(pt);
        _lootWindow.DragStateChanged += item =>
        {
            _dragOverlayItem = item;
        };
        _lootWindow.TakeItem += item =>
        {
            _ = client.SendAsync("take_loot", new { CorpseId = _lootWindow.CorpseId, TakeAll = false, ItemIds = new[] { item.Id }, TakeGold = false });
            _lootWindow.RemoveItem(item);
        };
        _lootWindow.DropOnInventory += (pt, item) =>
        {
            if (_inventoryWindow.Contains(pt))
                _ = client.SendAsync("take_loot", new { CorpseId = _lootWindow.CorpseId, TakeAll = false, ItemIds = new[] { item.Id }, TakeGold = false });
        };
        _inventoryWindow.DropOnEquip += (pt, item) =>
        {
            if (!_equipmentWindow.Visible) return false;
            if (_equipmentWindow.TryGetSlotAt(pt, item.Type, out var slot) && slot != null)
            {
                _ = client.SendAsync("equip", new { ItemId = item.Id, TargetSlot = slot });
                return true;
            }
            return false;
        };
        client.HotbarUpdated += slots => _inputManager.UpdateHotbar(slots);
        client.ChatReceived += (channel, name, text) =>
        {
            if (Enum.TryParse<ChatChannel>(channel, out var ch))
                _chatRenderer.AddMessage(ch, name, text);
            else
                _chatRenderer.AddMessage(ChatChannel.System, name, text);
        };
        client.SystemMessage += msg => _chatRenderer.AddMessage(ChatChannel.System, "Система", msg);
        client.WelcomeReceived += () =>
        {
            _ = client.SendAsync("status", null);
            _ = client.SendAsync("inventory_request", null);
        };
        client.ShopUpdated += data =>
        {
            _shopWindow.UpdateData(data);
            _inventoryWindow.Visible = true;
            _inventoryWindow.ShopMode = true;
            PositionTradeWindows();
        };
        _shopWindow.Closed += () =>
        {
            _shopWindow.Visible = false;
            _inventoryWindow.Visible = false;
            _inventoryWindow.ShopMode = false;
        };
        _shopWindow.BuyItem += (id, qty) => _ = client.SendAsync("buy", new { ItemId = id, Quantity = qty });
        _shopWindow.DragStateChanged += item => _dragOverlayItem = item;
        _shopWindow.SellAllTrophies += () => _ = client.SendAsync("sell_all_trophies", new { });

        // Покупка: перетащили товар из магазина в инвентарь
        _shopWindow.DropOnInventory += (pt, item) =>
        {
            if (!_inventoryWindow.Visible || !_inventoryWindow.Contains(pt)) return false;
            int stock = Math.Max(1, item.Stock);
            int maxAffordable = _shopWindow.DiscountedPrice(item) > 0
                ? _playerGoldCache / _shopWindow.DiscountedPrice(item) : stock;
            int max = Math.Min(stock, Math.Max(1, maxAffordable));
            if (stock > 1 || item.MaxStack > 1)
                OpenQuantity(item.Name, max, _shopWindow.DiscountedPrice(item),
                    q => _ = client.SendAsync("buy", new { ItemId = item.Id, Quantity = q }));
            else
                _ = client.SendAsync("buy", new { ItemId = item.Id, Quantity = 1 });
            return true;
        };
        _shopWindow.PendingBuy += (item, max) =>
        {
            int stock = Math.Max(1, item.Stock);
            int maxAffordable = _shopWindow.DiscountedPrice(item) > 0
                ? _playerGoldCache / _shopWindow.DiscountedPrice(item) : stock;
            int realMax = Math.Min(max, Math.Max(1, maxAffordable));
            OpenQuantity(item.Name, realMax, _shopWindow.DiscountedPrice(item),
                q => _ = client.SendAsync("buy", new { ItemId = item.Id, Quantity = q }));
        };

        // Продажа: перетащили предмет из инвентаря в магазин
        _inventoryWindow.DropOnSell += (pt, item) =>
        {
            if (!_shopWindow.Visible || !_shopWindow.Contains(pt)) return false;
            RequestSell(item);
            return true;
        };
        _inventoryWindow.SellItem += (id, qty) => _ = client.SendAsync("sell", new { ItemId = id, Quantity = qty });
        _inventoryWindow.PendingSell += (item, max) => RequestSellDialog(item, max);
        _inventoryWindow.PendingDrop += (item, max) =>
            OpenQuantity(item.Name, max, 1, q => _ = client.SendAsync("drop_item", new { ItemId = item.Id, Quantity = q }), showPrice: false);
        client.TradeOpened += data =>
        {
            var inv = data.YourInventory ?? new List<TradeItemData>();
            var grouped = inv.GroupBy(i => i.Id).Select(gr => $"{gr.First().Name} x{gr.Count()}").ToList();
            Logger.Action($"ОБМЕН: получен trade_open с '{data.OtherName}', предметов в инвентаре={inv.Count} (уникальных={grouped.Count}), золото={data.YourGold}");
            foreach (var line in grouped)
                Logger.Debug($"ОБМЕН: инвентарь аккаунта -> {line}");
            _tradeWindow.Open(data);
            _windows.BringToFront(_tradeWindow);
        };
        client.QuestLogUpdated += (available, active) =>
        {
            _questLogWindow.UpdateActive(active);
            _activeQuests = active ?? new List<QuestInfo>();
            _questBoardWindow.UpdateData(available, active);
        };
        client.LootReceived += (corpseId, monsterName, damagePct, items, gold) =>
        {
            var lootItems = new List<RPGGame.ClientMonoGame.Networking.LootItemInfo>();
            foreach (var item in items)
                lootItems.Add(new RPGGame.ClientMonoGame.Networking.LootItemInfo
                {
                    Id = item.Id,
                    Name = item.Name,
                    Type = item.Type,
                    Value = item.Value,
                    Description = item.Description
                });
            _lootWindow.Setup(corpseId, monsterName, damagePct, lootItems, gold);
            var g = GameMain.Instance!.Graphics;
            _lootWindow.X = Math.Max(0, (g.PreferredBackBufferWidth - _lootWindow.Width) / 2);
            _lootWindow.Y = Math.Max(0, (g.PreferredBackBufferHeight - _lootWindow.Height) / 2);
        };

        // События карты
        _mapRenderer.MoveRequested += (x, y) =>
        {
            Logger.Action($"Движение в клетку ({x}, {y})");
            _ = client.SendAsync("move_to", new { X = x, Y = y });
        };
        _mapRenderer.InteractRequested += (entity, x, y) =>
        {
            Logger.Action($"Взаимодействие с {entity.Type} '{entity.Name}' ({x}, {y})");

            // Заготовленный навык применится автоматически, когда начнётся бой
            // (по текущей цели). Здесь просто начинаем бой обычным образом.
            if (entity.Type == "corpse" && entity.Id != null)
                _ = client.SendAsync("loot_corpse", new { CorpseId = entity.Id });
            else
                _ = client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, MonsterId = entity.Id?.ToString() });
        };

        // Клик по клетке с несколькими сущностями — открываем окно выбора
        _mapRenderer.EntityPickRequested += (entities, mapX, mapY) =>
        {
            // Убираем уже собранные трупы
            var filtered = entities.Where(e => e.Type != "corpse" || e.Id == null || !_lootedCorpses.Contains(e.Id)).ToList();
            if (filtered.Count == 0) return;
            if (filtered.Count == 1)
            {
                var single = filtered[0];
                Logger.Action($"Выбрана сущность: {single.Type} '{single.Name}' ({mapX}, {mapY})");
                if (single.Type == "corpse" && single.Id != null)
                {
                    _lootedCorpses.Add(single.Id);
                    _ = client.SendAsync("loot_corpse", new { CorpseId = single.Id });
                }
                else
                    _ = client.SendAsync("interact_target", new { Type = single.Type, X = mapX, Y = mapY, MonsterId = single.Id?.ToString() });
                return;
            }
            var g = GameMain.Instance!.Graphics;
            _entityPickDialog.Setup(filtered, mapX, mapY);
            _entityPickDialog.X = Math.Max(0, (g.PreferredBackBufferWidth - _entityPickDialog.Width) / 2);
            _entityPickDialog.Y = Math.Max(0, (g.PreferredBackBufferHeight - _entityPickDialog.Height) / 2);
            _windows.BringToFront(_entityPickDialog);
        };
        _entityPickDialog.OnPick += (entity, x, y) =>
        {
            Logger.Action($"Выбрана сущность: {entity.Type} '{entity.Name}' ({x}, {y})");
            if (entity.Type == "corpse" && entity.Id != null)
            {
                _lootedCorpses.Add(entity.Id);
                _ = client.SendAsync("loot_corpse", new { CorpseId = entity.Id });
            }
            else
                _ = client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, MonsterId = entity.Id?.ToString() });
        };

        // Хотбар
        _inputManager.HotbarActivated += (idx, item) => ActivateHotbarSlot(idx, item);

        // Привязка окон к GameClient (торговля)
        client.TradeOfferUpdated += offer =>
        {
            if (offer.IsFromMe) _tradeWindow.UpdateMyOffer(offer);
            else _tradeWindow.UpdateTheirOffer(offer);
        };
        client.TradeConfirmUpdated += conf => _tradeWindow.UpdateConfirm(conf);
        client.TradeCompleted += done =>
        {
            _tradeWindow.HandleComplete(done);
            if (!string.IsNullOrEmpty(done.Message))
                _chatRenderer.AddMessage(ChatChannel.System, "Обмен", done.Message);
        };
        client.TradeClosed += msg =>
        {
            _tradeWindow.Visible = false;
            if (!string.IsNullOrEmpty(msg))
                _chatRenderer.AddMessage(ChatChannel.System, "Обмен", msg);
        };

        // Взаимодействие с торговлей
        _tradeWindow.OfferChanged += (entries, gold) =>
        {
            _ = client.SendAsync("trade_offer", new { Entries = entries, Gold = gold });
        };
        _tradeWindow.RequestQuantity += (itemName, max, defaultQty, onConfirm) =>
        {
            OpenQuantity(itemName, max, 0, onConfirm, showPrice: false);
        };
        _tradeWindow.ConfirmRequested += () => _ = client.SendAsync("trade_confirm", null);
        _tradeWindow.CancelRequested += () => _ = client.SendAsync("trade_cancel", null);

        // Взаимодействие с квестами
        _questBoardWindow.TakeQuest += id => _ = client.SendAsync("take_quest", new { QuestId = id });
        _questBoardWindow.CompleteQuest += id => _ = client.SendAsync("complete_quest", new { QuestId = id });
        _questBoardWindow.AbandonQuest += id => _ = client.SendAsync("abandon_quest", new { QuestId = id });
        _questLogWindow.AbandonQuest += id => _ = client.SendAsync("abandon_quest", new { QuestId = id });

        // Настройки: применение режима/разрешения экрана
        _settingsWindow.ApplyRequested += ApplySettings;
        _settingsWindow.LogoutRequested += () =>
        {
            _settingsWindow.Visible = false;
            var g = GameMain.Instance!.Graphics;
            _logoutConfirmWindow.ResetTimer();
            _logoutConfirmWindow.X = (g.PreferredBackBufferWidth - _logoutConfirmWindow.Width) / 2;
            _logoutConfirmWindow.Y = (g.PreferredBackBufferHeight - _logoutConfirmWindow.Height) / 2;
            _logoutConfirmWindow.Visible = true;
            _windows.BringToFront(_logoutConfirmWindow);
        };
        _logoutConfirmWindow.Confirmed += () =>
        {
            _ = client.SendAsync("logout", new { });
            GameMain.Instance!.Network.Disconnect();
            GameMain.Instance.ShowLogin();
        };
        _logoutConfirmWindow.Cancelled += () => { };
        client.BoardOpened += () =>
        {
            _questBoardWindow.Visible = true;
            CenterWindow(_questBoardWindow);
        };

        // Навыки: клик = заготовить навык (применится по клику по монстру)
        _skillsWindow.UseSkill += id =>
        {
            if (_pendingSkillId == id)
            {
                _pendingSkillId = null;
                _pendingSlot = -1;
                return;
            }
            // Найдём слот хотбара с этим навыком, чтобы подсветить рамкой
            int slot = -1;
            var slots = _inputManager.HotbarSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == "skill:" + (_inputManager.GetSkillById(id)?.Name ?? ""))
                    slot = i;
            _pendingSkillId = id;
            _pendingSlot = slot;
        };
        _skillsWindow.SkillDragStateChanged += skill => _dragOverlaySkill = skill;
        _skillsWindow.SkillDragEnded += HandleSkillDragEnd;

        // Лут
        _lootWindow.TakeLoot += (corpseId, takeAll, ids, takeGold) =>
            _ = client.SendAsync("take_loot", new { CorpseId = corpseId, TakeAll = takeAll, ItemIds = ids, TakeGold = takeGold });

        // Инвентарь
        _inventoryWindow.EquipItem += id =>
        {
            _ = client.SendAsync("equip", new { ItemId = id });
            OpenEquipmentBesideInventory();
        };
        _inventoryWindow.UseItem += id => _ = client.SendAsync("use_item", new { ItemId = id });
        _inventoryWindow.DeleteItem += id => _ = client.SendAsync("drop_item", new { ItemId = id });
        _inventoryWindow.SortItems += () =>
        {
            var inv = client.Inventory;
            if (inv?.Items == null) return;
            int Cat(string t) => t switch
            {
                "weapon" => 0, "armor" => 1, "accessory" => 2,
                "consumable" => 3, "collectible" => 4, "material" => 5, _ => 6
            };
            var order = inv.Items
                .OrderBy(i => Cat(i.Type))
                .ThenBy(i => i.Name)
                .Select(i => i.Id)
                .ToList();
            _ = client.SendAsync("inventory_sort", new { Order = order });
        };

        _statusWindow.AllocateAttribute += attr => _ = client.SendAsync("allocate_attribute", new { Attribute = attr });

        // Регистрация окон в менеджере (порядок = z-order: позже = выше)
        _windows.Add(_inventoryWindow);
        _windows.Add(_statusWindow);
        _windows.Add(_skillsWindow);
        _windows.Add(_equipmentWindow);
        _windows.Add(_questLogWindow);
        _windows.Add(_shopWindow);
        _windows.Add(_lootWindow);
        _windows.Add(_questBoardWindow);
        _windows.Add(_tradeWindow);
        _windows.Add(_quantityDialog);
        _windows.Add(_entityPickDialog);
        _windows.Add(_settingsWindow);
        _windows.Add(_logoutConfirmWindow);
        _windows.Add(_partyInviteWindow);
        _windows.Add(_tradeRequestWindow);
        _windows.Add(_socialWindow);

        _partyInviteWindow.Accepted += inviterName => _ = client.SendAsync("party_accept", new { InviterName = inviterName });
        _partyInviteWindow.Declined += inviterName => _ = client.SendAsync("party_decline", new { InviterName = inviterName });

        client.TradeRequestReceived += inviterName =>
        {
            _tradeRequestWindow.Show(inviterName);
            _windows.BringToFront(_tradeRequestWindow);
        };
        _tradeRequestWindow.Accepted += inviterName => _ = client.SendAsync("trade_accept", new { InviterName = inviterName });
        _tradeRequestWindow.Declined += inviterName => _ = client.SendAsync("trade_decline", new { InviterName = inviterName });
    }

    private void ApplySettings()
    {
        var g = GameMain.Instance!.Graphics;
        var (rw, rh) = _settingsWindow.SelectedResolution;
        g.PreferredBackBufferWidth = rw;
        g.PreferredBackBufferHeight = rh;

        switch (_settingsWindow.SelectedMode)
        {
            case "fullscreen":
                g.IsFullScreen = true;
                GameMain.Instance.Window.IsBorderless = false;
                break;
            case "borderless":
                g.IsFullScreen = true;
                GameMain.Instance.Window.IsBorderless = true;
                break;
            default:
                g.IsFullScreen = false;
                GameMain.Instance.Window.IsBorderless = false;
                break;
        }
        g.ApplyChanges();

        var s = SettingsManager.Load();
        s.Width = rw;
        s.Height = rh;
        s.Mode = _settingsWindow.SelectedMode;
        s.Save();
    }

    public void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        var client = GameMain.Instance!.Client;

        // Drop перетаскиваемого предмета из инвентаря/луталки/магазина в хотбар
        // (обрабатываем ДО _windows.Update, т.к. окна сбрасывают _dragOverlayItem при отпускании)
        if (_dragOverlayItem != null
            && mouse.LeftButton == ButtonState.Released
            && _prevMouse.LeftButton == ButtonState.Pressed)
        {
            int idx = HitHotbarSlot(mouse.X, mouse.Y);
            if (idx >= 0)
            {
                var slots = _inputManager.HotbarSlots.ToList();
                while (slots.Count < 10) slots.Add(null);
                string tag = "item:" + _dragOverlayItem.Name;
                if (slots[idx] == tag) slots[idx] = null; // повторный drop — очистка
                else slots[idx] = tag;
                _inputManager.UpdateHotbar(slots.ToArray());
                _ = client.SendAsync("hotbar_update", new { Slots = slots });
            }
        }

        // Обновление всех окон (WindowManager сам разруливает перекрытия и фокус)
        _windows.Update(gameTime, keyboard, mouse);

        bool settingsOpen = _settingsWindow.Visible;
        bool mouseOverAnyWindow = _windows.IsMouseOverVisibleWindow(mouse.X, mouse.Y);

        // Если окно настроек открыто — блокируем весь ввод игры
        if (settingsOpen)
        {
            _prevKeyboard = keyboard;
            _prevMouse = mouse;
            return;
        }

        // Авто-подход к игроку для обмена
        if (_pendingTradeTarget != null)
        {
            var targetEntity = _mapRenderer.GetSelectedEntity();
            if (targetEntity != null && targetEntity.Type == "player" && targetEntity.Name == _pendingTradeTarget)
            {
                int dist = Math.Abs(targetEntity.X - _mapRenderer.GetPlayerX()) + Math.Abs(targetEntity.Y - _mapRenderer.GetPlayerY());
                if (dist <= 1)
                {
                    Logger.Action($"Запрос обмена: {_pendingTradeTarget}");
                    _ = client.SendAsync("trade_request", new { TargetName = _pendingTradeTarget });
                    _pendingTradeTarget = null;
                }
                else
                {
                    int now = Environment.TickCount;
                    if (now - _lastTradeRequestTime >= TradeRequestCooldownMs)
                    {
                        int dx = Math.Sign(targetEntity.X - _mapRenderer.GetPlayerX());
                        int dy = Math.Sign(targetEntity.Y - _mapRenderer.GetPlayerY());
                        int stepX = _mapRenderer.GetPlayerX() + dx;
                        int stepY = _mapRenderer.GetPlayerY() + dy;
                        _ = client.SendAsync("move_to", new { X = stepX, Y = stepY });
                        _lastTradeRequestTime = now;
                    }
                }
            }
            else
            {
                _pendingTradeTarget = null;
            }
        }

        // Клики по хотбару (если не над окном и не в режиме перетаскивания навыка)
        if (!mouseOverAnyWindow && _dragOverlaySkill == null)
        {
            bool leftClick = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
            bool rightClick = mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
            if (leftClick || rightClick)
            {
                int idx = HitHotbarSlot(mouse.X, mouse.Y);
                if (idx >= 0)
                {
                    var slots = _inputManager.HotbarSlots.ToList();
                    while (slots.Count < 10) slots.Add(null);

                    if (rightClick)
                    {
                        // Правый клик — очистить слот
                        slots[idx] = null;
                        _inputManager.UpdateHotbar(slots.ToArray());
                        _ = GameMain.Instance!.Client.SendAsync("hotbar_update", new { Slots = slots });
                    }
                    else if (leftClick && !string.IsNullOrEmpty(slots[idx]))
                    {
                        // Левый клик — активировать слот
                        ActivateHotbarSlot(idx, slots[idx]!);
                    }
                }
            }
        }

        // Чат: клики (индикатор раскладки / выпадающее меню) — обрабатываем всегда
        {
            int hotbarW2 = (int)(GameMain.Instance!.Graphics.PreferredBackBufferWidth * 0.35f);
            int hotbarLeft2 = (GameMain.Instance!.Graphics.PreferredBackBufferWidth - hotbarW2) / 2;
            int chatX2 = 8;
            int chatW2 = hotbarLeft2 - chatX2 - 8;
            int chatH2 = 180;
            int chatY2 = GameMain.Instance!.Graphics.PreferredBackBufferHeight - chatH2 - 8;
            bool chatPressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
            bool chatHandled = _chatRenderer.HandleClick(mouse.X, mouse.Y, chatX2, chatY2, chatW2, chatH2, chatPressed);
            if (chatHandled)
                mouseOverAnyWindow = true; // не даём карте/инвентарю перехватить
        }

        // Esc снимает заготовленный навык (если не в режиме ввода чата)
        if (!_chatRenderer.IsTyping && keyboard.IsKeyDown(Keys.Escape) && _prevKeyboard.IsKeyUp(Keys.Escape))
        {
            _pendingSkillId = null;
            _pendingSlot = -1;
            _pendingSent = false;
        }

        // В бою заготовленный навык применяется автоматически по текущей цели
        // (альтернатива началу боя — клик по навыку при выбранной цели).
        // Отправляем один раз, рамка держится до подтверждения skill_cooldown.
        if (_pendingSkillId != null)
        {
            if (_hudRenderer.InCombat && !_pendingSent)
            {
                _ = GameMain.Instance!.Client.SendAsync("use_skill", new { SkillId = _pendingSkillId });
                _pendingSent = true;
            }
            else if (!_hudRenderer.InCombat)
            {
                // Вне боя применение — по клику по монстру; сбрасываем флаг отправки
                _pendingSent = false;
            }
        }

        // Глобальная смена раскладки чата: Shift+Alt или Win+Space
        bool winDown = keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
        bool altDown = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        bool shiftDown = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        bool altJustPressed = (keyboard.IsKeyDown(Keys.LeftAlt) && _prevKeyboard.IsKeyUp(Keys.LeftAlt))
                              || (keyboard.IsKeyDown(Keys.RightAlt) && _prevKeyboard.IsKeyUp(Keys.RightAlt));
        bool shiftJustPressed = (keyboard.IsKeyDown(Keys.LeftShift) && _prevKeyboard.IsKeyUp(Keys.LeftShift))
                                || (keyboard.IsKeyDown(Keys.RightShift) && _prevKeyboard.IsKeyUp(Keys.RightShift));
        if ((altDown && shiftDown && (altJustPressed || shiftJustPressed))
            || (winDown && keyboard.IsKeyDown(Keys.Space) && _prevKeyboard.IsKeyUp(Keys.Space)))
        {
            _chatRenderer.CurrentLayout = _chatRenderer.CurrentLayout == ChatRenderer.Layout.En
                ? ChatRenderer.Layout.Ru : ChatRenderer.Layout.En;
        }

        // Чат: Enter для ввода/отправки
        if (keyboard.IsKeyDown(Keys.Enter) && _prevKeyboard.IsKeyUp(Keys.Enter))
        {
            if (_chatRenderer.IsTyping)
            {
                if (!string.IsNullOrWhiteSpace(_chatRenderer.TypedText))
                {
                    Logger.Action($"Сообщение в чат: {_chatRenderer.TypedText}");
                    _ = client.SendAsync("say", _chatRenderer.TypedText);
                }
                _chatRenderer.IsTyping = false;
                _chatRenderer.TypedText = "";
            }
            else
            {
                _chatRenderer.IsTyping = true;
            }
        }

        // Чат: текстовый ввод
        if (_chatRenderer.IsTyping)
            _chatRenderer.HandleInput(keyboard, _prevKeyboard);

        // Горячие клавиши (блокируем во время ввода в чат)
        if (!_chatRenderer.IsTyping)
        {
            if (keyboard.IsKeyDown(Keys.I) && _prevKeyboard.IsKeyUp(Keys.I))
            {
                _inventoryWindow.Visible = !_inventoryWindow.Visible;
                if (_inventoryWindow.Visible) PositionInventoryRight();
            }
            if (keyboard.IsKeyDown(Keys.J) && _prevKeyboard.IsKeyUp(Keys.J))
            {
                _questLogWindow.Visible = !_questLogWindow.Visible;
                if (_questLogWindow.Visible) CenterWindow(_questLogWindow);
            }
            if (keyboard.IsKeyDown(Keys.K) && _prevKeyboard.IsKeyUp(Keys.K))
            {
                _skillsWindow.Visible = !_skillsWindow.Visible;
                if (_skillsWindow.Visible) CenterWindow(_skillsWindow);
            }
            if (keyboard.IsKeyDown(Keys.E) && _prevKeyboard.IsKeyUp(Keys.E))
            {
                _equipmentWindow.Visible = !_equipmentWindow.Visible;
                if (_equipmentWindow.Visible) CenterWindow(_equipmentWindow);
            }
            if (keyboard.IsKeyDown(Keys.P) && _prevKeyboard.IsKeyUp(Keys.P))
                _statusWindow.Visible = !_statusWindow.Visible;
        }

        // Хотбар 1-0 (блокируем во время ввода в чат)
        if (!_chatRenderer.IsTyping)
            _inputManager.HandleHotbarKeys(keyboard, _prevKeyboard);

        // WASD / стрелки для движения (блокируем во время ввода в чат)
        if (!_chatRenderer.IsTyping)
            _inputManager.HandleMovement(keyboard, _prevKeyboard, client, _mapRenderer);

        // Направление взгляда игрока вычисляется внутри MapRenderer
        // (AdvanceVisPositions) по фактическому вектору движения — работает
        // и для WASD, и для перемещения кликом/вейпоинтом.

        // Иконки (правый нижний угол)
        bool clickedIcon = false;
        if (_iconRects.Length >= 6 &&
            mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
        {
            for (int i = 0; i < 7; i++)
            {
                if (_iconRects[i].Contains(mouse.X, mouse.Y))
                {
                    clickedIcon = true;
                    switch (i)
                    {
                        case 0: _statusWindow.Visible = !_statusWindow.Visible; break;
                        case 1:
                            _inventoryWindow.Visible = !_inventoryWindow.Visible;
                            if (_inventoryWindow.Visible) PositionInventoryRight();
                            break;
                        case 2:
                            _skillsWindow.Visible = !_skillsWindow.Visible;
                            if (_skillsWindow.Visible) CenterWindow(_skillsWindow);
                            break;
                        case 3:
                            _equipmentWindow.Visible = !_equipmentWindow.Visible;
                            if (_equipmentWindow.Visible) CenterWindow(_equipmentWindow);
                            break;
                        case 4:
                            if (_socialWindow.Visible) _socialWindow.Visible = false;
                            else _socialWindow.Open();
                            break;
                        case 5:
                            _questLogWindow.Visible = !_questLogWindow.Visible;
                            if (_questLogWindow.Visible) CenterWindow(_questLogWindow);
                            break;
                        case 6:
                            _settingsWindow.Visible = !_settingsWindow.Visible;
                            if (_settingsWindow.Visible) CenterWindow(_settingsWindow);
                            break;
                    }
                    break;
                }
            }
        }
        mouseOverAnyWindow |= clickedIcon;

        // Клики по кнопкам пати и приглашения
        bool partyClick = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        if (!mouseOverAnyWindow && partyClick)
        {
            if (_invitePartyRect.Contains(mouse.X, mouse.Y))
            {
                var sel = _mapRenderer.GetSelectedEntity();
                if (sel != null && sel.Type == "player")
                {
                    Logger.Action($"Приглашение в группу: {sel.Name}");
                    _ = GameMain.Instance!.Client.SendAsync("party_invite", new { TargetName = sel.Name });
                }
                mouseOverAnyWindow = true;
            }
            else if (_tradePlayerRect.Contains(mouse.X, mouse.Y))
            {
                var sel = _mapRenderer.GetSelectedEntity();
                if (sel != null && sel.Type == "player")
                {
                    int dist = Math.Abs(sel.X - _mapRenderer.GetPlayerX()) + Math.Abs(sel.Y - _mapRenderer.GetPlayerY());
                    if (dist <= 1)
                    {
                        Logger.Action($"Запрос обмена: {sel.Name}");
                        _ = GameMain.Instance!.Client.SendAsync("trade_request", new { TargetName = sel.Name });
                    }
                    else
                    {
                        Logger.Action($"Подхожу к {sel.Name} для обмена...");
                        _pendingTradeTarget = sel.Name;
                    }
                }
                mouseOverAnyWindow = true;
            }
            else if (_partyLeaveRect.Contains(mouse.X, mouse.Y))
            {
                Logger.Action("Покинуть пати");
                _ = GameMain.Instance!.Client.SendAsync("party_leave", (object?)null);
                mouseOverAnyWindow = true;
            }
            else if (_partyDisbandRect.Contains(mouse.X, mouse.Y))
            {
                Logger.Action("Распустить пати");
                _ = GameMain.Instance!.Client.SendAsync("party_leave", (object?)null);
                mouseOverAnyWindow = true;
            }
        }

        // Курсор над хотбаром — не пропускаем клики на карту (double-click pathing и т.п.)
        bool overHotbar = HitHotbarSlot(mouse.X, mouse.Y) >= 0;

        // Зум карты колесом мыши (только когда курсор не над окном/иконками/хотбаром)
        if (!mouseOverAnyWindow && !overHotbar)
        {
            int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
            if (scroll != 0)
                _mapRenderer.ChangeZoom(scroll > 0 ? 0.15f : -0.15f);
        }

        // Мышь по карте (не кликаем сквозь окна, иконки и хотбар)
        if (!mouseOverAnyWindow && !overHotbar)
            _inputManager.HandleMapClick(mouse, _prevMouse, _mapRenderer);

        _prevKeyboard = keyboard;
        _prevMouse = mouse;
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        int w = GameMain.Instance!.Graphics.PreferredBackBufferWidth;
        int h = GameMain.Instance!.Graphics.PreferredBackBufferHeight;

        // Размеры зон HUD (боковые панели убраны, карта на весь экран)
        int topH = 40;

        // Top bar
        DrawTopBar(spriteBatch, w, topH);

        // Map — растянута на весь экран (под топбаром до нижней границы)
        _mapRenderer.Draw(spriteBatch, 0, topH, w, h - topH);

        // Трекер заданий — правый верхний угол
        DrawQuestTracker(spriteBatch, w);

        // Панель игрока: квадрат уровня + бары (здоровье/манна/опыт) в левом верхнем углу
        _hudRenderer.DrawPlayerStatusPanel(spriteBatch, 8, topH + 8);

        // Полоса здоровья цели (по центру сверху) — рисуем ПОСЛЕ карты, иначе она перекроет.
        // В мирном режиме берём выбранную сущность с карты.
        _hudRenderer.SetSelectedEntity(_mapRenderer.GetSelectedEntity());
        _hudRenderer.DrawTargetBar(spriteBatch, w);

        // Кнопки под TargetBar, если выбран игрок: «Пригласить в группу» / «В группе» + «Обмен»
        var sel = _mapRenderer.GetSelectedEntity();
        _invitePartyRect = Rectangle.Empty;
        _tradePlayerRect = Rectangle.Empty;
        var party = _hudRenderer.Party;
        bool canInvite = sel != null && sel.Type == "player" &&
            (party == null || party.Members.Count == 0 || GameMain.Instance!.Client.PlayerId == party.LeaderId);
        bool targetInParty = party != null && sel != null && sel.Type == "player" &&
            party.Members.Any(m => m.Name == sel.Name);
        if (sel != null && sel.Type == "player")
        {
            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (font != null)
            {
                int btnW = 200;
                int btnH = 24;
                int btnX = (w - btnW) / 2;
                int btnY = 84;

                if (canInvite)
                {
                    _invitePartyRect = new Rectangle(btnX, btnY, btnW, btnH);
                    var ms = Mouse.GetState();
                    bool hov = _invitePartyRect.Contains(ms.X, ms.Y);
                    spriteBatch.Draw(SpriteCache.Pixel, _invitePartyRect, hov ? new Color(70, 190, 100) : new Color(50, 160, 80));
                    var txt = "Пригласить в группу";
                    var ts = font.MeasureString(txt);
                    spriteBatch.DrawString(font, txt, new Vector2(btnX + (btnW - ts.X) / 2, btnY + (btnH - ts.Y) / 2), Color.White);
                    btnY += btnH + 4;
                }
                else if (targetInParty)
                {
                    // Уже в группе — неактивная кнопка вместо приглашения
                    var infoRect = new Rectangle(btnX, btnY, btnW, btnH);
                    spriteBatch.Draw(SpriteCache.Pixel, infoRect, new Color(70, 75, 90));
                    var txt = "В группе";
                    var ts = font.MeasureString(txt);
                    spriteBatch.DrawString(font, txt, new Vector2(btnX + (btnW - ts.X) / 2, btnY + (btnH - ts.Y) / 2), new Color(180, 185, 195));
                    btnY += btnH + 4;
                }

                _tradePlayerRect = new Rectangle(btnX, btnY, btnW, btnH);
                var tMs = Mouse.GetState();
                bool tHov = _tradePlayerRect.Contains(tMs.X, tMs.Y);
                spriteBatch.Draw(SpriteCache.Pixel, _tradePlayerRect, tHov ? new Color(200, 170, 60) : new Color(160, 130, 40));
                var tTxt = "Обмен";
                var tSz = font.MeasureString(tTxt);
                spriteBatch.DrawString(font, tTxt, new Vector2(btnX + (btnW - tSz.X) / 2, btnY + (btnH - tSz.Y) / 2), Color.White);
            }
        }

        // Панель пати — левая сторона под статус-баром
        DrawPartyPanel(spriteBatch, 8, topH + 8 + 60 + 8, 240);

        // Hotbar — у нижней границы, ширина 50% экрана
        int hotbarH = 64;
        int hotbarW = (int)(w * 0.35f);
        int hotbarX = (w - hotbarW) / 2;
        int hotbarY = h - hotbarH - 8;
        var hotbarIcons = new Texture2D?[10];
        var hotbarCounts = new int[10];
        var cdRemain = new int[10];
        var cdTotal = new int[10];
        int hoverSlot = HitHotbarSlot(Mouse.GetState().X, Mouse.GetState().Y);
        int highlightSlot = _pendingSlot; // жёлтая рамка для заготовленного навыка
        for (int i = 0; i < 10; i++)
        {
            hotbarIcons[i] = _inputManager.GetHotbarIcon(i);
            hotbarCounts[i] = _inputManager.GetHotbarItemCount(i);
            if (_hotbarCooldowns.TryGetValue(i, out var cd))
            {
                int remMs = (int)(cd.End - DateTime.UtcNow).TotalMilliseconds;
                if (remMs <= 0)
                    _hotbarCooldowns.Remove(i);
                else { cdRemain[i] = remMs; cdTotal[i] = cd.Total; }
            }
        }
        _hudRenderer.DrawHotbar(spriteBatch, hotbarX, hotbarY, hotbarW, hotbarH, _inputManager.HotbarSlots, hotbarIcons, hotbarCounts,
            hoverSlot, highlightSlot, cdRemain, cdTotal);

        // Chat — левый нижний угол; правая граница доходит до левой границы хотбара
        int hotbarLeft = (w - hotbarW) / 2;
        int chatX = 8;
        int chatW = hotbarLeft - chatX - 8;
        int chatH = 180;
        int chatY = h - chatH - 8;
        _chatRenderer.Draw(spriteBatch, chatX, chatY, chatW, chatH);

        // Иконки в правом нижнем углу — на уровне хотбара, прижаты к нижней границе
        int iconCount = 7;
        int iconsTotalW = iconCount * IconSize + (iconCount - 1) * IconGap;
        int iconY = h - IconSize - 8;
        int iconStartX = w - 8 - iconsTotalW;
        _iconRects = new Rectangle[iconCount];
        for (int i = 0; i < iconCount; i++)
            _iconRects[i] = new Rectangle(iconStartX + i * (IconSize + IconGap), iconY, IconSize, IconSize);
        DrawIconBar(spriteBatch);

        // Затенение фона когда открыто окно настроек
        if (_settingsWindow.Visible)
            spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(0, 0, w, h), new Color(0, 0, 0, 140));

        // Окна (z-order и перекрытия управляются WindowManager)
        _windows.Draw(gameTime, spriteBatch);

        // Перетаскиваемый предмет — рисуем поверх всех окон
        if (_dragOverlayItem != null)
        {
            var ms = Mouse.GetState();
            var spr = SpriteCache.ForItemType(_dragOverlayItem.Type);
            int sz = 44;
            var r = new Rectangle(ms.X - sz / 2, ms.Y - sz / 2, sz, sz);
            if (spr != null)
                spriteBatch.Draw(spr, r, Color.White * 0.95f);
            else
                spriteBatch.Draw(SpriteCache.Pixel, r, new Color(120, 120, 140, 240));
        }
        else if (_dragOverlaySkill != null)
        {
            var ms = Mouse.GetState();
            int sz = 44;
            var r = new Rectangle(ms.X - sz / 2, ms.Y - sz / 2, sz, sz);
            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            var spr = SpriteCache.ForItemType(_dragOverlaySkill.Type);
            spriteBatch.Draw(SpriteCache.Pixel, r, new Color(44, 48, 64, 235));
            if (spr != null)
                spriteBatch.Draw(spr, new Rectangle(r.X + 8, r.Y + 8, 28, 28), Color.White);
            if (font != null)
            {
                var label = _dragOverlaySkill.Name;
                var m = font.MeasureString(label);
                spriteBatch.DrawString(font, label, new Vector2(ms.X - m.X / 2, ms.Y + sz / 2 + 2), new Color(200, 220, 255));
            }
            // Подсветка целевой ячейки хотбара
            int idx = HitHotbarSlot(ms.X, ms.Y);
            if (idx >= 0)
            {
                var g = GameMain.Instance!.Graphics;
                int hbW = (int)(g.PreferredBackBufferWidth * 0.35f);
                int hbX = (g.PreferredBackBufferWidth - hbW) / 2;
                int hbY = g.PreferredBackBufferHeight - 64 - 8;
                int slotW = hbW / 10;
                int hbSize = slotW - 6;
                var slotRect = new Rectangle(hbX + idx * slotW + (slotW - hbSize) / 2, hbY + (64 - hbSize) / 2, hbSize, hbSize);
                spriteBatch.Draw(SpriteCache.Pixel, slotRect, new Color(90, 150, 220, 120));
            }
        }

        // Подсветка хотбара при перетаскивании предмета
        if (_dragOverlayItem != null)
        {
            int idx = HitHotbarSlot(Mouse.GetState().X, Mouse.GetState().Y);
            if (idx >= 0)
            {
                var g = GameMain.Instance!.Graphics;
                int hbW = (int)(g.PreferredBackBufferWidth * 0.35f);
                int hbX = (g.PreferredBackBufferWidth - hbW) / 2;
                int hbY = g.PreferredBackBufferHeight - 64 - 8;
                int slotW = hbW / 10;
                int hbSize = slotW - 6;
                var slotRect = new Rectangle(hbX + idx * slotW + (slotW - hbSize) / 2, hbY + (64 - hbSize) / 2, hbSize, hbSize);
                spriteBatch.Draw(SpriteCache.Pixel, slotRect, new Color(90, 200, 120, 130));
            }
        }

        spriteBatch.End();
    }

    private void DrawIconBar(SpriteBatch sb)
    {
        var mouse = Mouse.GetState();
        if (_iconRects.Length < 7) return;
        var icons = new Texture2D?[]
        {
            SpriteCache.GetIconStatus(),
            SpriteCache.GetIconInventory(),
            SpriteCache.GetIconSkills(),
            SpriteCache.ForItemType("weapon"), // снаряжение
            SpriteCache.GetIconCommunication(),
            null, // место под журнал заданий (нет картинки)
            SpriteCache.GetIconSettings()
        };

        for (int i = 0; i < 7; i++)
        {
            var r = _iconRects[i];
            bool hover = r.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, r, hover ? new Color(70, 75, 95) : new Color(45, 48, 60));
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, 2), new Color(90, 95, 115));

            var spr = icons[i];
            if (spr != null)
            {
                int pad = 6;
                sb.Draw(spr, new Rectangle(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2), Color.White);
            }
            else
            {
                // Плейсхолдер для журнала заданий
                var font = SpriteCache.FontSmall ?? SpriteCache.Font;
                if (font != null)
                    sb.DrawString(font, "J", new Vector2(r.X + r.Width / 2 - 4, r.Y + r.Height / 2 - 8), Color.Gray);
            }
        }
    }

    private void DrawTopBar(SpriteBatch sb, int w, int h)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(0, 0, w, h), new Color(220, 225, 235));

        var font = SpriteCache.Font;
        if (font == null) return;

        var client = GameMain.Instance!.Client;
        string status = client.IsConnected ? "Подключено" : "Отключено";
        Color statusColor = client.IsConnected ? Color.LimeGreen : Color.Red;

        sb.DrawString(font, "IP:", new Vector2(10, 10), Color.Black);
        sb.DrawString(font, "127.0.0.1", new Vector2(40, 10), Color.Black);
        sb.DrawString(font, status, new Vector2(200, 10), statusColor);

        if (client.Status != null)
        {
            var info = $"Золото: {client.Status.Gold}  |  ATK {client.Status.TotalAttack}  DEF {client.Status.TotalDefense}";
            sb.DrawString(font, info, new Vector2(350, 10), new Color(60, 60, 70));
        }
    }

    public void Dispose() { }

    // Трекер активных заданий — компактная панель в правом верхнем углу
    private void DrawQuestTracker(SpriteBatch sb, int screenW)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        if (_activeQuests.Count == 0) return;

        int pad = 8;
        int maxTracked = 4;
        var tracked = _activeQuests.Take(maxTracked).ToList();

        // Измеряем ширину/высоту
        int lineH = 16;
        int headerH = 18;
        int blockH = headerH + tracked.Count * lineH + 8;
        int maxW = (int)font.MeasureString("ЗАДАНИЯ").X + pad * 2;
        int maxTextW = 320;
        foreach (var q in tracked)
        {
            string objLine = $"• {q.Title}  [{Math.Min(q.Current, q.Target)}/{q.Target}]";
            maxW = Math.Max(maxW, (int)font.MeasureString(objLine).X);
        }
        maxW = Math.Min(maxW, maxTextW);
        int boxW = maxW + pad * 2;
        int boxX = screenW - boxW - 12;
        int boxY = 48 + 30; // под координатами (topH=40 + плашка координат)

        sb.Draw(SpriteCache.Pixel, new Rectangle(boxX, boxY, boxW, blockH), new Color(20, 22, 30, 200));
        sb.Draw(SpriteCache.Pixel, new Rectangle(boxX, boxY, boxW, 2), new Color(90, 95, 115));

        sb.DrawString(font, "ЗАДАНИЯ", new Vector2(boxX + pad, boxY + 4), new Color(220, 200, 120));
        int y = boxY + headerH;
        foreach (var q in tracked)
        {
            bool done = q.Completed || (q.Current >= q.Target && q.Target > 0);
            Color c = done ? new Color(255, 210, 60) : Color.White;
            string line = $"• {q.Title}  [{Math.Min(q.Current, q.Target)}/{q.Target}]";
            // Обрезаем длинные названия
            while (line.Length > 4 && font.MeasureString(line).X > maxTextW)
                line = line.Substring(0, line.Length - 4) + "...";
            sb.DrawString(font, line, new Vector2(boxX + pad, y), c);
            y += lineH;
        }
    }

    private void PositionTradeWindows()
    {
        var g = GameMain.Instance!.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;

        int gap = 16;
        int shopW = _shopWindow.Width;
        int invW = _inventoryWindow.Width;
        int totalW = shopW + invW + gap;
        int startX = Math.Max(8, (sw - totalW) / 2);
        int y = Math.Max(50, (sh - _shopWindow.Height) / 2);

        _shopWindow.X = startX;
        _shopWindow.Y = y;
        _inventoryWindow.X = startX + shopW + gap;
        _inventoryWindow.Y = y;
    }

    private void PositionInventoryRight()
    {
        // В режиме торговли позиции задаёт PositionTradeWindows
        if (_shopWindow.Visible) return;
        var g = GameMain.Instance!.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;
        _inventoryWindow.X = Math.Max(8, sw - _inventoryWindow.Width - 16);
        _inventoryWindow.Y = Math.Max(50, (sh - _inventoryWindow.Height) / 2);
    }

    private void CenterWindow(Windows.GameWindow w)
    {
        var g = GameMain.Instance!.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;
        w.X = Math.Max(8, (sw - w.Width) / 2);
        w.Y = Math.Max(50, (sh - w.Height) / 2);
    }

    private void OpenEquipmentBesideInventory()
    {
        if (_equipmentWindow.Visible) return; // уже открыто — не дублируем
        var g = GameMain.Instance!.Graphics;
        int sh = g.PreferredBackBufferHeight;
        _equipmentWindow.Y = Math.Max(50, (sh - _equipmentWindow.Height) / 2);
        // Слева от инвентаря
        _equipmentWindow.X = _inventoryWindow.X - _equipmentWindow.Width - 16;
        if (_equipmentWindow.X < 8) _equipmentWindow.X = 8;
        _equipmentWindow.Visible = true;
    }

    private void OpenQuantity(string itemName, int max, int pricePerUnit, Action<int> onConfirm, bool showPrice = true)
    {
        if (max <= 1) { onConfirm(1); return; }
        _quantityDialog.IsModal = true;
        _quantityDialog.ShowPrice = showPrice;
        _quantityDialog.Setup(itemName, max, pricePerUnit);
        _quantityDialog.OnConfirm = q => onConfirm(q);
        // Центрируем по экрану поверх всех окон
        var g = GameMain.Instance!.Graphics;
        _quantityDialog.X = (g.PreferredBackBufferWidth - _quantityDialog.Width) / 2;
        _quantityDialog.Y = (g.PreferredBackBufferHeight - _quantityDialog.Height) / 2;
        _windows.BringToFront(_quantityDialog);
    }

    private void RequestSell(Item item)
    {
        // Продажа: сервер сам определяет цену (Balance.SellPrice)
        _ = GameMain.Instance!.Client.SendAsync("sell", new { ItemId = item.Id, Quantity = 1 });
    }

    private void ActivateHotbarSlot(int idx, string item)
    {
        if (item.StartsWith("skill:"))
        {
            // Навык в кулдауне — нельзя ни заготовить, ни применить
            if (_hotbarCooldowns.ContainsKey(idx))
                return;

            var skillName = item[6..];
            var skill = _inputManager.GetSkillByName(skillName);
            if (skill != null)
            {
                // Повторный клик по тому же навыку — отмена заготовки
                if (_pendingSkillId == skill.Id)
                {
                    _pendingSkillId = null;
                    _pendingSlot = -1;
                    _pendingSent = false;
                    return;
                }

                // Клик по навыку = заготовить. Если цель уже выбрана (монстр),
                // начинаем бой по ней — далее игрок пойдёт к монстру и навык применится.
                _pendingSkillId = skill.Id;
                _pendingSlot = idx;
                _pendingSent = false;

                var sel = _mapRenderer.GetSelectedEntity();
                if (sel != null && sel.Type == "monster" && sel.Id != null)
                {
                    _ = GameMain.Instance!.Client.SendAsync("interact_target",
                        new { Type = "monster", X = sel.X, Y = sel.Y, MonsterId = sel.Id });
                }
            }
        }
        else
        {
            var name = item.StartsWith("item:") ? item["item:".Length..] : item;
            var invItem = _inputManager.GetItemByName(name);
            if (invItem != null)
            {
                if (invItem.Type == "consumable" && invItem.HealAmount > 0)
                    _ = GameMain.Instance!.Client.SendAsync("use_item", new { ItemId = invItem.Id });
                else if (invItem.Type is "weapon" or "armor" or "accessory")
                    _ = GameMain.Instance!.Client.SendAsync("equip", new { ItemId = invItem.Id });
            }
        }
    }

    private void RequestSellDialog(Item item, int max)
    {
        OpenQuantity(item.Name, max, 1, q => _ = GameMain.Instance!.Client.SendAsync("sell", new { ItemId = item.Id, Quantity = q }));
    }

    // Определяет индекс слота хотбара (0..9) по координатам мыши, или -1.
    private int HitHotbarSlot(int mx, int my)
    {
        var g = GameMain.Instance!.Graphics;
        int w = g.PreferredBackBufferWidth;
        int h = g.PreferredBackBufferHeight;

        int hotbarH = 64;
        int hotbarW = (int)(w * 0.35f);
        int hotbarX = (w - hotbarW) / 2;
        int hotbarY = h - hotbarH - 8;

        if (mx < hotbarX || mx > hotbarX + hotbarW || my < hotbarY || my > hotbarY + hotbarH)
            return -1;

        int slotW = hotbarW / 10;
        int idx = (mx - hotbarX) / slotW;
        return idx >= 0 && idx < 10 ? idx : -1;
    }

    private void HandleSkillDragEnd()
    {
        var skill = _dragOverlaySkill;
        _dragOverlaySkill = null;
        if (skill == null) return;

        var ms = Mouse.GetState();
        int idx = HitHotbarSlot(ms.X, ms.Y);
        if (idx < 0) return;

        var client = GameMain.Instance!.Client;
        var slots = _inputManager.HotbarSlots.ToList();
        while (slots.Count < 10) slots.Add(null);
        // Повторный drop в ту же занятую ячейку — очищаем (toggle)
        if (slots[idx] == "skill:" + skill.Name)
            slots[idx] = null;
        else
            slots[idx] = "skill:" + skill.Name;

        _inputManager.UpdateHotbar(slots.ToArray());
        _ = client.SendAsync("hotbar_update", new { Slots = slots });
    }

    private void DrawPartyPanel(SpriteBatch sb, int x, int y, int panelW)
    {
        var party = _hudRenderer.Party;
        if (party == null || party.Members.Count == 0) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        bool isLeader = GameMain.Instance!.Client.PlayerId == party.LeaderId;

        int headerH = 22;
        int memberNameH = 16;
        int barH = 14;
        int memberH = memberNameH + barH + 6;
        int btnH = 26;
        int btnGap = 4;
        int padding = 10;

        int buttonsH = btnH + padding;
        if (isLeader) buttonsH += btnH + btnGap;

        int panelH = headerH + party.Members.Count * memberH + buttonsH;

        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, panelW, panelH), new Color(30, 35, 48, 220));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, panelW, 1), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y + panelH - 1, panelW, 1), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, 1, panelH), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + panelW - 1, y, 1, panelH), new Color(80, 90, 110));

        int cx = x + 8;
        int cw = panelW - 16;
        int cy = y + 4;

        string title = $"Группа ({party.Members.Count}/5)";
        sb.DrawString(font, title, new Vector2(cx, cy), new Color(220, 200, 120));
        cy += headerH;

        foreach (var m in party.Members)
        {
            bool mLdr = m.PlayerId == party.LeaderId;
            string nameStr = (mLdr ? "★ " : "  ") + m.Name;
            sb.DrawString(font, nameStr, new Vector2(cx, cy), mLdr ? new Color(220, 200, 120) : new Color(200, 200, 210));
            cy += memberNameH;

            float hpPct = m.MaxHealth > 0 ? (float)m.Health / m.MaxHealth : 0;
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, barH), new Color(60, 30, 30));
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, (int)(cw * hpPct), barH), new Color(180, 50, 50));
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, 1), new Color(80, 40, 40));
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy + barH - 1, cw, 1), new Color(80, 40, 40));

            string hpText = $"{m.Health}/{m.MaxHealth}";
            var hpSize = font.MeasureString(hpText);
            sb.DrawString(font, hpText, new Vector2(cx + (cw - hpSize.X) / 2, cy + (barH - hpSize.Y) / 2), Color.White);

            cy += barH + 4;
        }

        cy += btnGap;
        var ms = Mouse.GetState();

        _partyLeaveRect = new Rectangle(cx, cy, cw, btnH);
        bool leaveHov = _partyLeaveRect.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, _partyLeaveRect, leaveHov ? new Color(180, 70, 70) : new Color(130, 50, 50));
        string leaveTxt = "Покинуть группу";
        var leaveSize = font.MeasureString(leaveTxt);
        sb.DrawString(font, leaveTxt, new Vector2(_partyLeaveRect.X + (_partyLeaveRect.Width - leaveSize.X) / 2, _partyLeaveRect.Y + (_partyLeaveRect.Height - leaveSize.Y) / 2), Color.White);

        if (isLeader)
        {
            cy += btnH + btnGap;
            _partyDisbandRect = new Rectangle(cx, cy, cw, btnH);
            bool disbandHov = _partyDisbandRect.Contains(ms.X, ms.Y);
            sb.Draw(SpriteCache.Pixel, _partyDisbandRect, disbandHov ? new Color(190, 80, 80) : new Color(150, 60, 60));
            string disbandTxt = "Распустить группу";
            var disbandSize = font.MeasureString(disbandTxt);
            sb.DrawString(font, disbandTxt, new Vector2(_partyDisbandRect.X + (_partyDisbandRect.Width - disbandSize.X) / 2, _partyDisbandRect.Y + (_partyDisbandRect.Height - disbandSize.Y) / 2), Color.White);
        }
        else
        {
            _partyDisbandRect = Rectangle.Empty;
        }
    }
}
