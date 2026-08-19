using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.ClientMonoGame.Networking;

namespace LostAndDivine.ClientMonoGame.Windows;

public sealed class QuestLogWindow : GameWindow
{
    private enum Filter { All, Active, History }

    private List<QuestInfo> _active = new();
    private List<QuestInfo> _history = new();
    private Filter _filter = Filter.All;
    private int _scrollOffset;
    private new MouseState _prevMouse;
    private readonly List<(Rectangle Rect, string QuestId)> _cardButtons = new();

    public Action<string>? AbandonQuest { get; set; }

    private const int CardPadX = 10;
    private const int CardPadY = 8;
    private const int CardSpacing = 8;
    private const int BarWidth = 4;
    private const int LineHeight = 14;
    private const int HeaderHeight = 30;
    private const int TabHeight = 24;
    private const int IconSize = 26;
    private const int IconGap = 6;

    private static readonly Color BgCard = new(38, 40, 52);
    private static readonly Color BgCardHistory = new(32, 34, 42);
    private static readonly Color AccentBlue = new(0, 120, 215);
    private static readonly Color AccentGreen = new(0, 180, 90);
    private static readonly Color AccentReady = new(255, 210, 60);
    private static readonly Color TextWhite = Color.White;
    private static readonly Color TextMuted = new(150, 150, 160);
    private static readonly Color TextDesc = new(200, 200, 210);
    private static readonly Color TextProgress = new(150, 200, 255);
    private static readonly Color HeaderGold = new(220, 200, 120);
    private static readonly Color TabIdle = new(42, 46, 60);
    private static readonly Color TabHover = new(55, 60, 78);
    private static readonly Color TabActive = new(70, 80, 105);

    public QuestLogWindow()
    {
        Title = "Журнал заданий";
        Width = 420;
        Height = 500;
        Visible = false;
    }

    public void UpdateData(List<QuestInfo> active, List<QuestInfo> history)
    {
        _active = SortQuests(active ?? new List<QuestInfo>());
        _history = history ?? new List<QuestInfo>();
        _scrollOffset = 0;
    }

    private static List<QuestInfo> SortQuests(List<QuestInfo> quests)
    {
        // Готовые к сдаче сверху, затем в процессе
        int Rank(QuestInfo q)
        {
            if (q.Completed) return 0;
            return AllDone(q) ? 1 : 2;
        }
        return quests.OrderBy(Rank).ThenBy(q => q.Title ?? "").ToList();
    }

    private static bool AllDone(QuestInfo q)
    {
        if (q.Objectives != null && q.Objectives.Count > 0)
            return q.Objectives.All(o => o.Current >= o.Count);
        return q.Target > 0 && q.Current >= q.Target;
    }

    private static int ObjectiveCount(QuestInfo q)
    {
        if (q.Objectives is { Count: > 0 }) return q.VisibleObjectives().Count;
        return 1;
    }

    private List<QuestInfo> GetVisibleQuests()
    {
        switch (_filter)
        {
            case Filter.Active:
                return _active;
            case Filter.History:
                return _history;
            default:
                return _active.Concat(_history).ToList();
        }
    }

    private static string FormatDate(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("dd.MM.yyyy");
        return "";
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
        int listH = ch - HeaderHeight - TabHeight - 32;
        int btnY = cy + TabHeight + listH + 6;
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
                // Вкладки фильтров
                var tabs = GetTabRects(cx, cy, cw);
                bool tabHit = false;
                for (int i = 0; i < tabs.Count; i++)
                {
                    if (tabs[i].Contains(mouse.X, mouse.Y))
                    {
                        var newFilter = (Filter)i;
                        if (newFilter != _filter)
                        {
                            _filter = newFilter;
                            _scrollOffset = 0;
                        }
                        tabHit = true;
                        break;
                    }
                }
                if (!tabHit)
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
        }

