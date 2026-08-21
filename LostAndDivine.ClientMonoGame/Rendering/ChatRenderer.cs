using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.Shared.Network;
using LostAndDivine.ClientMonoGame.Rendering;

namespace LostAndDivine.ClientMonoGame.Rendering;

public class ChatRenderer
{
    private static readonly Color AdminNameColor = new Color(0, 255, 200);

    private readonly List<(ChatChannel channel, string name, string text, DateTime time, bool isAdmin)> _messages = new();
    private const int MaxMessages = 400;
    private int _scrollOffset;
    private bool _scrollDragging;

    // Геометрия скроллбара, сохранённая при отрисовке, для обработки мыши
    private int _sbVisible, _sbMaxScroll, _sbTrackY, _sbTrackH, _sbBarX, _sbCount;

    public bool IsTyping { get; set; }
    public string TypedText { get; set; } = "";

    // История отправленных сообщений/команд (навигация стрелками вверх/вниз).
    private readonly List<string> _history = new();
    private int _historyIndex = -1;      // -1 = поле ввода без навигации
    private string _historyDraft = "";   // черновик, сохранённый при уходе в историю

    // VK, нажатые в предыдущем кадре (для отслеживания "только что нажатых")
    private HashSet<uint> _prevDownVks = new();

    public enum Layout { En, Ru }
    public Layout CurrentLayout { get; set; } = Layout.En;
    public bool IsLangMenuOpen { get; set; }

    // Фильтр-вкладка: null = "Все каналы"
    public ChatChannel? ActiveTab { get; set; } = null;

    private readonly Dictionary<ChatChannel, int> _unread = new();
    private readonly ChatChannel[] _tabs =
    {
        ChatChannel.World, ChatChannel.Local, ChatChannel.Trade,
        ChatChannel.Party, ChatChannel.Guild, ChatChannel.Whisper,
        ChatChannel.System, ChatChannel.Combat
    };

    private static readonly Dictionary<ChatChannel, Color> ChannelColor = new()
    {
        { ChatChannel.System, new Color(255, 220, 80) },
        { ChatChannel.World, Color.White },
        { ChatChannel.Local, new Color(230, 220, 130) },
        { ChatChannel.Trade, new Color(150, 220, 150) },
        { ChatChannel.Party, new Color(120, 180, 255) },
        { ChatChannel.Guild, new Color(190, 140, 240) },
        { ChatChannel.Whisper, new Color(240, 150, 210) },
        { ChatChannel.Combat, new Color(255, 150, 90) }
    };

    private static readonly Dictionary<ChatChannel, string> ChannelLabel = new()
    {
        { ChatChannel.System, "Сис" },
        { ChatChannel.World, "Общ" },
        { ChatChannel.Local, "Лок" },
        { ChatChannel.Trade, "Торг" },
        { ChatChannel.Party, "Груп" },
        { ChatChannel.Guild, "Гил" },
        { ChatChannel.Whisper, "Личн" },
        { ChatChannel.Combat, "Бой" }
    };

    public void AddMessage(ChatChannel channel, string name, string text, bool isAdmin = false)
    {
        bool atBottom = _scrollOffset == 0;
        _messages.Add((channel, name, text, DateTime.UtcNow, isAdmin));
        if (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);

        if (ActiveTab != channel)
            _unread[channel] = (_unread.TryGetValue(channel, out var n) ? n : 0) + 1;

        if (atBottom) _scrollOffset = 0;
    }

    public void HandleScroll(int scrollDelta, float msgAreaH)
    {
        int lineH = 14;
        int totalLines = CountVisibleLines();
        int visibleLines = Math.Max(1, (int)(msgAreaH / lineH));
        int maxScroll = Math.Max(0, totalLines - visibleLines);
        _scrollOffset = Math.Clamp(_scrollOffset - scrollDelta, 0, maxScroll);
    }

