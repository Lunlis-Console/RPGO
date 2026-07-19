using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;

namespace RPGGame.ClientMonoGame.Windows;

public sealed class QuestLogWindow : GameWindow
{
    private List<QuestInfo> _quests = new();
    private int _scrollOffset;
    private MouseState _prevMouse;
    private readonly List<(Rectangle Rect, string QuestId)> _cardButtons = new();

    public Action<string>? AbandonQuest { get; set; }

    private const int CardPadX = 10;
    private const int CardPadY = 8;
    private const int CardSpacing = 8;
    private const int BarWidth = 4;
    private const int LineHeight = 14;
    private const int HeaderHeight = 30;

    private static readonly Color BgCard = new(38, 40, 52);
    private static readonly Color AccentBlue = new(0, 120, 215);
    private static readonly Color AccentGreen = new(0, 180, 90);
    private static readonly Color TextWhite = Color.White;
    private static readonly Color TextMuted = new(150, 150, 160);
    private static readonly Color TextDesc = new(200, 200, 210);
    private static readonly Color TextProgress = new(150, 200, 255);
    private static readonly Color HeaderGold = new(220, 200, 120);

    public QuestLogWindow()
    {
        Title = "Журнал заданий";
        Width = 420;
        Height = 500;
        Visible = false;
    }

    public void UpdateData(List<QuestInfo> quests) => SetQuests(quests);

    public void UpdateActive(List<QuestInfo> quests) => SetQuests(quests);

    private void SetQuests(List<QuestInfo>? quests)
    {
        _quests = SortQuests(quests ?? new List<QuestInfo>());
        _scrollOffset = 0;
    }

    private static List<QuestInfo> SortQuests(List<QuestInfo> quests)
    {
        // Выполненные сверху, затем активные (готовые к завершению), затем в процессе
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

        // Скролл колесом мыши
        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (wheel != 0)
            _scrollOffset += wheel > 0 ? -30 : 30;

        if (keyboard.IsKeyDown(Keys.PageUp))
            _scrollOffset = Math.Max(0, _scrollOffset - 30);
        if (keyboard.IsKeyDown(Keys.PageDown))
            _scrollOffset += 30;

        int cx = ContentX, cy = ContentY + HeaderHeight, cw = ContentW, ch = ContentH;
        int listH = ch - HeaderHeight - 32;
        int btnY = cy + listH + 6;
        int btnW = 100, btnH = 22;
        int btnX = cx + (cw - btnW) / 2;

        if (clicked)
        {
            if (mouse.X >= btnX && mouse.X <= btnX + btnW && mouse.Y >= btnY && mouse.Y <= btnY + btnH)
            {
                Visible = false;
            }
            else
            {
                // Кнопка «Отказаться» на карточках
                foreach (var b in _cardButtons)
                {
                    if (b.Rect.Contains(mouse.X, mouse.Y))
                    {
                        AbandonQuest?.Invoke(b.QuestId);
                        break;
                    }
                }
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

        string header = "ЖУРНАЛ ЗАДАНИЙ";
        var headerSize = font.MeasureString(header);
        DrawText(sb, header, cx + (cw - (int)headerSize.X) / 2, cy, HeaderGold);
        cy += HeaderHeight;

        int listH = ch - HeaderHeight - 32;

        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, listH), new Color(20, 22, 28));

        if (_quests.Count == 0)
        {
            string empty = "У вас пока нет заданий.";
            var emptySize = font.MeasureString(empty);
            DrawText(sb, empty, cx + (cw - (int)emptySize.X) / 2, cy + listH / 2 - (int)emptySize.Y / 2, TextMuted);
        }
        else
        {
            int totalContentHeight = 0;
            foreach (var q in _quests)
                totalContentHeight += GetCardHeight(q, cw, font) + CardSpacing;
            totalContentHeight = Math.Max(0, totalContentHeight - CardSpacing);

            int maxScroll = Math.Max(0, totalContentHeight - listH);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);

            int drawY = cy - _scrollOffset;
            var clipRect = new Rectangle(cx, cy, cw, listH);

            sb.End();
            var oldScissor = sb.GraphicsDevice.ScissorRectangle;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
                new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
            sb.GraphicsDevice.ScissorRectangle = clipRect;

            foreach (var q in _quests)
            {
                int cardH = GetCardHeight(q, cw, font);
                if (drawY < cy + listH && drawY + cardH > cy)
                    DrawQuestCard(sb, q, cx, drawY, cw, cardH, font, Mouse.GetState());
                drawY += cardH + CardSpacing;
            }

            sb.End();
            sb.GraphicsDevice.ScissorRectangle = oldScissor;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

            if (totalContentHeight > listH && maxScroll > 0)
            {
                int barH = Math.Max(30, (int)((float)listH / totalContentHeight * listH));
                int barY = cy + (int)((float)_scrollOffset / maxScroll * (listH - barH));
                sb.Draw(SpriteCache.Pixel, new Rectangle(cx + cw - 5, barY, 4, barH), new Color(100, 110, 130));
            }
        }

