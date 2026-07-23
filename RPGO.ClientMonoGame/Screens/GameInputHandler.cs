using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Input;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Windows;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Screens;

internal class GameInputHandler
{
    internal readonly InputManager Input;
    internal readonly MapRenderer Map;
    internal readonly HudRenderer Hud;
    internal readonly ChatRenderer Chat;
    internal readonly WindowManager Windows;

    // Drag overlay state (shared with Draw)
    internal Item? DragOverlayItem;
    internal ClientSkillInfo? DragOverlaySkill;

    // Hotbar cooldowns: idx -> (end time, total ms)
    internal readonly Dictionary<int, (DateTime End, int Total)> HotbarCooldowns = new();

    // Pending skill state
    internal string? PendingSkillId;
    internal int PendingSlot = -1;
    internal bool PendingSent;

    // Pending trade
    internal string? PendingTradeTarget;
    private int _lastTradeRequestTime;
    private const int TradeRequestCooldownMs = 500;

    // Window open-order stack (LIFO) for ESC
    internal readonly List<Windows.GameWindow> WindowStack = new();

    // Gold cache
    internal int PlayerGoldCache;

    // XP tracking
    internal int LastXp = -1;
    internal int LastLevel = -1;

    // Previous input state
    internal KeyboardState PrevKeyboard;
    internal MouseState PrevMouse;

    internal GameInputHandler(InputManager input, MapRenderer map, HudRenderer hud, ChatRenderer chat, WindowManager windows)
    {
        Input = input;
        Map = map;
        Hud = hud;
        Chat = chat;
        Windows = windows;
    }

    internal void HandleHotbarDrop(MouseState mouse, GameMain game)
    {
        if (DragOverlayItem == null
            || mouse.LeftButton != ButtonState.Released
            || PrevMouse.LeftButton != ButtonState.Pressed)
            return;

        int idx = HitHotbarSlot(mouse.X, mouse.Y, game);
        if (idx < 0) return;

        var client = game.Client;
        var slots = Input.HotbarSlots.ToList();
        while (slots.Count < 10) slots.Add(null);
        string tag = "item:" + DragOverlayItem.Name;
        if (slots[idx] == tag) slots[idx] = null;
        else slots[idx] = tag;
        Input.UpdateHotbar(slots.ToArray());
        _ = client.SendAsync("hotbar_update", new { Slots = slots });
    }

    internal void HandlePendingTrade(GameMain game)
    {
        if (PendingTradeTarget == null) return;

        var client = game.Client;
        var targetEntity = Map.GetSelectedEntity();
        if (targetEntity != null && targetEntity.Type == "player" && targetEntity.Name == PendingTradeTarget)
        {
            int dist = Math.Abs(targetEntity.X - Map.GetPlayerX()) + Math.Abs(targetEntity.Y - Map.GetPlayerY());
            if (dist <= 1)
            {
                Logger.Action($"Запрос обмена: {PendingTradeTarget}");
                _ = client.SendAsync("trade_request", new { TargetName = PendingTradeTarget });
                PendingTradeTarget = null;
            }
            else
            {
                int now = Environment.TickCount;
                if (now - _lastTradeRequestTime >= TradeRequestCooldownMs)
                {
                    int dx = Math.Sign(targetEntity.X - Map.GetPlayerX());
                    int dy = Math.Sign(targetEntity.Y - Map.GetPlayerY());
                    int stepX = Map.GetPlayerX() + dx;
                    int stepY = Map.GetPlayerY() + dy;
                    _ = client.SendAsync("move_to", new { X = stepX, Y = stepY });
                    _lastTradeRequestTime = now;
                }
            }
        }
        else
        {
            PendingTradeTarget = null;
        }
    }

    internal void HandleHotbarClick(MouseState mouse, bool mouseOverAnyWindow, GameMain game)
    {
        if (mouseOverAnyWindow || DragOverlaySkill != null) return;

        bool leftClick = mouse.LeftButton == ButtonState.Pressed && PrevMouse.LeftButton == ButtonState.Released;
        bool rightClick = mouse.RightButton == ButtonState.Pressed && PrevMouse.RightButton == ButtonState.Released;
        if (!leftClick && !rightClick) return;

        int idx = HitHotbarSlot(mouse.X, mouse.Y, game);
        if (idx < 0) return;

        var client = game.Client;
        var slots = Input.HotbarSlots.ToList();
        while (slots.Count < 10) slots.Add(null);

        if (rightClick)
        {
            slots[idx] = null;
            Input.UpdateHotbar(slots.ToArray());
            _ = client.SendAsync("hotbar_update", new { Slots = slots });
        }
        else if (leftClick && !string.IsNullOrEmpty(slots[idx]))
        {
            ActivateHotbarSlot(idx, slots[idx]!, game);
        }
    }