    private int CountVisibleLines()
    {
        var fontSmall = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (fontSmall == null) return 0;
        float maxTextW = _lastW - 16;
        int count = 0;
        int startIdx = Math.Max(0, _messages.Count - MaxMessages);
        for (int i = startIdx; i < _messages.Count; i++)
        {
            var msg = _messages[i];
            if (ActiveTab != null && msg.channel != ActiveTab) continue;
            string tag = ChannelLabel.TryGetValue(msg.channel, out var lbl) ? $"[{lbl}] " : "";
            string full = $"{tag}{msg.name}: {msg.text}";
            var words = full.Split(' ');
            var cur = "";
            foreach (var word in words)
            {
                string test = cur.Length == 0 ? word : cur + " " + word;
                if (fontSmall.MeasureString(test).X > maxTextW && cur.Length > 0)
                {
                    count++;
                    cur = word;
                }
                else cur = test;
            }
            if (cur.Length > 0) count++;
        }
        return count;
    }

    private float _lastW;

    // Обратная совместимость: сообщения без канала -> Система
    public void AddMessage(string name, string text)
        => AddMessage(ChatChannel.System, name, text, false);

    public void HandleInput(KeyboardState keyboard, KeyboardState prevKeyboard)
    {
        // Синхронизируем индикатор с раскладкой АКТИВНОГО окна (ту, что видит пользователь)
        bool russian = KeyboardLayoutHelper.IsRussianForeground();
        CurrentLayout = russian ? Layout.Ru : Layout.En;

        // При начале ввода подставляем префикс активной вкладки (если ещё ничего не набрано)
        if (TypedText.Length == 0 && !string.IsNullOrEmpty(CurrentPrefix))
            TypedText = CurrentPrefix;

        // Навигация по истории отправленных сообщений (стрелки вверх/вниз)
        if (keyboard.IsKeyDown(Keys.Up) && prevKeyboard.IsKeyUp(Keys.Up))
        {
            if (_history.Count > 0)
            {
                if (_historyIndex == -1)
                    _historyDraft = TypedText;
                if (_historyIndex < _history.Count - 1)
                    _historyIndex++;
                TypedText = _history[_history.Count - 1 - _historyIndex];
            }
        }
        if (keyboard.IsKeyDown(Keys.Down) && prevKeyboard.IsKeyUp(Keys.Down))
        {
            if (_historyIndex != -1)
            {
                _historyIndex--;
                if (_historyIndex < 0)
                {
                    _historyIndex = -1;
                    TypedText = _historyDraft;
                }
                else
                {
                    TypedText = _history[_history.Count - 1 - _historyIndex];
                }
            }
        }

        // Ввод символов: опрашиваем нажатые VK напрямую у ОС (GetAsyncKeyState)
        // и переводим каждый в символ по детерминированной таблице VK->char.
        // Это обходит баг MonoGame DesktopGL (GetPressedKeys даёт Keys.None для
        // OEM-клавиш на русской раскладке).
        bool shiftDown = KeyboardLayoutHelper.IsShiftDown();
        var nowDown = new HashSet<uint>(KeyboardLayoutHelper.GetPressedVks());
        foreach (var vk in nowDown)
        {
            if (_prevDownVks.Contains(vk)) continue; // только что нажатая клавиша

            // Пропускаем чисто модификаторы/управляющие клавиши
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14 ||
                vk == 0x09 /*Tab*/ || vk == 0x0D /*Enter*/ ||
                vk == 0x1B /*Escape*/ || vk == 0x08 /*Back*/ ||
                vk == 0x26 /*Up*/ || vk == 0x28 /*Down*/)
                continue;

            if (KeyCharMap.TryGetCharByVk(vk, russian, shiftDown, out char ch))
                TypedText += ch;
        }
        _prevDownVks = nowDown;

