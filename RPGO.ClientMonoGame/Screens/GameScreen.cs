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
    private readonly WindowManager _windows = new();
    private readonly GameInputHandler _input;
    private readonly GameHudRenderer _hudDraw;

    // Windows
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
    private int _lastPartyMemberCount;
    private HashSet<Guid> _lastPartyMemberIds = new();

    public GameScreen()
    {
        var client = GameMain.Instance!.Client;

        _mapRenderer = new MapRenderer();
        _hudRenderer = new HudRenderer();
        _chatRenderer = new ChatRenderer();
        _inputManager = new InputManager();
        _input = new GameInputHandler(_inputManager, _mapRenderer, _hudRenderer, _chatRenderer, _windows);
        _hudDraw = new GameHudRenderer(_hudRenderer, _mapRenderer);

        _socialWindow = new SocialWindow(client);
        _socialWindow.WhisperRequested += name =>
        {
            _chatRenderer.IsTyping = true;
            _chatRenderer.TypedText = $"/w {name} ";
        };

        // === GameClient events ===
        client.MapUpdated += map =>
        {
            _mapRenderer.SetMap(map);
            _mapRenderer.SetPlayerName(client.PlayerName);
            _mapRenderer.SetPlayerLevel(client.PlayerLevel);
        };
        client.FloatingTextReceived += (x, y, text, argb, isCrit) =>
        {
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
            if (!inCombat)
            {
                _mapRenderer.ClearSelection();
                _hudRenderer.UpdateTargetDebuffs(null);
            }
        };
        client.TargetDebuffsUpdated += debuffs =>
        {
            _hudRenderer.UpdateTargetDebuffs(debuffs);
        };
        client.AttackCooldownUpdated += (skillId, remainingMs, totalMs) =>
        {
            int slot = -1;
            var slots = _inputManager.HotbarSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == "skill:" + (_inputManager.GetSkillById(skillId)?.Name ?? ""))
                    slot = i;
            if (slot >= 0 && totalMs > 0)
                _input.HotbarCooldowns[slot] = (DateTime.UtcNow.AddMilliseconds(remainingMs), totalMs);
            if (_input.PendingSkillId == skillId)
            {
                _input.PendingSkillId = null;
                _input.PendingSlot = -1;
                _input.PendingSent = false;
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
            var myName = GameMain.Instance!.Client.PlayerName;
            var groupNames = party.Members
                .Where(m => !string.Equals(m.Name, myName, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Name).ToList();
            _mapRenderer.SetPartyMembers(groupNames);
            var myId = GameMain.Instance!.Client.PlayerId;
            if (_lastPartyMemberCount == 0 && party.Members.Count >= 2)
                _chatRenderer.AddMessage(ChatChannel.Party, "Группа", "Группа сформирована!");
            else
                foreach (var m in party.Members)
                    if (m.PlayerId != myId && !_lastPartyMemberIds.Contains(m.PlayerId))
                        _chatRenderer.AddMessage(ChatChannel.Party, "Группа", $"{m.Name} присоединился к группе");
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
            _input.PlayerGoldCache = status.Gold;
            _skillsWindow.SetPlayerLevel(status.Level);
            if (_input.LastXp < 0) { _input.LastXp = status.Experience; _input.LastLevel = status.Level; }
            else
            {
                int xpGain = status.Experience - _input.LastXp;
                if (xpGain > 0)
                    _mapRenderer.SpawnFloatingTextAtPlayer($"+{xpGain} XP", new Color(120, 220, 255));
                if (status.Level > _input.LastLevel)
                    _mapRenderer.SpawnFloatingTextAtPlayer("Новый уровень!", Color.Gold, true);
                _input.LastXp = status.Experience;
                _input.LastLevel = status.Level;
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
            _input.DragOverlayItem = item;
        };
        _equipmentWindow.DragStateChanged += item =>
        {
            _equipmentWindow.DraggingType = item?.Type;
            _input.DragOverlayItem = item;
        };
        _equipmentWindow.IsOverInventory = pt => _inventoryWindow.Contains(pt);
        _lootWindow.DragStateChanged += item => _input.DragOverlayItem = item;
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
        client.ChatReceived += (channel, name, text, isAdmin) =>
        {
            if (Enum.TryParse<ChatChannel>(channel, out var ch))
                _chatRenderer.AddMessage(ch, name, text, isAdmin);
            else
                _chatRenderer.AddMessage(ChatChannel.System, name, text, isAdmin);
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
            _input.PositionTradeWindows(_shopWindow, _inventoryWindow, GameMain.Instance!);
        };
        _shopWindow.Closed += () =>
        {
            _shopWindow.Visible = false;
            _inventoryWindow.Visible = false;
            _inventoryWindow.ShopMode = false;
        };
        _shopWindow.BuyItem += (id, qty) => _ = client.SendAsync("buy", new { ItemId = id, Quantity = qty });
        _shopWindow.DragStateChanged += item => _input.DragOverlayItem = item;
        _shopWindow.SellAllTrophies += () => _ = client.SendAsync("sell_all_trophies", new { });
        _shopWindow.DropOnInventory += (pt, item) =>
        {
            if (!_inventoryWindow.Visible || !_inventoryWindow.Contains(pt)) return false;
            int stock = Math.Max(1, item.Stock);
            int maxAffordable = _shopWindow.DiscountedPrice(item) > 0
                ? _input.PlayerGoldCache / _shopWindow.DiscountedPrice(item) : stock;
            int max = Math.Min(stock, Math.Max(1, maxAffordable));
            if (stock > 1 || item.MaxStack > 1)
                _input.OpenQuantity(item.Name, max, _shopWindow.DiscountedPrice(item),
                    q => _ = client.SendAsync("buy", new { ItemId = item.Id, Quantity = q }), true, _quantityDialog, GameMain.Instance!);
            else
                _ = client.SendAsync("buy", new { ItemId = item.Id, Quantity = 1 });
            return true;
        };
        _shopWindow.PendingBuy += (item, max) =>
        {
            int stock = Math.Max(1, item.Stock);
            int maxAffordable = _shopWindow.DiscountedPrice(item) > 0
                ? _input.PlayerGoldCache / _shopWindow.DiscountedPrice(item) : stock;
            int realMax = Math.Min(max, Math.Max(1, maxAffordable));
            _input.OpenQuantity(item.Name, realMax, _shopWindow.DiscountedPrice(item),
                q => _ = client.SendAsync("buy", new { ItemId = item.Id, Quantity = q }), true, _quantityDialog, GameMain.Instance!);
        };
        _inventoryWindow.DropOnSell += (pt, item) =>
        {
            if (!_shopWindow.Visible || !_shopWindow.Contains(pt)) return false;
            _ = client.SendAsync("sell", new { ItemId = item.Id, Quantity = 1 });
            return true;
        };
        _inventoryWindow.SellItem += (id, qty) => _ = client.SendAsync("sell", new { ItemId = id, Quantity = qty });
        _inventoryWindow.PendingSell += (item, max) =>
            _input.OpenQuantity(item.Name, max, 1, q => _ = GameMain.Instance!.Client.SendAsync("sell", new { ItemId = item.Id, Quantity = q }), true, _quantityDialog, GameMain.Instance!);
        _inventoryWindow.PendingDrop += (item, max) =>
            _input.OpenQuantity(item.Name, max, 1, q => _ = client.SendAsync("drop_item", new { ItemId = item.Id, Quantity = q }), false, _quantityDialog, GameMain.Instance!);
        client.TradeOpened += data =>
        {
            var inv = data.YourInventory ?? new List<TradeItemData>();
            var grouped = inv.GroupBy(i => i.Id).Select(gr => $"{gr.First().Name} x{gr.Count()}").ToList();
            Logger.Action($"ОБМЕН: получен trade_open с '{data.OtherName}', предметов в инвентаре={inv.Count} (уникальных={grouped.Count}), золото={data.YourGold}");
            foreach (var line in grouped) Logger.Debug($"ОБМЕН: инвентарь аккаунта -> {line}");
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
            var lootItems = items.Select(item => new RPGGame.ClientMonoGame.Networking.LootItemInfo
            {
                Id = item.Id, Name = item.Name, Type = item.Type, Value = item.Value, Description = item.Description
            }).ToList();
            _lootWindow.Setup(corpseId, monsterName, damagePct, lootItems, gold);
            var g = GameMain.Instance!.Graphics;
            _lootWindow.X = Math.Max(0, (g.PreferredBackBufferWidth - _lootWindow.Width) / 2);
            _lootWindow.Y = Math.Max(0, (g.PreferredBackBufferHeight - _lootWindow.Height) / 2);
        };

        // Map events
        _mapRenderer.MoveRequested += (x, y) =>
        {
            Logger.Action($"Движение в клетку ({x}, {y})");
            _ = client.SendAsync("move_to", new { X = x, Y = y });
        };
        _mapRenderer.InteractRequested += (entity, x, y) =>
        {
            Logger.Action($"Взаимодействие с {entity.Type} '{entity.Name}' ({x}, {y})");
            if (entity.Type == "corpse" && entity.Id != null)
                _ = client.SendAsync("loot_corpse", new { CorpseId = entity.Id });
            else
                _ = client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, MonsterId = entity.Id?.ToString() });
        };
        _mapRenderer.EntityPickRequested += (entities, mapX, mapY) =>
        {
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

        // Hotbar
        _inputManager.HotbarActivated += (idx, item) => _input.ActivateHotbarSlot(idx, item, GameMain.Instance!);

        // Trade window events
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
        _tradeWindow.OfferChanged += (entries, gold) =>
            _ = client.SendAsync("trade_offer", new { Entries = entries, Gold = gold });
        _tradeWindow.RequestQuantity += (itemName, max, defaultQty, onConfirm) =>
            _input.OpenQuantity(itemName, max, 0, onConfirm, false, _quantityDialog, GameMain.Instance!);
        _tradeWindow.ConfirmRequested += () => _ = client.SendAsync("trade_confirm", null);
        _tradeWindow.CancelRequested += () => _ = client.SendAsync("trade_cancel", null);

        // Quests
        _questBoardWindow.TakeQuest += id => _ = client.SendAsync("take_quest", new { QuestId = id });
        _questBoardWindow.CompleteQuest += id => _ = client.SendAsync("complete_quest", new { QuestId = id });
        _questBoardWindow.AbandonQuest += id => _ = client.SendAsync("abandon_quest", new { QuestId = id });
        _questLogWindow.AbandonQuest += id => _ = client.SendAsync("abandon_quest", new { QuestId = id });

        // Settings
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
            GameInputHandler.CenterWindow(_questBoardWindow, GameMain.Instance!);
        };

        // Skills
        _skillsWindow.UseSkill += id =>
        {
            if (_input.PendingSkillId == id)
            {
                _input.PendingSkillId = null;
                _input.PendingSlot = -1;
                return;
            }
            int slot = -1;
            var slots = _inputManager.HotbarSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == "skill:" + (_inputManager.GetSkillById(id)?.Name ?? ""))
                    slot = i;
            _input.PendingSkillId = id;
            _input.PendingSlot = slot;
        };
        _skillsWindow.SkillDragStateChanged += skill => _input.DragOverlaySkill = skill;
        _skillsWindow.SkillDragEnded += () => _input.HandleSkillDragEnd(GameMain.Instance!);

        // Loot
        _lootWindow.TakeLoot += (corpseId, takeAll, ids, takeGold) =>
            _ = client.SendAsync("take_loot", new { CorpseId = corpseId, TakeAll = takeAll, ItemIds = ids, TakeGold = takeGold });

        // Inventory actions
        _inventoryWindow.EquipItem += id =>
        {
            _ = client.SendAsync("equip", new { ItemId = id });
            GameInputHandler.OpenEquipmentBesideInventory(_equipmentWindow, _inventoryWindow, GameMain.Instance!);
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
            var order = inv.Items.OrderBy(i => Cat(i.Type)).ThenBy(i => i.Name).Select(i => i.Id).ToList();
            _ = client.SendAsync("inventory_sort", new { Order = order });
        };
        _statusWindow.AllocateAttribute += attr => _ = client.SendAsync("allocate_attribute", new { Attribute = attr });

        // Register windows in manager
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
        var game = GameMain.Instance!;

        _input.HandleHotbarDrop(mouse, game);
        _windows.Update(gameTime, keyboard, mouse);

        bool settingsOpen = _settingsWindow.Visible;
        bool mouseOverAnyWindow = _windows.IsMouseOverVisibleWindow(mouse.X, mouse.Y);

        if (settingsOpen) { _input.PrevKeyboard = keyboard; _input.PrevMouse = mouse; return; }

        _input.HandlePendingTrade(game);
        _input.HandleHotbarClick(mouse, mouseOverAnyWindow, game);

        // Chat clicks
        {
            int hotbarW2 = (int)(game.Graphics.PreferredBackBufferWidth * 0.35f);
            int hotbarLeft2 = (game.Graphics.PreferredBackBufferWidth - hotbarW2) / 2;
            int chatX2 = 8;
            int chatW2 = hotbarLeft2 - chatX2 - 8;
            int chatH2 = 180;
            int chatY2 = game.Graphics.PreferredBackBufferHeight - chatH2 - 8;
            var chatRect = new Rectangle(chatX2, chatY2, chatW2, chatH2);
            bool chatPressed = mouse.LeftButton == ButtonState.Pressed && _input.PrevMouse.LeftButton == ButtonState.Released;
            bool chatHandled = _chatRenderer.HandleClick(mouse.X, mouse.Y, chatX2, chatY2, chatW2, chatH2, chatPressed);
            if (chatHandled) mouseOverAnyWindow = true;

            if (chatRect.Contains(mouse.X, mouse.Y))
            {
                mouseOverAnyWindow = true;
                int scroll = mouse.ScrollWheelValue - _input.PrevMouse.ScrollWheelValue;
                if (scroll != 0) _chatRenderer.HandleScroll(scroll > 0 ? -3 : 3, chatH2 - 54);
            }
        }

        _input.HandleEscape(keyboard);
        _input.HandlePendingSkill(game);
        _input.HandleChatInput(keyboard, game);
        _input.HandleWindowToggles(keyboard, game,
            _inventoryWindow, _statusWindow, _skillsWindow, _equipmentWindow,
            _questLogWindow, _socialWindow, _settingsWindow);
        if (!_chatRenderer.IsTyping)
            _inputManager.HandleHotbarKeys(keyboard, _input.PrevKeyboard);
        if (!_chatRenderer.IsTyping)
            _inputManager.HandleMovement(keyboard, _input.PrevKeyboard, client, _mapRenderer);

        // Icon clicks
        bool clickedIcon = false;
        if (_hudDraw.IconRects.Length >= 6 &&
            mouse.LeftButton == ButtonState.Pressed && _input.PrevMouse.LeftButton == ButtonState.Released)
        {
            int before = _hudDraw.IconRects.Length;
            _input.HandleIconClick(mouse, mouseOverAnyWindow, game,
                _inventoryWindow, _statusWindow, _skillsWindow, _equipmentWindow,
                _socialWindow, _questLogWindow, _settingsWindow, _hudDraw.IconRects);
            clickedIcon = _hudDraw.IconRects.Length == before && mouseOverAnyWindow;
        }
        mouseOverAnyWindow |= clickedIcon;

        // Party buttons
        bool partyHandled = _input.HandlePartyButtons(mouse, mouseOverAnyWindow, game,
            _hudDraw.InvitePartyRect, _hudDraw.TradePlayerRect,
            _hudDraw.PartyLeaveRect, _hudDraw.PartyDisbandRect);
        mouseOverAnyWindow |= partyHandled;

        bool overHotbar = _input.HitHotbarSlot(mouse.X, mouse.Y, game) >= 0;

        if (!mouseOverAnyWindow && !overHotbar)
        {
            int scroll = mouse.ScrollWheelValue - _input.PrevMouse.ScrollWheelValue;
            if (scroll != 0) _mapRenderer.ChangeZoom(scroll > 0 ? 0.15f : -0.15f);
        }
        if (!mouseOverAnyWindow && !overHotbar)
            _inputManager.HandleMapClick(mouse, _input.PrevMouse, _mapRenderer);

        _input.PrevKeyboard = keyboard;
        _input.PrevMouse = mouse;
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        int w = GameMain.Instance!.Graphics.PreferredBackBufferWidth;
        int h = GameMain.Instance!.Graphics.PreferredBackBufferHeight;
        int topH = 40;

        _hudDraw.DrawTopBar(spriteBatch, w, h, GameMain.Instance!);
        _mapRenderer.Draw(spriteBatch, 0, topH, w, h - topH);
        _hudDraw.DrawQuestTracker(spriteBatch, w, _activeQuests);
        _hudRenderer.DrawPlayerStatusPanel(spriteBatch, 8, topH + 8);
        float debuffH = _hudRenderer.DrawPlayerDebuffs(spriteBatch, 8, topH + 8 + 60 + 4, w - 16);
        _hudRenderer.SetSelectedEntity(_mapRenderer.GetSelectedEntity());
        _hudRenderer.DrawTargetBar(spriteBatch, w);
        _hudRenderer.DrawTargetDebuffs(spriteBatch, w, 64 + 18 + 4);
        _hudDraw.DrawTargetButtons(spriteBatch, w, GameMain.Instance!);
        int partyY = topH + 8 + 60 + 4 + (int)debuffH + 4;
        _hudDraw.DrawPartyPanel(spriteBatch, 8, partyY, 240, GameMain.Instance!);
        _hudRenderer.DrawDebuffTooltip(spriteBatch);

        // Hotbar
        int hotbarH = 64;
        int hotbarW = (int)(w * 0.35f);
        int hotbarX = (w - hotbarW) / 2;
        int hotbarY = h - hotbarH - 8;
        var hotbarIcons = new Texture2D?[10];
        var hotbarCounts = new int[10];
        var cdRemain = new int[10];
        var cdTotal = new int[10];
        int hoverSlot = _input.HitHotbarSlot(Mouse.GetState().X, Mouse.GetState().Y, GameMain.Instance!);
        int highlightSlot = _input.PendingSlot;
        for (int i = 0; i < 10; i++)
        {
            hotbarIcons[i] = _inputManager.GetHotbarIcon(i);
            hotbarCounts[i] = _inputManager.GetHotbarItemCount(i);
            if (_input.HotbarCooldowns.TryGetValue(i, out var cd))
            {
                int remMs = (int)(cd.End - DateTime.UtcNow).TotalMilliseconds;
                if (remMs <= 0) _input.HotbarCooldowns.Remove(i);
                else { cdRemain[i] = remMs; cdTotal[i] = cd.Total; }
            }
        }
        _hudRenderer.DrawHotbar(spriteBatch, hotbarX, hotbarY, hotbarW, hotbarH, _inputManager.HotbarSlots, hotbarIcons, hotbarCounts,
            hoverSlot, highlightSlot, cdRemain, cdTotal);

        // Chat
        int hotbarLeft = (w - hotbarW) / 2;
        int chatX = 8;
        int chatW = hotbarLeft - chatX - 8;
        int chatH = 180;
        int chatY = h - chatH - 8;
        _chatRenderer.Draw(spriteBatch, chatX, chatY, chatW, chatH);

        // Icon bar
        _hudDraw.LayoutIconBar(w, h);
        _hudDraw.DrawIconBar(spriteBatch);

        // Settings overlay
        if (_settingsWindow.Visible)
            spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(0, 0, w, h), new Color(0, 0, 0, 140));

        _windows.Draw(gameTime, spriteBatch);

        // Drag overlay
        int dragHitIdx = _input.HitHotbarSlot(Mouse.GetState().X, Mouse.GetState().Y, GameMain.Instance!);
        _hudDraw.DrawDragOverlay(spriteBatch, _input.DragOverlayItem, _input.DragOverlaySkill, dragHitIdx, GameMain.Instance!);

        spriteBatch.End();
    }

    public void Dispose() { }
}
