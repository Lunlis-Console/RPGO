using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.ClientMonoGame.Networking;

namespace LostAndDivine.ClientMonoGame.Windows;

/// <summary>
/// Журнал заданий: слева сворачиваемые секции «Сюжетные / Побочные / Повторяемые»
/// со списком названий, справа — панель с деталями выбранного задания
/// (описание, цели, награда, отказ). Выполненные квесты остаются в своих секциях
/// приглушёнными, повторяемые после сдачи исчезают из журнала.
/// </summary>
public sealed class QuestLogWindow : GameWindow
{
    private enum SectionKey { Story, Side, Repeatable }

    private static readonly (SectionKey Key, string Title)[] SectionDefs =
    {
        (SectionKey.Story, "Сюжетные"),
        (SectionKey.Side, "Побочные"),
        (SectionKey.Repeatable, "Повторяемые")
    };

    private List<QuestInfo> _active = new();
    private List<QuestInfo> _history = new();
    private readonly HashSet<SectionKey> _collapsed = new();
    private string? _selectedQuestId;
    private int _scrollOffset;
    private new MouseState _prevMouse;

    public Action<string>? AbandonQuest { get; set; }

    private const int CardPadX = 10;
    private const int CardPadY = 8;
    private const int BarWidth = 4;
    private const int LineHeight = 14;
    private const int HeaderHeight = 30;
    private const int ListW = 210;
    private const int ListGap = 6;
    private const int SectionHeaderH = 24;
    private const int RowH = 20;
    private const int RowIndent = 14;
    private const int IconSize = 26;
    private const int IconGap = 6;

    private static readonly Color BgList = new(20, 22, 28);
    private static readonly Color BgDetail = new(26, 28, 36);
    private static readonly Color BgSelected = new(45, 55, 75);
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

    public QuestLogWindow()
    {
        Title = "Журнал заданий";
        Width = 620;
        Height = 500;
        Visible = false;
    }

    public void UpdateData(List<QuestInfo> active, List<QuestInfo> history)
    {
        _active = active ?? new List<QuestInfo>();
        _history = history ?? new List<QuestInfo>();
        _scrollOffset = 0;
        var entries = Entries();
        if (_selectedQuestId == null || !entries.Any(e => e.Quest.QuestId == _selectedQuestId))
            _selectedQuestId = entries.FirstOrDefault().Quest?.QuestId;
    }

    private static SectionKey CategoryOf(QuestInfo q)
    {
        if (q.IsStory || !string.IsNullOrEmpty(q.ChainId)) return SectionKey.Story;
        if (q.Repeatable) return SectionKey.Repeatable;
        return SectionKey.Side;
    }

    /// <summary>Активные + выполненные (повторяемые историю не хранят и пропадают).</summary>
    private List<(SectionKey Key, QuestInfo Quest)> Entries()
    {
        var list = new List<(SectionKey Key, QuestInfo Quest)>();
        list.AddRange(_active.Select(q => (CategoryOf(q), q)));
        foreach (var q in _history)
        {
            if (q.Repeatable) continue;
            list.Add((CategoryOf(q), q));
        }
        int Rank(QuestInfo q) => q.Completed ? 3 : AllDone(q) ? 1 : 2;
        return list
            .OrderBy(e => e.Key)
            .ThenBy(e => Rank(e.Quest))
            .ThenBy(e => e.Quest.Title ?? "")
            .ToList();
    }

    private QuestInfo? SelectedQuest()
        => Entries().FirstOrDefault(e => e.Quest.QuestId == _selectedQuestId).Quest;

    private static bool AllDone(QuestInfo q)
    {
        if (q.Objectives != null && q.Objectives.Count > 0)
            return q.Objectives.All(o => o.Current >= o.Count);
        return q.Target > 0 && q.Current >= q.Target;
    }

    private static string FormatDate(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("dd.MM.yyyy");
        return "";
    }

    /// <summary>Кнопка «Отказаться» внизу панели деталей (пусто для выполненных).</summary>
    private Rectangle AbandonRect()
    {
        var sel = SelectedQuest();
        if (sel == null || sel.Completed) return Rectangle.Empty;
        int dx = ContentX + ListW + ListGap;
        int dw = ContentW - ListW - ListGap;
        int bottomY = ContentY + ContentH - 32 - CardPadY;
        return new Rectangle(dx + dw - 90 - CardPadX, bottomY - 22, 90, 22);
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        base.Update(gameTime, keyboard, mouse);
        if (!Visible) return;

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

        int cx = ContentX, cy = ContentY + HeaderHeight;
        int listH = ContentH - HeaderHeight - 32;

        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (wheel != 0)
            _scrollOffset += wheel > 0 ? -30 : 30;

        if (keyboard.IsKeyDown(Keys.PageUp))
            _scrollOffset = Math.Max(0, _scrollOffset - 30);
        if (keyboard.IsKeyDown(Keys.PageDown))
            _scrollOffset += 30;

        int btnY = cy + listH + 6;
        int btnW = 100, btnH = 22;
        int btnX = cx + (ContentW - btnW) / 2;

        if (clicked)
        {
            if (new Rectangle(btnX, btnY, btnW, btnH).Contains(mouse.X, mouse.Y))
            {
                Visible = false;
            }
            else if (AbandonRect().Contains(mouse.X, mouse.Y))
            {
                var sel = SelectedQuest();
                if (sel != null) AbandonQuest?.Invoke(sel.QuestId ?? "");
            }
            else if (mouse.X >= cx && mouse.X <= cx + ListW)
            {
                HandleListClick(mouse.X, mouse.Y, cx, cy);
            }
        }

        _prevMouse = mouse;
    }