        if (keyboard.IsKeyDown(Keys.Back) && prevKeyboard.IsKeyUp(Keys.Back) && TypedText.Length > 0)
            TypedText = TypedText[..^1];
        if (keyboard.IsKeyDown(Keys.Escape) && prevKeyboard.IsKeyUp(Keys.Escape))
        {
            IsTyping = false;
            TypedText = "";
            IsLangMenuOpen = false;
        }
    }

    /// <summary>Сохраняет отправленное сообщение/команду в историю для стрелок вверх/вниз.</summary>
    public void AddToHistory(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return;
        if (_history.Count == 0 || !string.Equals(_history[^1], trimmed, StringComparison.OrdinalIgnoreCase))
            _history.Add(trimmed);
        _historyIndex = -1;
        _historyDraft = "";
    }

    private Rectangle GetTabRect(float x, float y, int index)
    {
        int tabW = 38, tabH = 18, gap = 2;
        return new Rectangle((int)(x + 4 + index * (tabW + gap)), (int)(y + 2), tabW, tabH);
    }

    private int TabCount => _tabs.Length + 1; // + "Все"

    // Префикс, который подставляется в поле ввода при выборе вкладки
    private static readonly Dictionary<ChatChannel, string> TabPrefix = new()
    {
        { ChatChannel.World, "/world " },
        { ChatChannel.Local, "/local " },
        { ChatChannel.Trade, "/trade " },
        { ChatChannel.Party, "/p " },
        { ChatChannel.Guild, "/g " },
        { ChatChannel.Whisper, "/w " },
        { ChatChannel.System, "" },
        { ChatChannel.Combat, "" }
    };

    // Список всех префиксов для очистки старого при смене вкладки
    private static readonly string[] AllPrefixes = { "/world ", "/local ", "/say ", "/s ", "/trade ", "/p ", "/party ", "/g ", "/guild ", "/w ", "/whisper ", "/tell " };

    public string CurrentPrefix => ActiveTab.HasValue && TabPrefix.TryGetValue(ActiveTab.Value, out var p) ? p : "";

    private void ApplyTabPrefix()
    {
        string prefix = CurrentPrefix;
        // Убираем старый префикс, если он был
        string trimmed = TypedText;
        foreach (var old in AllPrefixes)
        {
            if (trimmed.StartsWith(old, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(old.Length);
                break;
            }
        }
        TypedText = prefix + trimmed;
    }


    // Обработка перетаскивания скроллбара чата левой кнопкой мыши.
    // Возвращает true, если клик/перетаскивание захвачено баром.
    public bool HandleScrollbar(int mx, int my, bool pressed, bool released)
    {
        if (_sbMaxScroll <= 0)
        {
            _scrollDragging = false;
            return false;
        }

        int thumbH = ScrollBar.ComputeThumbHeight(_sbTrackH, _sbVisible, _sbCount);
        int thumbY = ScrollBar.ComputeThumbY(_sbTrackY, _sbTrackH, thumbH, _sbMaxScroll - _scrollOffset, _sbVisible, _sbCount);
        var trackRect = new Rectangle(_sbBarX, _sbTrackY, ScrollBar.DefaultWidth, _sbTrackH);
        var thumbRect = new Rectangle(_sbBarX, thumbY, ScrollBar.DefaultWidth, thumbH);

        bool consumed = false;
        if (pressed && trackRect.Contains(mx, my))
        {
            consumed = true;
            if (thumbRect.Contains(mx, my))
                _scrollDragging = true;
            else
                _scrollOffset = _sbMaxScroll - ScrollBar.ScrollFromMouse(_sbTrackY, _sbTrackH, thumbH, _sbVisible, _sbCount, my);
        }

        if (_scrollDragging && released)
            _scrollDragging = false;
        if (_scrollDragging)
            _scrollOffset = _sbMaxScroll - ScrollBar.ScrollFromMouse(_sbTrackY, _sbTrackH, thumbH, _sbVisible, _sbCount, my);

        _scrollOffset = Math.Clamp(_scrollOffset, 0, _sbMaxScroll);
        return consumed || _scrollDragging;
    }

    // Возвращает true, если клик обработан чатом
    public bool HandleClick(int mx, int my, float x, float y, float w, float h, bool pressed)
    {
        // Вкладки сверху
        var allRect = GetTabRect(x, y, 0);
        if (pressed && allRect.Contains(mx, my)) { ActiveTab = null; _scrollOffset = 0; _unread.Clear(); ApplyTabPrefix(); return true; }
        for (int i = 0; i < _tabs.Length; i++)
        {
            var r = GetTabRect(x, y, i + 1);
            if (pressed && r.Contains(mx, my))
            {
                ActiveTab = _tabs[i];
                _scrollOffset = 0;
                _unread[_tabs[i]] = 0;
                ApplyTabPrefix();
                return true;
            }
        }

        float inputY = y + h - 26;
        int indW = 34, indH = 22;
        float indX = x + w - 8 - indW;
        var indRect = new Rectangle((int)indX, (int)inputY, indW, indH);

        int itemH = 22;
        var ruRect = new Rectangle((int)indX, (int)inputY - itemH * 2, indW, itemH);
        var enRect = new Rectangle((int)indX, (int)inputY - itemH, indW, itemH);

        if (IsLangMenuOpen)
        {
            if (pressed)
            {
                if (ruRect.Contains(mx, my)) { KeyboardLayoutHelper.SetRussian(true); IsLangMenuOpen = false; return true; }
                if (enRect.Contains(mx, my)) { KeyboardLayoutHelper.SetRussian(false); IsLangMenuOpen = false; return true; }
                if (indRect.Contains(mx, my)) { IsLangMenuOpen = false; return true; }
            }
            return true;
        }

        if (pressed && indRect.Contains(mx, my))
        {
            IsLangMenuOpen = true;
            return true;
        }
        return false;
    }

    public void Draw(SpriteBatch sb, float x, float y, float w, float h)
    {
        var font = SpriteCache.Font;
        var fontSmall = SpriteCache.FontSmall ?? font;
        if (font == null) return;
        _lastW = w;

        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(26, 26, 34, 150));

        // Вкладки
        var allRect = GetTabRect(x, y, 0);
        DrawTab(sb, fontSmall, allRect, "Все", ActiveTab == null,
            (_unread.Values.Sum()) > 0 && ActiveTab != null);
        for (int i = 0; i < _tabs.Length; i++)
        {
            var r = GetTabRect(x, y, i + 1);
            var ch = _tabs[i];
            bool active = ActiveTab == ch;
            bool hasUnread = (_unread.TryGetValue(ch, out var n) ? n : 0) > 0 && !active;
            DrawTab(sb, fontSmall, r, ChannelLabel[ch], active, hasUnread);
        }

        float msgTop = y + 24;
        float msgBottom = y + h - 30;
        float msgH = msgBottom - msgTop;

        // Сообщения (отфильтрованные)
        int lineH = 14;
        float maxTextW = w - 16;
        var wrapped = new List<(string text, Color color)>();
        int startIdx = Math.Max(0, _messages.Count - MaxMessages);
        for (int i = startIdx; i < _messages.Count; i++)
        {
            var msg = _messages[i];
            if (ActiveTab != null && msg.channel != ActiveTab) continue;

            Color chColor = ChannelColor.TryGetValue(msg.channel, out var cc) ? cc : Color.White;
            string tag = ChannelLabel.TryGetValue(msg.channel, out var lbl) ? $"[{lbl}] " : "";
            string prefix = $"{tag}{msg.name}: ";
            Color nameColor = msg.isAdmin ? AdminNameColor : chColor;

            string full = prefix + msg.text;
            var words = full.Split(' ');
            var cur = "";
            foreach (var word in words)
            {
                string test = cur.Length == 0 ? word : cur + " " + word;
                if (fontSmall.MeasureString(test).X > maxTextW && cur.Length > 0)
                {
                    wrapped.Add((cur, nameColor));
                    cur = word;
                }
                else
                {
                    cur = test;
                }
            }
            if (cur.Length > 0) wrapped.Add((cur, nameColor));
        }

        int visibleLines = Math.Max(1, (int)(msgH / lineH));
        int from = Math.Max(0, wrapped.Count - visibleLines - _scrollOffset);
        int to = Math.Min(wrapped.Count, from + visibleLines);
        float msgY = msgTop;
        for (int i = from; i < to; i++)
        {
            sb.DrawString(fontSmall, wrapped[i].text, new Vector2(x + 8, msgY), wrapped[i].color);
            msgY += lineH;
        }

        int maxScroll = Math.Max(0, wrapped.Count - visibleLines);
        _sbVisible = visibleLines;
        _sbMaxScroll = maxScroll;
        _sbTrackY = (int)msgTop;
        _sbTrackH = (int)msgH;
        _sbCount = wrapped.Count;
        int barX = (int)(x + w - 5) - (ScrollBar.DefaultWidth - 3);
        _sbBarX = barX;
        if (maxScroll > 0)
        {
            // В чате прокрутка инвертирована (0 = самые новые снизу), поэтому передаём maxScroll - offset
            ScrollBar.Draw(sb, barX, (int)msgTop, (int)msgH, maxScroll - _scrollOffset, visibleLines, wrapped.Count, ScrollBar.DefaultWidth);
        }

        // Поле ввода
        float inputY = y + h - 26;
        int indW = 34, indH = 22;
        float indX = x + w - 8 - indW;
        float textMaxX = indX - 6;
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)(x + 4), (int)inputY, (int)(w - 8), 22), new Color(40, 40, 50, 180));

        var msNow = Microsoft.Xna.Framework.Input.Mouse.GetState();
        bool hover = new Rectangle((int)indX, (int)inputY, indW, indH).Contains(msNow.X, msNow.Y);
        Color indColor = IsLangMenuOpen ? new Color(80, 110, 160)
            : (hover ? new Color(90, 96, 120) : new Color(60, 64, 80));
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)indX, (int)inputY, indW, indH), indColor);
        sb.DrawString(fontSmall, CurrentLayout == Layout.Ru ? "RU" : "EN",
            new Vector2(indX + (indW - fontSmall.MeasureString(CurrentLayout == Layout.Ru ? "RU" : "EN").X) / 2, inputY + 3),
            Color.White);

        if (IsLangMenuOpen)
        {
            int itemH = 22;
            var ruRect = new Rectangle((int)indX, (int)inputY - itemH * 2, indW, itemH);
            var enRect = new Rectangle((int)indX, (int)inputY - itemH, indW, itemH);
            sb.Draw(SpriteCache.Pixel, ruRect, CurrentLayout == Layout.Ru ? new Color(80, 110, 160) : new Color(50, 54, 70));
            sb.Draw(SpriteCache.Pixel, enRect, CurrentLayout == Layout.En ? new Color(80, 110, 160) : new Color(50, 54, 70));
            sb.DrawString(fontSmall, "RU", new Vector2(indX + (indW - fontSmall.MeasureString("RU").X) / 2, ruRect.Y + 3), Color.White);
            sb.DrawString(fontSmall, "EN", new Vector2(indX + (indW - fontSmall.MeasureString("EN").X) / 2, enRect.Y + 3), Color.White);
        }

        if (IsTyping)
        {
            string displayText = TypedText + "_";
            var measured = fontSmall.MeasureString(displayText);
            float drawX = x + 8;
            if (drawX + measured.X > textMaxX) drawX = textMaxX - measured.X;
            if (drawX < x + 8) drawX = x + 8;
            sb.DrawString(fontSmall, displayText, new Vector2(drawX, inputY + 3), Color.White);
        }
        else
        {
            string hint;
            if (ActiveTab == null)
                hint = "Enter — ввод (Локальный). Вкладки задают канал.";
            else if (ActiveTab == ChatChannel.Whisper)
                hint = "[Личн] Enter — ввод. Допишите ник: /w <ник> текст";
            else
                hint = $"[{ChannelLabel[ActiveTab.Value]}] Enter — ввод. Префикс: {CurrentPrefix.Trim()}";
            sb.DrawString(fontSmall, hint, new Vector2(x + 8, inputY + 3), new Color(120, 120, 130));
        }
    }

    private static void DrawTab(SpriteBatch sb, SpriteFont font, Rectangle rect, string label, bool active, bool unread)
    {
        Color bg = active ? new Color(70, 90, 130) : (unread ? new Color(90, 70, 40) : new Color(45, 48, 60));
        sb.Draw(SpriteCache.Pixel, rect, bg);
        sb.DrawString(font, label,
            new Vector2(rect.X + (rect.Width - font.MeasureString(label).X) / 2, rect.Y + 2),
            unread && !active ? new Color(255, 210, 120) : Color.White);
    }
}