        int btnY = cy + listH + 6;
        int btnW = 100;
        int btnH = 22;
        int btnX = cx + (cw - btnW) / 2;
        var ms = Mouse.GetState();
        // Кнопка «Закрыть» — клик обрабатывается в Update
        var closeRect = new Rectangle(btnX, btnY, btnW, btnH);
        bool closeHover = closeRect.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, closeRect, closeHover ? new Color(150, 60, 60) : new Color(80, 40, 40));
        DrawText(sb, "Закрыть", btnX + (btnW - (int)font.MeasureString("Закрыть").X) / 2, btnY + (btnH - (int)font.MeasureString("Закрыть").Y) / 2, Color.White);
    }

    private int GetCardHeight(QuestInfo q, int availableWidth, SpriteFont font)
    {
        int innerW = availableWidth - CardPadX * 2 - BarWidth - 4;
        int h = CardPadY * 2;
        h += LineHeight;        // заголовок
        h += LineHeight;        // статус
        h += MeasureWrappedText(q.Description ?? "", innerW, font).H; // описание
        h += LineHeight;        // цель
        h += LineHeight;        // прогресс
        h += LineHeight;        // награда
        h += LineHeight;        // отступ под кнопку «Отказаться»
        return h;
    }

    private static string GetObjectiveText(QuestInfo q)
    {
        string verb = q.Type?.ToLower() switch
        {
            "kill" => "Убить",
            "collect" => "Собрать",
            "talk" => "Поговорить",
            "explore" => "Исследовать",
            _ => "Выполнить"
        };
        return $"{verb}: {q.Target}";
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

    private void DrawQuestCard(SpriteBatch sb, QuestInfo q, int x, int y, int w, int h, SpriteFont font, MouseState mouse)
    {
        bool completed = q.Completed;
        bool readyToComplete = !completed && q.Current >= q.Target && q.Target > 0;

        Color accent;
        string stateText;
        if (completed)
        {
            accent = AccentGreen;
            stateText = "✔ ВЫПОЛНЕНО";
        }
        else if (readyToComplete)
        {
            accent = new Color(255, 210, 60);
            stateText = "★ МОЖНО СДАТЬ!";
        }
        else
        {
            accent = AccentBlue;
            stateText = "АКТИВНО";
        }

        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, h), BgCard);
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, BarWidth, h), accent);

        int textX = x + BarWidth + 4;
        int textY = y + CardPadY;
        int innerW = w - CardPadX * 2 - BarWidth - 4;

        DrawText(sb, q.Title ?? "Без названия", textX, textY, TextWhite);
        textY += LineHeight;

        DrawText(sb, stateText, textX, textY, accent);
        textY += LineHeight;

        DrawWrappedText(sb, q.Description ?? "", textX, y + CardPadY + LineHeight * 2, innerW, TextDesc, font);

        // Нижняя зона: цель, прогресс, награда (без наложения), кнопка «Отказаться» отдельно
        int btnH = 20;
        int bottomY = y + h - CardPadY;

        // Кнопка «Отказаться» только для активных (не выполненных) заданий — в самом низу справа
        int btnW = 90;
        int btnX = x + w - btnW - CardPadX;
        int btnY = bottomY - btnH;
        if (!completed)
        {
            Color btnBg = new Color(150, 60, 60);
            if (mouse.X >= btnX && mouse.X <= btnX + btnW && mouse.Y >= btnY && mouse.Y <= btnY + btnH)
                btnBg = new Color(190, 80, 80);
            sb.Draw(SpriteCache.Pixel, new Rectangle(btnX, btnY, btnW, btnH), btnBg);
            DrawText(sb, "Отказаться", btnX + (btnW - (int)font.MeasureString("Отказаться").X) / 2, btnY + (btnH - (int)font.MeasureString("Отказаться").Y) / 2, Color.White);
            _cardButtons.Add((new Rectangle(btnX, btnY, btnW, btnH), q.QuestId ?? ""));
        }

        // Текстовая зона над кнопкой (слева, не под кнопкой)
        int textBottom = bottomY - (completed ? 0 : btnH + 2);
        int lineY = textBottom;
        string reward = $"Награда: {q.XpReward} XP, {q.GoldReward} зол.";
        DrawText(sb, reward, textX, lineY - LineHeight, new Color(220, 200, 120));
        string progress = completed ? "✔ Выполнено" : $"Прогресс: {Math.Min(q.Current, q.Target)} / {q.Target}";
        DrawText(sb, progress, textX, lineY - LineHeight * 2, readyToComplete ? accent : TextProgress);
        string objective = $"Цель: {GetObjectiveText(q)}";
        DrawText(sb, objective, textX, lineY - LineHeight * 3, TextMuted);
    }
}