    private bool HandleListClick(int mx, int my, int cx, int cy)
    {
        int y = cy - _scrollOffset;
        foreach (var def in SectionDefs)
        {
            var headerRect = new Rectangle(cx, y, ListW, SectionHeaderH);
            if (headerRect.Contains(mx, my))
            {
                if (!_collapsed.Remove(def.Key)) _collapsed.Add(def.Key);
                return true;
            }
            y += SectionHeaderH;
            if (_collapsed.Contains(def.Key)) continue;
            foreach (var entry in Entries().Where(e => e.Key == def.Key))
            {
                var row = new Rectangle(cx + RowIndent, y, ListW - RowIndent, RowH);
                if (row.Contains(mx, my))
                {
                    _selectedQuestId = entry.Quest.QuestId ?? "";
                    return true;
                }
                y += RowH;
            }
        }
        return false;
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
        var ms = Mouse.GetState();

        string header = "ЖУРНАЛ ЗАДАНИЙ";
        var headerSize = font.MeasureString(header);
        DrawText(sb, header, cx + (cw - (int)headerSize.X) / 2, cy, HeaderGold);
        cy += HeaderHeight;

        int listH = ch - HeaderHeight - 32;
        int dy = cy;

        var entries = Entries();
        int totalH = 0;
        foreach (var def in SectionDefs)
        {
            int count = entries.Count(e => e.Key == def.Key);
            totalH += SectionHeaderH;
            if (!_collapsed.Contains(def.Key)) totalH += count * RowH;
        }
        int maxScroll = Math.Max(0, totalH - listH);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);