        _cardButtons.Clear();
        _prevMouse = mouse;
    }

    private List<Rectangle> GetTabRects(int cx, int cy, int cw)
    {
        int gap = 4;
        int tabW = (cw - gap * 2) / 3;
        return new List<Rectangle>
        {
            new Rectangle(cx, cy, tabW, TabHeight),
            new Rectangle(cx + tabW + gap, cy, tabW, TabHeight),
            new Rectangle(cx + tabW * 2 + gap * 2, cy, tabW, TabHeight)
        };
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

        // Вкладки фильтров
        int totalCount = _active.Count + _history.Count;
        var tabs = GetTabRects(cx, cy, cw);
        string[] tabLabels =
        {
            $"Все ({totalCount})",
            $"Активные ({_active.Count})",
            $"История ({_history.Count})"
        };
        var ms = Mouse.GetState();
        for (int i = 0; i < tabs.Count; i++)
        {
            bool selected = (int)_filter == i;
            bool hover = tabs[i].Contains(ms.X, ms.Y);
            Color bg = selected ? TabActive : hover ? TabHover : TabIdle;
            sb.Draw(SpriteCache.Pixel, tabs[i], bg);
            Color fg = selected ? Color.White : TextMuted;
            var labelSize = font.MeasureString(tabLabels[i]);
            DrawText(sb, tabLabels[i], tabs[i].X + (tabs[i].Width - (int)labelSize.X) / 2, tabs[i].Y + (tabs[i].Height - (int)labelSize.Y) / 2, fg);
        }
        cy += TabHeight;

        int listH = ch - HeaderHeight - TabHeight - 32;

        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, listH), new Color(20, 22, 28));

        var visible = GetVisibleQuests();
        if (visible.Count == 0)
        {
            string empty = _filter == Filter.History ? "История пуста." : "У вас пока нет заданий.";
            var emptySize = font.MeasureString(empty);
            DrawText(sb, empty, cx + (cw - (int)emptySize.X) / 2, cy + listH / 2 - (int)emptySize.Y / 2, TextMuted);
        }
        else
        {
            int totalContentHeight = 0;
            foreach (var q in visible)
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

            foreach (var q in visible)
            {
                int cardH = GetCardHeight(q, cw, font);
                if (drawY < cy + listH && drawY + cardH > cy)
                    DrawQuestCard(sb, q, cx, drawY, cw, cardH, font, ms);
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
        // Кнопка «Закрыть» — клик обрабатывается в Update
        var closeRect = new Rectangle(btnX, btnY, btnW, btnH);
        bool closeHover = closeRect.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, closeRect, closeHover ? new Color(150, 60, 60) : new Color(80, 40, 40));
        DrawText(sb, "Закрыть", btnX + (btnW - (int)font.MeasureString("Закрыть").X) / 2, btnY + (btnH - (int)font.MeasureString("Закрыть").Y) / 2, Color.White);
    }

    private int GetCardHeight(QuestInfo q, int availableWidth, SpriteFont font)
    {
        int innerW = availableWidth - CardPadX * 2 - BarWidth - 4 - IconSize - IconGap;
        int h = CardPadY * 2;
        h += LineHeight;        // заголовок
        h += LineHeight;        // статус
        if (GetChainText(q) != null) h += LineHeight; // сюжет
        h += MeasureWrappedText(q.Description ?? "", innerW, font).H; // описание
        h += ObjectiveCount(q) * LineHeight; // цели
        h += LineHeight;        // награда
        if (!q.Completed) h += LineHeight; // отступ под кнопку «Отказаться»
        return h;
    }

    private static string GetObjectiveText(QuestInfo q)
    {
        string verb = q.Type?.ToLower() switch
        {
            "kill" => "Убить",
            "collect" => "Собрать",
            "talk" => "Поговорить",
            "travel" => "Отправиться",
            "use" => "Использовать",
            "explore" => "Исследовать",
            _ => "Выполнить"
        };
        if (!string.IsNullOrEmpty(q.TargetZoneId))
            return $"{verb}: {q.TargetZoneId}";
        if (!string.IsNullOrEmpty(q.TargetNpcId))
            return $"{verb}: {q.TargetNpcId}";
        return $"{verb}: {q.Target}";
    }

    private static string? GetChainText(QuestInfo q)
    {
        if (string.IsNullOrEmpty(q.ChainId)) return null;
        return q.Step > 0 ? $"* Сюжет: {q.ChainId} · Шаг {q.Step}" : $"* Сюжет: {q.ChainId}";
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
        int curY = y;
        int lineHeight = (int)font.LineSpacing;
        float lineWidth = 0;

        foreach (var word in text.Split(' '))
        {
            float wordW = font.MeasureString(word).X;
            if (lineWidth > 0 && lineWidth + spaceW + wordW > maxW)
            {
                curY += lineHeight;
                lineWidth = 0;
            }
            float wordX = x + lineWidth + (lineWidth > 0 ? spaceW : 0);
            sb.DrawString(font, word, new Vector2(wordX, curY), color);
            lineWidth += (lineWidth > 0 ? spaceW : 0) + wordW;
        }
    }

    /// <summary>Иконка квеста по ключу с сервера (monster:{id}, item:{type}, npc, worldmap).</summary>
    private static Texture2D? GetQuestIcon(QuestInfo q)
    {
        string? icon = q.Icon;
        if (string.IsNullOrEmpty(icon)) return null;
        if (icon.StartsWith("monster:", StringComparison.OrdinalIgnoreCase))
            return SpriteCache.GetMonsterSprite(icon.Substring(8));
        if (icon.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
            return SpriteCache.ForItemType(icon.Substring(5));
        return icon.ToLowerInvariant() switch
        {
            "npc" => SpriteCache.GetIconCommunication(),
            "worldmap" => SpriteCache.GetIconWorldMap(),
            _ => null
        };
    }

    private static string GetIconSymbol(QuestInfo q) => (q.Type ?? "").ToLower() switch
    {
        "kill" => "×",
        "collect" => "»",
        "use" => "!",
        "talk" => "«",
        "travel" => ">",
        "explore" => "*",
        _ => "·"
    };

    private void DrawQuestCard(SpriteBatch sb, QuestInfo q, int x, int y, int w, int h, SpriteFont font, MouseState mouse)
    {
        bool completed = q.Completed;
        bool readyToComplete = !completed && AllDone(q);

        Color accent;
        string stateText;
        if (completed)
        {
            accent = AccentGreen;
            stateText = "+ ВЫПОЛНЕНО";
        }
        else if (readyToComplete)
        {
            accent = AccentReady;
            stateText = "* МОЖНО СДАТЬ!";
        }
        else
        {
            accent = AccentBlue;
            stateText = "АКТИВНО";
        }

        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, h), completed ? BgCardHistory : BgCard);
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, BarWidth, h), accent);

        int textX = x + BarWidth + 4 + IconSize + IconGap;
        int textY = y + CardPadY;
        int innerW = w - CardPadX * 2 - BarWidth - 4 - IconSize - IconGap;

        // Иконка квеста (первая цель): текстура или символ типа
        var iconRect = new Rectangle(x + BarWidth + 6, y + CardPadY, IconSize, IconSize);
        sb.Draw(SpriteCache.Pixel, iconRect, new Color(24, 26, 34));
        sb.Draw(SpriteCache.Pixel, new Rectangle(iconRect.X, iconRect.Y, iconRect.Width, 1), accent);
        sb.Draw(SpriteCache.Pixel, new Rectangle(iconRect.X, iconRect.Y, 1, iconRect.Height), accent);
        sb.Draw(SpriteCache.Pixel, new Rectangle(iconRect.X, iconRect.Bottom - 1, iconRect.Width, 1), accent);
        sb.Draw(SpriteCache.Pixel, new Rectangle(iconRect.Right - 1, iconRect.Y, 1, iconRect.Height), accent);
        var iconTex = GetQuestIcon(q);
        if (iconTex != null)
        {
            int inner = IconSize - 6;
            float scale = Math.Min((float)inner / iconTex.Width, (float)inner / iconTex.Height);
            int dw = (int)(iconTex.Width * scale);
            int dh = (int)(iconTex.Height * scale);
            sb.Draw(iconTex, new Rectangle(iconRect.X + (IconSize - dw) / 2, iconRect.Y + (IconSize - dh) / 2, dw, dh), completed ? new Color(140, 150, 160) : Color.White);
        }
        else
        {
            string sym = GetIconSymbol(q);
            var symSize = font.MeasureString(sym);
            DrawText(sb, sym, iconRect.X + (IconSize - (int)symSize.X) / 2, iconRect.Y + (IconSize - (int)symSize.Y) / 2, completed ? TextMuted : accent);
        }
        // Бейдж сюжетного квеста — золотая звезда на иконке
        if (!string.IsNullOrEmpty(q.ChainId))
            DrawText(sb, "*", iconRect.X + IconSize - 11, iconRect.Y - 3, HeaderGold);

        Color titleColor = completed ? new Color(170, 175, 185) : TextWhite;
        DrawText(sb, q.Title ?? "Без названия", textX, textY, titleColor);
        textY += LineHeight;

        string stateLine = stateText;
        if (completed)
        {
            string date = FormatDate(q.CompletedAt);
            if (date.Length > 0) stateLine += $" · {date}";
        }
        DrawText(sb, stateLine, textX, textY, accent);
        textY += LineHeight;

        string? chain = GetChainText(q);
        if (chain != null)
        {
            DrawText(sb, chain, textX, textY, new Color(200, 170, 110));
            textY += LineHeight;
        }

        DrawWrappedText(sb, q.Description ?? "", textX, textY, innerW, completed ? TextMuted : TextDesc, font);
        textY += MeasureWrappedText(q.Description ?? "", innerW, font).H;

        // Кнопка «Отказаться» только для активных (не выполненных) заданий — в самом низу справа
        int btnH = 20;
        int bottomY = y + h - CardPadY;

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

        // Текстовая зона: цели (открытые на текущем этапе, по одной строке) + награда
        var objectives = q.VisibleObjectives();
        if (objectives.Count == 0)
        {
            objectives = new List<QuestObjectiveInfo>
            {
                new() { Type = q.Type, Target = q.TargetNpcId, Count = q.Target, Current = q.Current, Label = GetObjectiveText(q) }
            };
        }

        int objY = textY;
        foreach (var obj in objectives)
        {
            bool objDone = obj.Count > 0 && obj.Current >= obj.Count;
            string mark = objDone ? "+" : "·";
            string line = $"{mark} {obj.Label} — {Math.Min(obj.Current, obj.Count)}/{obj.Count}";
            DrawText(sb, line, textX, objY, completed ? TextMuted : objDone ? AccentGreen : TextProgress);
            objY += LineHeight;
        }

        DrawText(sb, $"Награда: {q.XpReward} XP, {q.GoldReward} зол.", textX, objY, completed ? new Color(150, 140, 100) : new Color(220, 200, 120));
    }
}