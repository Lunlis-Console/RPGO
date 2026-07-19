using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;

namespace RPGGame.ClientMonoGame.Windows;

public sealed class QuestBoardWindow : GameWindow
{
    private List<QuestInfo> _available = new();
    private List<QuestInfo> _active = new();
    private int _scrollOffset;
    private MouseState _prevMouse;
    private int _tab; // 0 = Доступные, 1 = Активные, 2 = Сдать

    private static readonly string[] Tabs = { "Доступные", "Активные", "Сдать" };

    // Кнопки карточек, сохранённые при отрисовке, обрабатываются в Update
    private readonly List<(Rectangle Rect, string QuestId, bool Submit, bool Abandon)> _cardButtons = new();

    private const int CardPadX = 10;
    private const int CardPadY = 8;
    private const int CardSpacing = 6;
    private const int LineHeight = 14;
    private const int SectionGap = 20;
    private const int BarWidth = 4;

    private static readonly Color BgCard = new(38, 40, 52);
    private static readonly Color AccentBlue = new(0, 120, 215);
    private static readonly Color AccentGreen = new(0, 180, 90);
    private static readonly Color TextWhite = Color.White;
    private static readonly Color TextMuted = new(150, 150, 160);
    private static readonly Color TextDesc = new(200, 200, 210);
    private static readonly Color TextProgress = new(150, 200, 255);
    private static readonly Color HeaderGold = new(220, 200, 120);
    private static readonly Color SectionAvail = new(150, 200, 255);
    private static readonly Color SectionActive = new(150, 220, 150);
    private static readonly Color BtnTake = new(0, 90, 170);
    private static readonly Color BtnSubmit = new(0, 140, 70);
    private static readonly Color BtnDisabled = new(60, 60, 70);

    public Action<string>? TakeQuest { get; set; }
    public Action<string>? CompleteQuest { get; set; }
    public Action<string>? AbandonQuest { get; set; }

    public QuestBoardWindow()
    {
        Title = "Доска заданий";
        Width = 520;
        Height = 520;
        Visible = false;
    }

    public void UpdateData(List<QuestInfo> available, List<QuestInfo> active)
    {
        _available = available ?? new List<QuestInfo>();
        _active = SortQuests(active ?? new List<QuestInfo>());
        _scrollOffset = 0;
    }