        // Список секций слева (с клиппингом)
        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, dy, ListW, listH), BgList);

        sb.End();
        var oldScissor = sb.GraphicsDevice.ScissorRectangle;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
        sb.GraphicsDevice.ScissorRectangle = new Rectangle(cx, dy, ListW, listH);

        int y = dy - _scrollOffset;
        foreach (var def in SectionDefs)
        {
            bool collapsed = _collapsed.Contains(def.Key);
            int count = entries.Count(e => e.Key == def.Key);
            var headerRect = new Rectangle(cx, y, ListW, SectionHeaderH);
            bool headerHover = headerRect.Contains(ms.X, ms.Y);
            sb.Draw(SpriteCache.Pixel, headerRect, headerHover ? TabHover : TabIdle);
            string sectionLine = (collapsed ? "» " : "« ") + def.Title + $" ({count})";
            DrawText(sb, sectionLine, cx + 6, y + (SectionHeaderH - LineHeight) / 2, TextWhite);
            y += SectionHeaderH;
            if (collapsed) continue;

            foreach (var entry in entries.Where(e => e.Key == def.Key))
            {
                var q = entry.Quest;
                var row = new Rectangle(cx + RowIndent, y, ListW - RowIndent, RowH);
                bool selected = q.QuestId == _selectedQuestId;
                if (selected)
                    sb.Draw(SpriteCache.Pixel, row, BgSelected);
                Color c = q.Completed ? TextMuted : AllDone(q) ? AccentReady : selected ? TextWhite : TextWhite;
                string name = q.Title ?? "";
                while (name.Length > 0 && font.MeasureString(name).X > ListW - RowIndent - 8)
                    name = name.Substring(0, name.Length - 1);
                DrawText(sb, name, cx + RowIndent + 4, y + (RowH - LineHeight) / 2, c);
                y += RowH;
            }
        }

        if (entries.Count == 0)
        {
            string empty = "У вас пока нет заданий.";
            var emptySize = font.MeasureString(empty);
            DrawText(sb, empty, cx + (ListW - (int)emptySize.X) / 2, dy + listH / 2 - (int)emptySize.Y / 2, TextMuted);
        }

        sb.End();
        sb.GraphicsDevice.ScissorRectangle = oldScissor;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        // Панель деталей справа
        DrawDetail(sb, SelectedQuest(), cx + ListW + ListGap, dy, cw - ListW - ListGap, listH, font, ms);

        // Кнопка «Закрыть» — клик обрабатывается в Update
        int btnY = dy + listH + 6;
        int btnW = 100, btnH = 22;
        int btnX = cx + (cw - btnW) / 2;
        var closeRect = new Rectangle(btnX, btnY, btnW, btnH);
        bool closeHover = closeRect.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, closeRect, closeHover ? new Color(150, 60, 60) : new Color(80, 40, 40));
        DrawText(sb, "Закрыть", btnX + (btnW - (int)font.MeasureString("Закрыть").X) / 2, btnY + (btnH - (int)font.MeasureString("Закрыть").Y) / 2, Color.White);
    }

    private void DrawDetail(SpriteBatch sb, QuestInfo? q, int x, int y, int w, int h, SpriteFont font, MouseState ms)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, h), BgDetail);

        if (q == null)
        {
            string hint = "Выберите задание в списке.";
            var hintSize = font.MeasureString(hint);
            DrawText(sb, hint, x + (w - (int)hintSize.X) / 2, y + h / 2 - (int)hintSize.Y / 2, TextMuted);
            return;
        }

        bool completed = q.Completed;
        bool ready = !completed && AllDone(q);
        Color accent = completed ? AccentGreen : ready ? AccentReady : AccentBlue;

        string stateText = completed ? "+ ВЫПОЛНЕНО" : ready ? "* МОЖНО СДАТЬ!" : "АКТИВНО";
        if (completed)
        {
            string date = FormatDate(q.CompletedAt);
            if (date.Length > 0) stateText += $" · {date}";
        }

        sb.End();
        var oldScissor = sb.GraphicsDevice.ScissorRectangle;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
        sb.GraphicsDevice.ScissorRectangle = new Rectangle(x, y, w, h);

        int textX = x + CardPadX + IconSize + IconGap;
        int textY = y + CardPadY;
        int innerW = w - CardPadX * 2 - IconSize - IconGap;

        // Иконка квеста (первая цель)
        var iconRect = new Rectangle(x + CardPadX, y + CardPadY, IconSize, IconSize);
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
        if (!string.IsNullOrEmpty(q.ChainId))
            DrawText(sb, "*", iconRect.X + IconSize - 11, iconRect.Y - 3, HeaderGold);

        Color titleColor = completed ? new Color(170, 175, 185) : TextWhite;
        DrawText(sb, q.Title ?? "Без названия", textX, textY, titleColor);
        textY += LineHeight;

        DrawText(sb, stateText, textX, textY, accent);
        textY += LineHeight;

        string? chain = GetChainText(q);
        if (chain != null)
        {
            DrawText(sb, chain, textX, textY, new Color(200, 170, 110));
            textY += LineHeight;
        }

        textY += 4;

        // Описание задания («диалог»)
        DrawWrappedText(sb, q.Description ?? "", textX, textY, innerW, completed ? TextMuted : TextDesc, font);
        textY += MeasureWrappedText(q.Description ?? "", innerW, font).H + 8;

        // Цели (открытые на текущем этапе)
        var objectives = q.VisibleObjectives();
        if (objectives.Count == 0)
        {
            objectives = new List<QuestObjectiveInfo>
            {
                new() { Type = q.Type, Target = q.TargetNpcId, Count = q.Target, Current = q.Current, Label = GetObjectiveText(q) }
            };
        }
        foreach (var obj in objectives)
        {
            bool objDone = obj.Count > 0 && obj.Current >= obj.Count;
            string mark = objDone ? "+" : "·";
            string line = $"{mark} {obj.Label} — {Math.Min(obj.Current, obj.Count)}/{obj.Count}";
            DrawText(sb, line, textX, textY, completed ? TextMuted : objDone ? AccentGreen : TextProgress);
            textY += LineHeight;
        }

        textY += 4;

        // Награда
        string rewards = $"Награда: {q.XpReward} XP, {q.GoldReward} зол.";
        DrawText(sb, rewards, textX, textY, completed ? new Color(150, 140, 100) : new Color(220, 200, 120));

        // Кнопка «Отказаться» — только для активных (не выполненных)
        if (!completed)
        {
            var btn = AbandonRect();
            Color btnBg = btn.Contains(ms.X, ms.Y) ? new Color(190, 80, 80) : new Color(150, 60, 60);
            sb.Draw(SpriteCache.Pixel, btn, btnBg);
            DrawText(sb, "Отказаться", btn.X + (btn.Width - (int)font.MeasureString("Отказаться").X) / 2, btn.Y + (btn.Height - (int)font.MeasureString("Отказаться").Y) / 2, Color.White);
        }

        sb.End();
        sb.GraphicsDevice.ScissorRectangle = oldScissor;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
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
}