    internal void HandleChatInput(KeyboardState keyboard, GameMain game)
    {
        // Layout switch: Shift+Alt or Win+Space
        bool winDown = keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
        bool altDown = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        bool shiftDown = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        bool altJustPressed = (keyboard.IsKeyDown(Keys.LeftAlt) && PrevKeyboard.IsKeyUp(Keys.LeftAlt))
                              || (keyboard.IsKeyDown(Keys.RightAlt) && PrevKeyboard.IsKeyUp(Keys.RightAlt));
        bool shiftJustPressed = (keyboard.IsKeyDown(Keys.LeftShift) && PrevKeyboard.IsKeyUp(Keys.LeftShift))
                                || (keyboard.IsKeyDown(Keys.RightShift) && PrevKeyboard.IsKeyUp(Keys.RightShift));
        if ((altDown && shiftDown && (altJustPressed || shiftJustPressed))
            || (winDown && keyboard.IsKeyDown(Keys.Space) && PrevKeyboard.IsKeyUp(Keys.Space)))
        {
            Chat.CurrentLayout = Chat.CurrentLayout == ChatRenderer.Layout.En
                ? ChatRenderer.Layout.Ru : ChatRenderer.Layout.En;
        }

        // Enter for typing/sending
        if (keyboard.IsKeyDown(Keys.Enter) && PrevKeyboard.IsKeyUp(Keys.Enter))
        {
            var client = game.Client;
            if (Chat.IsTyping)
            {
                if (!string.IsNullOrWhiteSpace(Chat.TypedText))
                {
                    Logger.Action($"Сообщение в чат: {Chat.TypedText}");
                    _ = client.SendAsync("say", Chat.TypedText);
                }
                Chat.IsTyping = false;
                Chat.TypedText = "";
            }
            else
            {
                Chat.IsTyping = true;
            }
        }

        // Text input
        if (Chat.IsTyping)
            Chat.HandleInput(keyboard, PrevKeyboard);
    }

    internal void HandleWindowToggles(KeyboardState keyboard, GameMain game,
        InventoryWindow inventory, StatusWindow status, SkillsWindow skills,
        EquipmentWindow equipment, QuestLogWindow questLog, SocialWindow social,
        SettingsWindow settings)
    {
        if (Chat.IsTyping) return;

        if (keyboard.IsKeyDown(Keys.I) && PrevKeyboard.IsKeyUp(Keys.I))
        {
            inventory.Visible = !inventory.Visible;
            if (inventory.Visible) { PositionInventoryRight(inventory, game); PushWindow(inventory); }
        }
        if (keyboard.IsKeyDown(Keys.J) && PrevKeyboard.IsKeyUp(Keys.J))
        {
            questLog.Visible = !questLog.Visible;
            if (questLog.Visible) { CenterWindow(questLog, game); PushWindow(questLog); }
        }
        if (keyboard.IsKeyDown(Keys.K) && PrevKeyboard.IsKeyUp(Keys.K))
        {
            skills.Visible = !skills.Visible;
            if (skills.Visible) { CenterWindow(skills, game); PushWindow(skills); }
        }
        if (keyboard.IsKeyDown(Keys.E) && PrevKeyboard.IsKeyUp(Keys.E))
        {
            equipment.Visible = !equipment.Visible;
            if (equipment.Visible) { CenterWindow(equipment, game); PushWindow(equipment); }
        }
        if (keyboard.IsKeyDown(Keys.P) && PrevKeyboard.IsKeyUp(Keys.P))
        {
            status.Visible = !status.Visible;
            if (status.Visible) PushWindow(status);
        }
    }

