using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.ClientMonoGame.Input;
using LostAndDivine.ClientMonoGame.Networking;
using LostAndDivine.ClientMonoGame.Windows;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.ClientMonoGame.Screens;

public class GameScreen : IScreen
{
    private readonly MapRenderer _mapRenderer;
    private readonly MinimapRenderer _minimap = new();
    private readonly HudRenderer _hudRenderer;
    private readonly ChatRenderer _chatRenderer;
    private readonly InputManager _inputManager;
    private readonly WindowManager _windows = new();
    private readonly GameInputHandler _input;
    private readonly GameHudRenderer _hudDraw;

    // Windows
    private readonly InventoryWindow _inventoryWindow = new();
    private readonly StatusWindow _statusWindow = new();
    private readonly InspectWindow _inspectWindow = new();
    private readonly SkillsWindow _skillsWindow = new();
    private readonly EquipmentWindow _equipmentWindow = new();
    private readonly QuestLogWindow _questLogWindow = new();
    private List<QuestInfo> _activeQuests = new();
    private readonly ShopWindow _shopWindow = new();
    private readonly EnhancementWindow _enhancementWindow = new();
    private readonly LootWindow _lootWindow = new();
    private readonly QuestBoardWindow _questBoardWindow = new();
    private readonly TradeWindow _tradeWindow = new();
    private readonly QuantityDialog _quantityDialog = new();
    private readonly EntityPickDialog _entityPickDialog = new();
    private readonly SettingsWindow _settingsWindow = new();
    private readonly LogoutConfirmWindow _logoutConfirmWindow = new();
    private readonly PartyInviteWindow _partyInviteWindow = new();
    private readonly TradeRequestWindow _tradeRequestWindow = new();
    private readonly InstanceInviteWindow _instanceInviteWindow = new();
    private readonly InstanceWindow _instanceWindow = new();
    private PartyInfo? _partyInfo;
    private readonly SocialWindow _socialWindow;
    private readonly DeathWindow _deathWindow = new();
    private readonly DialogueWindow _dialogueWindow = new();
    private readonly MailWindow _mailWindow = new();
    private readonly MailAttachmentWindow _mailAttachmentWindow = new();
    private readonly WorldMapWindow _worldMapWindow = null!;
    private readonly StorageWindow _storageWindow = new();
    private readonly ChangelogWindow _changelogWindow = new();
    private readonly HashSet<string> _lootedCorpses = new();
    private int _lastPartyMemberCount;
    private HashSet<Guid> _lastPartyMemberIds = new();
    private string? _tileRequestedZone;
    private DateTime _tileRequestTime;
    private GameClient _client = null!;

    // Секторный открытый мир (main): текущая зона и уже запрошенные секторы
    // (дедупликация: повторный запрос сектора не уходит, пока он не пришёл).
    private string _currentZoneId = BalanceStatic.StartZoneId;
    private readonly HashSet<(int Col, int Row)> _requestedSectors = new();
    // Слепок карты мира запрошен один раз за «сессию» (вход / перезагрузка секторов).
    private bool _worldMapPreloaded;

    // Панорамирование камеры зажатием ЛКМ по карте: перетаскивание двигает камеру,
    // клик без перетаскивания — обычный клик по карте (движение/выбор цели).
    private const int PanDragThresholdSq = 25; // ~5px — порог «это уже перетаскивание»
    private bool _panPressed;
    private bool _panDragging;
    private int _panStartX, _panStartY;
    private int _panPrevX, _panPrevY;