    private static List<QuestInfo> SortQuests(List<QuestInfo> quests)
    {
        // Выполненные сверху, затем готовые к завершению, затем в процессе
        int Rank(QuestInfo q)
        {
            if (q.Completed) return 0;
            bool ready = !q.Completed && q.Target > 0 && q.Current >= q.Target;
            return ready ? 1 : 2;
        }
        return quests.OrderBy(Rank).ThenBy(q => q.Title ?? "").ToList();
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        base.Update(gameTime, keyboard, mouse);
        if (!Visible) return;

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

        // Скролл колесом мыши
        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (wheel != 0)
            _scrollOffset += wheel > 0 ? -30 : 30;

        if (keyboard.IsKeyDown(Keys.PageUp))
            _scrollOffset = Math.Max(0, _scrollOffset - 30);
        if (keyboard.IsKeyDown(Keys.PageDown))
            _scrollOffset += 30;

        int cx = ContentX;
        int cy = ContentY;
        int cw = ContentW;

        // Вкладки
        int tabW = (cw - (Tabs.Length - 1) * 6) / Tabs.Length;
        int tabY = cy + 24;
        int tabH = 22;
        if (clicked)
        {
            for (int i = 0; i < Tabs.Length; i++)
            {
                int tx = cx + i * (tabW + 6);
                if (mouse.X >= tx && mouse.X <= tx + tabW && mouse.Y >= tabY && mouse.Y <= tabY + tabH)
                {
                    _tab = i;
                    _scrollOffset = 0;
                    break;
                }
            }
        }

        // Кнопки карточек (сохранены при Draw)
        if (clicked)
        {
            foreach (var b in _cardButtons)
            {
                if (b.Rect.Contains(mouse.X, mouse.Y))
                {
                    if (b.Abandon) AbandonQuest?.Invoke(b.QuestId);
                    else if (b.Submit) CompleteQuest?.Invoke(b.QuestId);
                    else TakeQuest?.Invoke(b.QuestId);
                    break;
                }
            }

            // Кнопка «Закрыть» (координаты синхронны с Draw)
            int headerOff = 24;
            int tabsOff = headerOff + 22 + 6;
            int listH = ContentH - tabsOff - 32;
            int closeBtnX = ContentX + (ContentW - 100) / 2;
            int closeBtnW = 100;
            int closeBtnH = 22;
            int closeBtnYReal = ContentY + tabsOff + listH + 6;
            if (mouse.X >= closeBtnX && mouse.X <= closeBtnX + closeBtnW &&
                mouse.Y >= closeBtnYReal && mouse.Y <= closeBtnYReal + closeBtnH)
            {
                Visible = false;
            }
        }

        _cardButtons.Clear();
        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;

        base.Draw(sb, Mouse.GetState());

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int cx = ContentX;
        int cy = ContentY;
        int cw = ContentW;
        int ch = ContentH;

        string header = "ДОСКА ЗАДАНИЙ";
        var headerSize = font.MeasureString(header);
        DrawText(sb, header, cx + (cw - (int)headerSize.X) / 2, cy, HeaderGold);
        cy += 24;

        // Вкладки (только отрисовка, клики в Update)
        int tabW = (cw - (Tabs.Length - 1) * 6) / Tabs.Length;
        int tabY = cy;
        int tabH = 22;
        var mouse = Mouse.GetState();
        for (int i = 0; i < Tabs.Length; i++)
        {
            int tx = cx + i * (tabW + 6);
            bool activeTab = i == _tab;
            Color tabBg = activeTab ? new Color(0, 110, 200) : new Color(45, 48, 60);
            sb.Draw(SpriteCache.Pixel, new Rectangle(tx, tabY, tabW, tabH), tabBg);
            DrawText(sb, Tabs[i], tx + (tabW - (int)font.MeasureString(Tabs[i]).X) / 2, tabY + 4,
                activeTab ? Color.White : TextMuted);
        }
        cy += tabH + 6;

        int listH = ch - (cy - ContentY) - 32;
        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, listH), new Color(20, 22, 28));

        var activeIds = _active.Select(a => a.QuestId).ToHashSet();
        var filteredAvailable = _available.Where(q => !activeIds.Contains(q.QuestId)).ToList();
        var readyToSubmit = _active.Where(a => a.Completed).ToList();

        List<QuestInfo> listForTab = _tab switch
        {
            0 => filteredAvailable,
            1 => _active.Where(a => !a.Completed).ToList(),
            _ => readyToSubmit
        };

        int totalH = 0;
        if (listForTab.Count > 0)
            foreach (var q in listForTab) totalH += GetCardHeight(q, cw, font) + CardSpacing;
        else
            totalH += LineHeight + CardSpacing;

        int maxScroll = Math.Max(0, totalH - listH);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);

        int drawY = cy - _scrollOffset;
        var clipRect = new Rectangle(cx, cy, cw, listH);

        sb.End();
        var oldScissor = sb.GraphicsDevice.ScissorRectangle;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
        sb.GraphicsDevice.ScissorRectangle = clipRect;

        if (listForTab.Count == 0)
        {
            string empty = _tab switch
            {
                0 => "Нет доступных заданий.",
                1 => "У вас нет активных заданий.",
                _ => "Нет заданий для сдачи."
            };
            DrawText(sb, empty, cx + 8, drawY, TextMuted);
            drawY += LineHeight;
        }
        else
        {
            foreach (var q in listForTab)
            {
                int cardH = GetCardHeight(q, cw, font);
                if (drawY + cardH > cy && drawY < cy + listH)
                {
                    if (_tab == 0)
                        DrawAvailableCard(sb, q, cx, drawY, cw, cardH, font, mouse);
                    else
                        DrawActiveCard(sb, q, cx, drawY, cw, cardH, font, mouse, _tab == 2);
                }
                drawY += cardH + CardSpacing;
            }
        }

        sb.End();
        sb.GraphicsDevice.ScissorRectangle = oldScissor;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        if (totalH > listH && maxScroll > 0)
        {
            int barH = Math.Max(30, (int)((float)listH / totalH * listH));
            int barY = cy + (int)((float)_scrollOffset / maxScroll * (listH - barH));
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx + cw - 5, barY, 4, barH), new Color(100, 110, 130));
        }

        int btnY = cy + listH + 6;
        int btnW = 100;
        int btnH = 22;
        int btnX = cx + (cw - btnW) / 2;
        // Кнопка «Закрыть» — клик обрабатывается в Update
        var closeRect = new Rectangle(btnX, btnY, btnW, btnH);
        bool closeHover = closeRect.Contains(mouse.X, mouse.Y);
        sb.Draw(SpriteCache.Pixel, closeRect, closeHover ? new Color(150, 60, 60) : new Color(80, 40, 40));
        DrawText(sb, "Закрыть", btnX + (btnW - (int)font.MeasureString("Закрыть").X) / 2, btnY + (btnH - (int)font.MeasureString("Закрыть").Y) / 2, Color.White);
    }

    private int GetCardHeight(QuestInfo q, int availableWidth, SpriteFont font)
    {
        int innerW = availableWidth - CardPadX * 2 - BarWidth - 4;
        int h = CardPadY * 2;
        h += LineHeight;   // заголовок
        h += LineHeight;   // статус
        h += MeasureWrappedText(q.Description ?? "", innerW, font).H; // описание
        h += 10 + LineHeight + CardPadY; // прогресс-бар + текст
        return h;
    }

    private static (int W, int H) MeasureWrappedText(string text, int maxW, SpriteFont font)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);

        int lines = 1;
        float lineWidth = 0;
        float spaceW = font.MeasureString(" ").X;

        foreach (var word in text.Split(' '))
        {
            float wordW = font.MeasureString(word).X;
            if (lineWidth > 0 && lineWidth + spaceW + wordW > maxW)
            {
                lines++;
                lineWidth = wordW;
            }
            else
            {
                lineWidth += (lineWidth > 0 ? spaceW : 0) + wordW;
            }
        }

        return ((int)Math.Min(lineWidth, maxW), lines * (int)font.LineSpacing);
    }

    private void DrawWrappedText(SpriteBatch sb, string text, int x, int y, int maxW, Color color, SpriteFont font)
    {
        if (string.IsNullOrEmpty(text)) return;

        float spaceW = font.MeasureString(" ").X;
        float curX = x;
        int curY = y;
        int lineHeight = (int)font.LineSpacing;

        foreach (var word in text.Split(' '))
        {
            float wordW = font.MeasureString(word).X;
            if (curX > x && curX - x + spaceW + wordW > maxW)
            {
                curX = x;
                curY += lineHeight;
            }
            sb.DrawString(font, word, new Vector2(curX, curY), color);
            curX += wordW + spaceW;
        }
    }

    private void DrawAvailableCard(SpriteBatch sb, QuestInfo q, int x, int y, int w, int h, SpriteFont font, MouseState mouse)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, h), BgCard);
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, BarWidth, h), AccentBlue);

        int textX = x + BarWidth + 4;
        int textY = y + CardPadY;
        int innerW = w - CardPadX * 2 - BarWidth - 4;

        DrawText(sb, q.Title ?? "Без названия", textX, textY, TextWhite);
        textY += LineHeight;

        int descH = MeasureWrappedText(q.Description ?? "", innerW, font).H;
        DrawWrappedText(sb, q.Description ?? "", textX, textY, innerW, TextDesc, font);
        textY += descH;

        string rewards = $"Опыт: {q.XpReward}  |  Золото: {q.GoldReward}";
        DrawText(sb, rewards, textX, textY, TextProgress);

        int btnW = 80;
        int btnH = 20;
        int btnX = x + w - btnW - CardPadX;
        int btnY = y + h - btnH - CardPadY;
        Color btnBg = BtnTake;
        if (mouse.X >= btnX && mouse.X <= btnX + btnW && mouse.Y >= btnY && mouse.Y <= btnY + btnH)
            btnBg = new Color(20, 120, 210);
        sb.Draw(SpriteCache.Pixel, new Rectangle(btnX, btnY, btnW, btnH), btnBg);
        DrawText(sb, "Взять", btnX + (btnW - (int)font.MeasureString("Взять").X) / 2, btnY + (btnH - (int)font.MeasureString("Взять").Y) / 2, Color.White);
        _cardButtons.Add((new Rectangle(btnX, btnY, btnW, btnH), q.QuestId ?? "", false, false));
    }

    private void DrawActiveCard(SpriteBatch sb, QuestInfo q, int x, int y, int w, int h, SpriteFont font, MouseState mouse, bool forceSubmit)
    {
        bool completed = q.Completed;
        Color accent = completed ? AccentGreen : AccentBlue;
        string stateText = completed ? "✔ ГОТОВО К СДАЧЕ" : "В ПРОЦЕССЕ";

        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, h), BgCard);
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, BarWidth, h), accent);

        int textX = x + BarWidth + 4;
        int textY = y + CardPadY;
        int innerW = w - CardPadX * 2 - BarWidth - 4;

        DrawText(sb, q.Title ?? "Без названия", textX, textY, TextWhite);
        textY += LineHeight;

        DrawText(sb, stateText, textX, textY, accent);
        textY += LineHeight;

        int descH = MeasureWrappedText(q.Description ?? "", innerW, font).H;
        DrawWrappedText(sb, q.Description ?? "", textX, textY, innerW, TextDesc, font);
        textY += descH;

        // Прогресс-бар
        int barW = innerW;
        int barH = 8;
        int barX = textX;
        int barY = y + h - barH - CardPadY;
        float pct = q.Target > 0 ? Math.Min(1f, (float)q.Current / q.Target) : 0f;
        sb.Draw(SpriteCache.Pixel, new Rectangle(barX, barY, barW, barH), new Color(30, 32, 42));
        sb.Draw(SpriteCache.Pixel, new Rectangle(barX, barY, (int)(barW * pct), barH), accent);
        string progress = $"{Math.Min(q.Current, q.Target)} / {q.Target}";
        var pSize = font.MeasureString(progress);
        DrawText(sb, progress, barX + barW - (int)pSize.X, barY - LineHeight - 1, TextProgress);

        if (completed || forceSubmit)
        {
            int btnW = 90;
            int btnH = 20;
            int btnX = x + w - btnW - CardPadX;
            int btnY = y + h - btnH - CardPadY;
            Color btnBg = BtnSubmit;
            if (mouse.X >= btnX && mouse.X <= btnX + btnW && mouse.Y >= btnY && mouse.Y <= btnY + btnH)
                btnBg = new Color(0, 170, 90);
            sb.Draw(SpriteCache.Pixel, new Rectangle(btnX, btnY, btnW, btnH), btnBg);
            DrawText(sb, "Завершить", btnX + (btnW - (int)font.MeasureString("Завершить").X) / 2, btnY + (btnH - (int)font.MeasureString("Завершить").Y) / 2, Color.White);
            _cardButtons.Add((new Rectangle(btnX, btnY, btnW, btnH), q.QuestId ?? "", true, false));
        }
        else
        {
            // Активное, ещё не выполненное задание — кнопка отказа
            int btnW = 90;
            int btnH = 20;
            int btnX = x + w - btnW - CardPadX;
            int btnY = y + h - btnH - CardPadY;
            Color btnBg = new Color(150, 60, 60);
            if (mouse.X >= btnX && mouse.X <= btnX + btnW && mouse.Y >= btnY && mouse.Y <= btnY + btnH)
                btnBg = new Color(190, 80, 80);
            sb.Draw(SpriteCache.Pixel, new Rectangle(btnX, btnY, btnW, btnH), btnBg);
            DrawText(sb, "Отказаться", btnX + (btnW - (int)font.MeasureString("Отказаться").X) / 2, btnY + (btnH - (int)font.MeasureString("Отказаться").Y) / 2, Color.White);
            _cardButtons.Add((new Rectangle(btnX, btnY, btnW, btnH), q.QuestId ?? "", false, true));
        }
    }
}