    internal void HandleIconClick(MouseState mouse, bool mouseOverAnyWindow, GameMain game,
        InventoryWindow inventory, StatusWindow status, SkillsWindow skills,
        EquipmentWindow equipment, SocialWindow social, QuestLogWindow questLog,
        SettingsWindow settings, Rectangle[] iconRects)
    {
        if (iconRects.Length < 7 ||
            mouse.LeftButton != ButtonState.Pressed || PrevMouse.LeftButton != ButtonState.Released)
            return;

        for (int i = 0; i < 7; i++)
        {
            if (!iconRects[i].Contains(mouse.X, mouse.Y)) continue;
            switch (i)
            {
                case 0:
                    status.Visible = !status.Visible;
                    if (status.Visible) PushWindow(status);
                    break;
                case 1:
                    inventory.Visible = !inventory.Visible;
                    if (inventory.Visible) { PositionInventoryRight(inventory, game); PushWindow(inventory); }
                    break;
                case 2:
                    skills.Visible = !skills.Visible;
                    if (skills.Visible) { CenterWindow(skills, game); PushWindow(skills); }
                    break;
                case 3:
                    equipment.Visible = !equipment.Visible;
                    if (equipment.Visible) { CenterWindow(equipment, game); PushWindow(equipment); }
                    break;
                case 4:
                    if (social.Visible) social.Visible = false;
                    else { social.Open(); PushWindow(social); }
                    break;
                case 5:
                    questLog.Visible = !questLog.Visible;
                    if (questLog.Visible) { CenterWindow(questLog, game); PushWindow(questLog); }
                    break;
                case 6:
                    settings.Visible = !settings.Visible;
                    if (settings.Visible) { CenterWindow(settings, game); PushWindow(settings); }
                    break;
            }
            return;
        }
    }

    internal bool HandlePartyButtons(MouseState mouse, bool mouseOverAnyWindow, GameMain game,
        Rectangle invitePartyRect, Rectangle tradePlayerRect,
        Rectangle partyLeaveRect, Rectangle partyDisbandRect)
    {
        bool partyClick = mouse.LeftButton == ButtonState.Pressed && PrevMouse.LeftButton == ButtonState.Released;
        if (mouseOverAnyWindow || !partyClick) return false;

        if (invitePartyRect.Contains(mouse.X, mouse.Y))
        {
            var sel = Map.GetSelectedEntity();
            if (sel != null && sel.Type == "player")
            {
                Logger.Action($"Приглашение в группу: {sel.Name}");
                _ = game.Client.SendAsync("party_invite", new { TargetName = sel.Name });
            }
            return true;
        }
        if (tradePlayerRect.Contains(mouse.X, mouse.Y))
        {
            var sel = Map.GetSelectedEntity();
            if (sel != null && sel.Type == "player")
            {
                int dist = Math.Abs(sel.X - Map.GetPlayerX()) + Math.Abs(sel.Y - Map.GetPlayerY());
                if (dist <= 1)
                {
                    Logger.Action($"Запрос обмена: {sel.Name}");
                    _ = game.Client.SendAsync("trade_request", new { TargetName = sel.Name });
                }
                else
                {
                    Logger.Action($"Подхожу к {sel.Name} для обмена...");
                    PendingTradeTarget = sel.Name;
                }
            }
            return true;
        }
        if (partyLeaveRect.Contains(mouse.X, mouse.Y))
        {
            Logger.Action("Покинуть пати");
            _ = game.Client.SendAsync("party_leave", (object?)null);
            return true;
        }
        if (partyDisbandRect.Contains(mouse.X, mouse.Y))
        {
            Logger.Action("Распустить пати");
            _ = game.Client.SendAsync("party_leave", (object?)null);
            return true;
        }
        return false;
    }

    internal void HandleEscape(KeyboardState keyboard, SettingsWindow settings, GameMain game)
    {
        if (Chat.IsTyping || !(keyboard.IsKeyDown(Keys.Escape) && PrevKeyboard.IsKeyUp(Keys.Escape)))
            return;

        PendingSkillId = null;
        PendingSlot = -1;
        PendingSent = false;

        // Remove stale entries (closed via X button or hotkey toggle)
        WindowStack.RemoveAll(w => !w.Visible);

        if (WindowStack.Count > 0)
        {
            var top = WindowStack[^1];
            WindowStack.RemoveAt(WindowStack.Count - 1);
            top.Visible = false;
        }
        else
        {
            settings.Visible = !settings.Visible;
            if (settings.Visible)
            {
                settings.X = game.Graphics.PreferredBackBufferWidth / 2 - settings.Width / 2;
                settings.Y = game.Graphics.PreferredBackBufferHeight / 2 - settings.Height / 2;
                PushWindow(settings);
            }
        }
    }

    internal void PushWindow(Windows.GameWindow w)
    {
        WindowStack.Remove(w);
        WindowStack.Add(w);
    }

    internal void HandlePendingSkill(GameMain game)
    {
        if (PendingSkillId == null) return;

        if (Hud.InCombat && !PendingSent)
        {
            _ = game.Client.SendAsync("use_skill", new { SkillId = PendingSkillId });
            PendingSent = true;
        }
        else if (!Hud.InCombat)
        {
            PendingSent = false;
        }
    }