    // Exposed to the wiring layer (GameScreenMediator) — internal to this assembly only.
    internal MapRenderer MapRenderer => _mapRenderer;
    internal MinimapRenderer Minimap => _minimap;
    internal HudRenderer HudRenderer => _hudRenderer;
    internal ChatRenderer ChatRenderer => _chatRenderer;
    internal InputManager InputManager => _inputManager;
    internal GameInputHandler Input => _input;
    internal GameHudRenderer HudDraw => _hudDraw;
    internal InventoryWindow InventoryWindow => _inventoryWindow;
    internal StatusWindow StatusWindow => _statusWindow;
    internal InspectWindow InspectWindow => _inspectWindow;
    internal SkillsWindow SkillsWindow => _skillsWindow;
    internal EquipmentWindow EquipmentWindow => _equipmentWindow;
    internal QuestLogWindow QuestLogWindow => _questLogWindow;
    internal List<QuestInfo> ActiveQuests { get => _activeQuests; set => _activeQuests = value; }
    internal ShopWindow ShopWindow => _shopWindow;
    internal EnhancementWindow EnhancementWindow => _enhancementWindow;
    internal LootWindow LootWindow => _lootWindow;
    internal QuestBoardWindow QuestBoardWindow => _questBoardWindow;
    internal TradeWindow TradeWindow => _tradeWindow;
    internal QuantityDialog QuantityDialog => _quantityDialog;
    internal EntityPickDialog EntityPickDialog => _entityPickDialog;
    internal SettingsWindow SettingsWindow => _settingsWindow;
    internal LogoutConfirmWindow LogoutConfirmWindow => _logoutConfirmWindow;
    internal PartyInviteWindow PartyInviteWindow => _partyInviteWindow;
    internal TradeRequestWindow TradeRequestWindow => _tradeRequestWindow;
    internal InstanceInviteWindow InstanceInviteWindow => _instanceInviteWindow;
    internal InstanceWindow InstanceWindow => _instanceWindow;
    internal DeathWindow DeathWindow => _deathWindow;
    internal DialogueWindow DialogueWindow => _dialogueWindow;
    internal MailWindow MailWindow => _mailWindow;
    internal MailAttachmentWindow MailAttachmentWindow => _mailAttachmentWindow;
    internal WorldMapWindow WorldMapWindow => _worldMapWindow;
    internal StorageWindow StorageWindow => _storageWindow;
    internal ChangelogWindow ChangelogWindow => _changelogWindow;
    internal HashSet<string> LootedCorpses => _lootedCorpses;
    internal HashSet<Guid> LastPartyMemberIds { get => _lastPartyMemberIds; set => _lastPartyMemberIds = value; }
    internal string? TileRequestedZone { get => _tileRequestedZone; set => _tileRequestedZone = value; }
    internal HashSet<(int Col, int Row)> RequestedSectors => _requestedSectors;
    internal string CurrentZoneId { get => _currentZoneId; set => _currentZoneId = value; }
    internal bool WorldMapPreloaded { get => _worldMapPreloaded; set => _worldMapPreloaded = value; }

    private readonly GameScreenMediator _mediator = null!;

    public GameScreen()
    {
        _client = GameMain.Instance!.Client;

        _mapRenderer = new MapRenderer();
        _hudRenderer = new HudRenderer();
        _chatRenderer = new ChatRenderer();
        _inputManager = new InputManager();
        _hudRenderer.SetInputManager(_inputManager);
        _input = new GameInputHandler(_inputManager, _mapRenderer, _hudRenderer, _chatRenderer, _windows);
        _hudDraw = new GameHudRenderer(_hudRenderer, _mapRenderer);

        _socialWindow = new SocialWindow(_client);
        _worldMapWindow = new WorldMapWindow(_client);
        _socialWindow.WhisperRequested += name =>
        {
            _chatRenderer.IsTyping = true;
            _chatRenderer.TypedText = $"/w {name} ";
        };

        _mediator = new GameScreenMediator(_client, _windows, this);
        _mediator.WireAll();
        RegisterWindows();

        if (_client.LastChangelog != null)
            _mediator.ShowChangelog(_client.LastChangelog);
    }

    private void RegisterWindows()
    {
        _windows.Add(_inventoryWindow);
        _windows.Add(_statusWindow);
        _windows.Add(_inspectWindow);
        _windows.Add(_skillsWindow);
        _windows.Add(_equipmentWindow);
        _windows.Add(_questLogWindow);
        _windows.Add(_shopWindow);
        _windows.Add(_enhancementWindow);
        _windows.Add(_lootWindow);
        _windows.Add(_questBoardWindow);
        _windows.Add(_tradeWindow);
        _windows.Add(_quantityDialog);
        _windows.Add(_entityPickDialog);
        _windows.Add(_settingsWindow);
        _windows.Add(_logoutConfirmWindow);
        _windows.Add(_partyInviteWindow);
        _windows.Add(_tradeRequestWindow);
        _windows.Add(_instanceInviteWindow);
        _windows.Add(_instanceWindow);
        _windows.Add(_socialWindow);
        _windows.Add(_deathWindow);
        _windows.Add(_dialogueWindow);
        _windows.Add(_mailWindow);
        _windows.Add(_mailAttachmentWindow);
        _windows.Add(_storageWindow);
        _windows.Add(_changelogWindow);
        _windows.Add(_worldMapWindow);
    }

    /// <summary>
    /// Self-heal тайлов: если map_update пришёл без TileData и у рендера нет валидных
    /// тайлов для текущей зоны (гонка при логине — первый map_update мог прийти до
    /// создания GameScreen), запрашиваем тайлы у сервера. Повтор не чаще раза в 3 сек.
    /// </summary>
    public void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        var client = GameMain.Instance!.Client;
        var game = GameMain.Instance!;

        SkillEffectManager.Update(dtMs: (float)gameTime.ElapsedGameTime.TotalMilliseconds);
        HazardRenderer.Update(dtMs: (float)gameTime.ElapsedGameTime.TotalMilliseconds);

        _input.HandleHotbarDrop(mouse, game);
        _input.ItemUseLocked = _storageWindow.Visible;
        bool mouseOverAnyWindowBefore = _windows.IsMouseOverVisibleWindow(mouse.X, mouse.Y);
        _worldMapWindow.SetPlayerPosition(_mapRenderer.GetPlayerX(), _mapRenderer.GetPlayerY());
        _windows.Update(gameTime, keyboard, mouse);

        bool settingsOpen = _settingsWindow.Visible;
        bool mouseOverAnyWindow = mouseOverAnyWindowBefore || _windows.IsMouseOverVisibleWindow(mouse.X, mouse.Y);

        bool mailTyping = _mailWindow.IsInputActive;
        bool shopEscConsumed = _shopWindow.Visible && _shopWindow.ConsumesEscape;
        bool filterEscConsumed = shopEscConsumed
            || (_inventoryWindow.Visible && _inventoryWindow.ConsumesEscape)
            || (_storageWindow.Visible && _storageWindow.ConsumesEscape);
        if (!_chatRenderer.IsTyping && !mailTyping && !filterEscConsumed)
        {
            bool escPressed = keyboard.IsKeyDown(Keys.Escape) && _input.PrevKeyboard.IsKeyUp(Keys.Escape);
            if (escPressed)
                _input.HandleEscape(keyboard, _settingsWindow, game);
        }

        if (settingsOpen && _settingsWindow.Visible)
        {
            _input.PrevKeyboard = keyboard; _input.PrevMouse = mouse; return;
        }

        _input.HandlePendingTrade(game);
        _input.HandleHotbarClick(mouse, mouseOverAnyWindow, game);

        // Chat
        {
            int hotbarW2 = (int)(game.Graphics.PreferredBackBufferWidth * 0.35f);
            int hotbarLeft2 = (game.Graphics.PreferredBackBufferWidth - hotbarW2) / 2;
            int chatX2 = 8;
            int chatW2 = hotbarLeft2 - chatX2 - 8;
            int chatH2 = 180;
            int chatY2 = game.Graphics.PreferredBackBufferHeight - chatH2 - 8;
            bool chatPressed = mouse.LeftButton == ButtonState.Pressed && _input.PrevMouse.LeftButton == ButtonState.Released;
            bool chatReleased = mouse.LeftButton == ButtonState.Released && _input.PrevMouse.LeftButton == ButtonState.Pressed;

            if (_chatRenderer.HandleScrollbar(mouse.X, mouse.Y, chatPressed, chatReleased))
                mouseOverAnyWindow = true;

            if (!mouseOverAnyWindow)
            {
                var chatRect = new Rectangle(chatX2, chatY2, chatW2, chatH2);
                bool chatHandled = _chatRenderer.HandleClick(mouse.X, mouse.Y, chatX2, chatY2, chatW2, chatH2, chatPressed);
                if (chatHandled) mouseOverAnyWindow = true;

                if (chatRect.Contains(mouse.X, mouse.Y))
                {
                    mouseOverAnyWindow = true;
                    int scroll = mouse.ScrollWheelValue - _input.PrevMouse.ScrollWheelValue;
                    if (scroll != 0) _chatRenderer.HandleScroll(scroll > 0 ? -3 : 3, chatH2 - 54);
                }
            }
        }

        _input.HandlePendingSkill(game);
        _input.HandleChatInput(keyboard, game);
        if (!_chatRenderer.IsTyping && !mailTyping)
            _input.HandleWindowToggles(keyboard, game,
                _inventoryWindow, _statusWindow, _skillsWindow, _equipmentWindow,
                _questLogWindow, _worldMapWindow);
        if (!_chatRenderer.IsTyping && !mailTyping)
            _inputManager.HandleHotbarKeys(keyboard, _input.PrevKeyboard);