    internal void ActivateHotbarSlot(int idx, string item, GameMain game)
    {
        if (item.StartsWith("skill:"))
        {
            if (HotbarCooldowns.ContainsKey(idx))
                return;

            var skillName = item[6..];
            var skill = Input.GetSkillByName(skillName);
            if (skill != null)
            {
                if (PendingSkillId == skill.Id)
                {
                    PendingSkillId = null;
                    PendingSlot = -1;
                    PendingSent = false;
                    return;
                }

                PendingSkillId = skill.Id;
                PendingSlot = idx;
                PendingSent = false;

                var sel = Map.GetSelectedEntity();
                if (sel != null && sel.Type == "monster" && sel.Id != null)
                {
                    _ = game.Client.SendAsync("interact_target",
                        new { Type = "monster", X = sel.X, Y = sel.Y, MonsterId = sel.Id });
                }
            }
        }
        else
        {
            var name = item.StartsWith("item:") ? item["item:".Length..] : item;
            var invItem = Input.GetItemByName(name);
            if (invItem != null)
            {
                if (invItem.Type == "consumable" && invItem.HealAmount > 0)
                    _ = game.Client.SendAsync("use_item", new { ItemId = invItem.Id });
                else if (invItem.Type is "weapon" or "armor" or "accessory")
                    _ = game.Client.SendAsync("equip", new { ItemId = invItem.Id });
            }
        }
    }

    internal void HandleSkillDragEnd(GameMain game)
    {
        var skill = DragOverlaySkill;
        DragOverlaySkill = null;
        if (skill == null) return;

        var ms = Mouse.GetState();
        int idx = HitHotbarSlot(ms.X, ms.Y, game);
        if (idx < 0) return;

        var client = game.Client;
        var slots = Input.HotbarSlots.ToList();
        while (slots.Count < 10) slots.Add(null);
        if (slots[idx] == "skill:" + skill.Name)
            slots[idx] = null;
        else
            slots[idx] = "skill:" + skill.Name;

        Input.UpdateHotbar(slots.ToArray());
        _ = client.SendAsync("hotbar_update", new { Slots = slots });
    }

    internal void OpenQuantity(string itemName, int max, int pricePerUnit, Action<int> onConfirm, bool showPrice, QuantityDialog dialog, GameMain game)
    {
        if (max <= 1) { onConfirm(1); return; }
        dialog.IsModal = true;
        dialog.ShowPrice = showPrice;
        dialog.Setup(itemName, max, pricePerUnit);
        dialog.OnConfirm = q => onConfirm(q);
        var g = game.Graphics;
        dialog.X = (g.PreferredBackBufferWidth - dialog.Width) / 2;
        dialog.Y = (g.PreferredBackBufferHeight - dialog.Height) / 2;
        Windows.BringToFront(dialog);
    }

    internal void PositionTradeWindows(ShopWindow shop, InventoryWindow inventory, GameMain game)
    {
        var g = game.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;

        int gap = 16;
        int shopW = shop.Width;
        int invW = inventory.Width;
        int totalW = shopW + invW + gap;
        int startX = Math.Max(8, (sw - totalW) / 2);
        int y = Math.Max(50, (sh - shop.Height) / 2);

        shop.X = startX;
        shop.Y = y;
        inventory.X = startX + shopW + gap;
        inventory.Y = y;
    }

    internal void PositionInventoryRight(InventoryWindow inventory, ShopWindow shop, GameMain game)
    {
        if (shop.Visible) return;
        var g = game.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;
        inventory.X = Math.Max(8, sw - inventory.Width - 16);
        inventory.Y = Math.Max(50, (sh - inventory.Height) / 2);
    }

    internal static void CenterWindow(Windows.GameWindow w, GameMain game)
    {
        var g = game.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;
        w.X = Math.Max(8, (sw - w.Width) / 2);
        w.Y = Math.Max(50, (sh - w.Height) / 2);
    }

    internal static void PositionInventoryRight(InventoryWindow inventory, GameMain game)
    {
        var g = game.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;
        inventory.X = Math.Max(8, sw - inventory.Width - 16);
        inventory.Y = Math.Max(50, (sh - inventory.Height) / 2);
    }

    internal static void OpenEquipmentBesideInventory(EquipmentWindow equipment, InventoryWindow inventory, GameMain game)
    {
        if (equipment.Visible) return;
        var g = game.Graphics;
        int sh = g.PreferredBackBufferHeight;
        equipment.Y = Math.Max(50, (sh - equipment.Height) / 2);
        equipment.X = inventory.X - equipment.Width - 16;
        if (equipment.X < 8) equipment.X = 8;
        equipment.Visible = true;
    }

    internal int HitHotbarSlot(int mx, int my, GameMain game)
    {
        var g = game.Graphics;
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
}