        // Icon clicks
        bool clickedIcon = false;
        if (_hudDraw.IconRects.Length >= 6 &&
            !mouseOverAnyWindow &&
            mouse.LeftButton == ButtonState.Pressed && _input.PrevMouse.LeftButton == ButtonState.Released)
        {
            foreach (var r in _hudDraw.IconRects)
            {
                if (r.Contains(mouse.X, mouse.Y)) { clickedIcon = true; break; }
            }
            _input.HandleIconClick(mouse, mouseOverAnyWindow, game,
                _inventoryWindow, _statusWindow, _skillsWindow, _equipmentWindow,
                _socialWindow, _questLogWindow, _settingsWindow, _mailWindow, _worldMapWindow, _hudDraw.IconRects);
        }
        mouseOverAnyWindow |= clickedIcon;

        // Party buttons
        bool partyHandled = _input.HandlePartyButtons(mouse, mouseOverAnyWindow, game,
            _hudDraw.InvitePartyRect, _hudDraw.TradePlayerRect, _hudDraw.InspectPlayerRect,
            _hudDraw.PartyLeaveRect, _hudDraw.PartyDisbandRect);
        mouseOverAnyWindow |= partyHandled;

        bool overHotbar = _input.HitHotbarSlot(mouse.X, mouse.Y, game) >= 0;
        bool overIconBar = _hudDraw.IconRects.Length > 0 && _hudDraw.IconRects.Any(r => r.Contains(mouse.X, mouse.Y));

        var mmRect = _minimap.GetPanelRect(game.Graphics.PreferredBackBufferWidth);
        bool overMinimap = mmRect.Contains(mouse.X, mouse.Y);

        bool overLeaveBtn = _hudDraw.InstanceLeaveRect.Contains(mouse.X, mouse.Y);
        if (!mouseOverAnyWindow && overLeaveBtn && _input.PrevMouse.LeftButton == ButtonState.Released && mouse.LeftButton == ButtonState.Pressed)
            _ = client.SendAsync("leave_instance", new { });

        bool mapArea = !mouseOverAnyWindow && !overHotbar && !overMinimap;
        if (mapArea)
        {
            int scroll = mouse.ScrollWheelValue - _input.PrevMouse.ScrollWheelValue;
            if (scroll != 0) _mapRenderer.ChangeZoom(scroll > 0 ? 0.15f : -0.15f);
        }

        // ЛКМ по карте: зажатие + перетаскивание двигает камеру (панорама, предел —
        // радиус от игрока), клик без перетаскивания — обычный клик по карте.
        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        bool leftPressed = leftDown && _input.PrevMouse.LeftButton == ButtonState.Released;
        bool leftReleased = !leftDown && _input.PrevMouse.LeftButton == ButtonState.Pressed;

        if (mapArea && leftPressed)
        {
            _panPressed = true;
            _panDragging = false;
            _panStartX = mouse.X; _panStartY = mouse.Y;
            _panPrevX = mouse.X; _panPrevY = mouse.Y;
        }

        if (_panPressed && leftDown)
        {
            if (!mapArea)
            {
                // Курсор ушёл с карты (окно/панель) — отменяем панораму и клик.
                if (_panDragging) _mapRenderer.EndPan();
                _panPressed = false;
                _panDragging = false;
            }
            else
            {
                int dx = mouse.X - _panStartX;
                int dy = mouse.Y - _panStartY;
                if (!_panDragging && dx * dx + dy * dy > PanDragThresholdSq)
                {
                    _panDragging = true;
                    _mapRenderer.BeginPan();
                }
                if (_panDragging)
                    _mapRenderer.PanByScreenDelta(mouse.X - _panPrevX, mouse.Y - _panPrevY);
                _panPrevX = mouse.X; _panPrevY = mouse.Y;
            }
        }
        else if (_panPressed && leftReleased)
        {
            if (_panDragging)
                _mapRenderer.EndPan();
            else if (mapArea)
                _inputManager.HandleMapClickAt(mouse.X, mouse.Y, _mapRenderer); // обычный клик
            _panPressed = false;
            _panDragging = false;
        }

        if (mapArea)
            _inputManager.HandleMapRightClick(mouse, _input.PrevMouse, _mapRenderer);

        // Compute cursor type for current frame
        {
            int w2 = game.Graphics.PreferredBackBufferWidth;
            int h2 = game.Graphics.PreferredBackBufferHeight;
            int topH2 = 0;
            bool overMap = !mouseOverAnyWindow && !overHotbar && !overIconBar && !overMinimap && mouse.Y >= topH2;
            string ct = "main";
            if (_panDragging || _windows.AnyDragging || _input.DragOverlayItem != null || _input.DragOverlaySkill != null)
                ct = "take";
            else if (overMap)
            {
                int areaW = w2;
                int areaH = h2 - topH2;
                ct = _mapRenderer.GetCursorType(mouse.X, mouse.Y, areaW, areaH);
            }
            else
            {
                _mapRenderer.ClearHoverTile();
            }
            CurrentCursorType = ct;
        }

        _input.PrevKeyboard = keyboard;
        _input.PrevMouse = mouse;
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        int w = GameMain.Instance!.Graphics.PreferredBackBufferWidth;
        int h = GameMain.Instance!.Graphics.PreferredBackBufferHeight;
        int topH = 0;

        _mapRenderer.Draw(spriteBatch, 0, topH, w, h - topH);
        if (_mapRenderer.IsMapLoaded)
        {
        _mapRenderer.DrawSkillEffects(spriteBatch, 0, topH, w, h - topH);
        _minimap.SetViewBounds(_mapRenderer.GetViewBounds());
        // Координаты игрока берём из авторитетной клетки (map.Players), а не из центра камеры:
        // камера плавно догоняет игрока и показывает отставание на шаг.
        _minimap.Draw(spriteBatch, _minimap.GetPanelRect(w), _mapRenderer.GetPlayerX(), _mapRenderer.GetPlayerY());
        _hudDraw.DrawInstanceLeaveButton(spriteBatch, w);
        _hudDraw.DrawQuestTracker(spriteBatch, w, _activeQuests);
        float panelH = _hudRenderer.DrawPlayerStatusPanel(spriteBatch, 8, topH + 8);
        float debuffH = _hudRenderer.DrawPlayerDebuffs(spriteBatch, 8, topH + 8 + panelH + 4, w - 16);
        _hudRenderer.SetSelectedEntity(_mapRenderer.GetSelectedEntity());
        _hudRenderer.DrawTargetBar(spriteBatch, w);
        _hudRenderer.DrawTargetDebuffs(spriteBatch, w, 64 + 18 + 4);
        _hudRenderer.DrawZoneIndicator(spriteBatch, w);
        _hudDraw.DrawTargetButtons(spriteBatch, w, GameMain.Instance!);
        int partyY = topH + 8 + (int)panelH + 4 + (int)debuffH + 4;
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
        _hudRenderer.DrawHotbarTooltip(spriteBatch);

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
        }

        // Settings overlay
        if (_settingsWindow.Visible)
            spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(0, 0, w, h), new Color(0, 0, 0, 140));

        _windows.Draw(gameTime, spriteBatch);

        // Drag overlay
        int dragHitIdx = _input.HitHotbarSlot(Mouse.GetState().X, Mouse.GetState().Y, GameMain.Instance!);
        _hudDraw.DrawDragOverlay(spriteBatch, _input.DragOverlayItem, _input.DragOverlaySkill, dragHitIdx, GameMain.Instance!);

        spriteBatch.End();
    }

    public static string? CurrentCursorType { get; set; }

    public void Dispose()
    {
        _client.UnsubscribeAll(this);
        _socialWindow.Unsubscribe();
    }

    private static string DebuffDisplayName(string type) => type switch
    {
        "Returning"       => "Возвращение",
        "Stun"            => "Оглушение",
        "Root"            => "Обездвижен",
        "Slow"            => "Замедление",
        "Dot"             => "Отравление",
        "SuppressingFire" => "Подавл. огонь",
        "ArmorPenetration"=> "Пробитие брони",
        "DamageBonus"     => "Усиление урона",
        "DamageReduction" => "Ослабление",
        "AccuracyReduction"=> "Дезориентация",
        "AttackSpeedBonus"=> "Проворность",
        "CleaveReady"     => "Рассечение",
        "DualWieldBonus"  => "Двойное оружие",
        _                 => type
    };
}
