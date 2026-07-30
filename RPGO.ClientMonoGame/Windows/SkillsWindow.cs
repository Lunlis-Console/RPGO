using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;
using System.Text.RegularExpressions;

namespace RPGGame.ClientMonoGame.Windows;

public class SkillsWindow : GameWindow
{
    private List<ClientSkillInfo> _skills = new();
    private int _playerLevel = 1;
    private int _skillPoints;
    private new MouseState _prevMouse;
    private KeyboardState _prevKey;
    private NodeLayout? _hoverNode;

    private const int HeaderH = 24;
    private const int PathTitleH = 22;
    private const int NodeW = 48;
    private const int NodeH = 48;
    private const int NodeGapY = 12;
    private const int ColGapX = 16;
    private const int SubColGapX = 20;
    private const int BranchHeaderH = 22;

    // Путь (колонка дерева): Меч или Лук, внутри — активные и пассивные навыки.
    private class PathGroup
    {
        public string PathId = "";          // "Меч" / "Лук"
        public string PathTitle = "";       // "Путь меча" / "Путь лука"
        public List<ClientSkillInfo> Active = new();
        public List<ClientSkillInfo> Passive = new();
    }

    private List<PathGroup> _pathGroups = new();

    public Action<string>? UseSkill { get; set; }
    public Action<string>? LearnSkill { get; set; }
    public Action? ResetSkills { get; set; }
    public Action<ClientSkillInfo?>? SkillDragStateChanged { get; set; }
    public Action? SkillDragEnded { get; set; }

    private NodeLayout? _dragNode;
    private Point _dragStart;

    public SkillsWindow()
    {
        Title = "Древо навыков";
        Width = 560;
        Height = 520;
        Visible = false;
    }

    public void SetPlayerLevel(int level) => _playerLevel = level;

    public void SetSkillPoints(int points) => _skillPoints = points;

    public void UpdateData(List<ClientSkillInfo> skills)
    {
        _skills = skills ?? new();
        RebuildTree();
    }

    private void RebuildTree()
    {
        _pathGroups = new();
        var byPath = new Dictionary<string, PathGroup>();
        foreach (var s in _skills)
        {
            // Type: "Меч · Акт", "Меч · Пас", "Лук · Акт", "Лук · Пас"
            string type = string.IsNullOrWhiteSpace(s.Type) ? "Основные" : s.Type;
            string pathId = type.StartsWith("Меч") ? "Меч" : type.StartsWith("Лук") ? "Лук" : "Прочее";
            string pathTitle = pathId switch { "Меч" => "Путь меча", "Лук" => "Путь лука", _ => type };
            bool isPassive = type.Contains("Пас") || type == "Пассивные";

            if (!byPath.TryGetValue(pathId, out var pg))
            {
                pg = new PathGroup { PathId = pathId, PathTitle = pathTitle };
                byPath[pathId] = pg;
                _pathGroups.Add(pg);
            }
            (isPassive ? pg.Passive : pg.Active).Add(s);
        }
        _pathGroups.Sort((a, b) => string.Compare(a.PathId, b.PathId, StringComparison.Ordinal));
        foreach (var pg in _pathGroups)
        {
            pg.Active.Sort((a, c) => a.Tier != c.Tier ? a.Tier.CompareTo(c.Tier) : a.MinLevel.CompareTo(c.MinLevel));
            pg.Passive.Sort((a, c) => a.Tier != c.Tier ? a.Tier.CompareTo(c.Tier) : a.MinLevel.CompareTo(c.MinLevel));
        }
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        bool pressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        bool rightClicked = mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
        var (nodes, _) = Layout(mouse);

        // ПКМ — изучить / улучшить навык
        if (rightClicked)
        {
            foreach (var n in nodes)
            {
                if (n.Rect.Contains(mouse.X, mouse.Y) && n.Available)
                {
                    if (!n.Skill.Learned || n.Skill.Rank < n.Skill.MaxRank)
                        LearnSkill?.Invoke(n.Skill.Id);
                    break;
                }
            }
        }

        // Кнопка «Сброс навыков»
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font != null && pressed)
        {
            string resetText = "Сброс навыков";
            var resetSz = font.MeasureString(resetText);
            int btnW = (int)resetSz.X + 16;
            int btnH = 22;
            int btnX = ContentX + ContentW - btnW;
            int btnY = ContentY + ContentH - btnH;
            var resetRect = new Rectangle(btnX, btnY, btnW, btnH);
            if (resetRect.Contains(mouse.X, mouse.Y))
            {
                ResetSkills?.Invoke();
            }
        }

        if (pressed)
        {
            foreach (var n in nodes)
            {
                if (n.Rect.Contains(mouse.X, mouse.Y) && n.Available)
                {
                    if (n.Skill.Learned)
                    {
                        _dragNode = n;
                        _dragStart = new Point(mouse.X, mouse.Y);
                        SkillDragStateChanged?.Invoke(n.Skill);
                    }
                    break;
                }
            }
        }
        else if (released && _dragNode != null)
        {
            int moved = Math.Abs(mouse.X - _dragStart.X) + Math.Abs(mouse.Y - _dragStart.Y);
            if (moved < 6)
                UseSkill?.Invoke(_dragNode.Skill.Id);
            _dragNode = null;
            SkillDragEnded?.Invoke();
        }
        else if (released)
        {
            foreach (var n in nodes)
            {
                if (n.Rect.Contains(mouse.X, mouse.Y) && n.Available && !n.Skill.Learned)
                {
                    LearnSkill?.Invoke(n.Skill.Id);
                    break;
                }
            }
        }

        base.Update(gameTime, keyboard, mouse);
        _prevMouse = mouse;
        _prevKey = keyboard;
    }

    // Вычисляет позиции всех узлов дерева. Возвращает список узлов и карту Id->узел.
    private (List<NodeLayout> nodes, Dictionary<string, NodeLayout> byId) Layout(MouseState mouse)
    {
        var nodes = new List<NodeLayout>();
        var byId = new Dictionary<string, NodeLayout>();

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        int startX = ContentX;
        int startY = ContentY + HeaderH + PathTitleH + BranchHeaderH;

        int colX = startX;
        foreach (var pg in _pathGroups)
        {
            // Ширина подколонки = макс(NodeW, ширина текста «Активные»/«Пассивные» + запас)
            var aw = font?.MeasureString("Активные").X ?? 48;
            var pw2 = font?.MeasureString("Пассивные").X ?? 48;
            int subColW = Math.Max(NodeW, Math.Max((int)aw, (int)pw2) + 10);

            int totalW = subColW * 2 + SubColGapX;
            if (font != null)
            {
                var pw = font.MeasureString(pg.PathTitle).X;
                totalW = Math.Max(totalW, (int)pw + 16);
            }

            int activeX = colX + (totalW - subColW * 2 - SubColGapX) / 2;
            int passiveX = activeX + subColW + SubColGapX;

            int nodeOffX = (subColW - NodeW) / 2;
            int y = startY;
            foreach (var skill in pg.Active)
            {
                var rect = new Rectangle(activeX + nodeOffX, y, NodeW, NodeH);
                bool available = skill.MinLevel <= _playerLevel;
                nodes.Add(new NodeLayout { Skill = skill, Rect = rect, Available = available, Branch = pg.PathId });
                if (!byId.ContainsKey(skill.Id)) byId[skill.Id] = nodes[^1];
                y += NodeH + NodeGapY;
            }

            y = startY;
            foreach (var skill in pg.Passive)
            {
                var rect = new Rectangle(passiveX + nodeOffX, y, NodeW, NodeH);
                bool available = skill.MinLevel <= _playerLevel;
                nodes.Add(new NodeLayout { Skill = skill, Rect = rect, Available = available, Branch = pg.PathId });
                if (!byId.ContainsKey(skill.Id)) byId[skill.Id] = nodes[^1];
                y += NodeH + NodeGapY;
            }

            colX += totalW + ColGapX;
        }

        // Линии связи к родителю (в той же ветке)
        foreach (var n in nodes)
        {
            if (!string.IsNullOrEmpty(n.Skill.ParentId) && byId.TryGetValue(n.Skill.ParentId, out var parent) && parent.Branch == n.Branch)
            {
                n.ParentRect = parent.Rect;
            }
        }

        return (nodes, byId);
    }

    private class NodeLayout
    {
        public ClientSkillInfo Skill = null!;
        public Rectangle Rect;
        public bool Available;
        public string Branch = "";
        public Rectangle ParentRect;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        var mouse = Mouse.GetState();
        base.Draw(sb, mouse);

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int cx = ContentX, cy = ContentY, cw = ContentW, ch = ContentH;

        DrawText(sb, "Древо навыков", cx + cw / 2 - (int)(font.MeasureString("Древо навыков").X / 2), cy, new Color(100, 160, 255));

        string ptsText = $"Очки навыков: {_skillPoints}";
        DrawText(sb, ptsText, cx + cw - (int)font.MeasureString(ptsText).X - 4, cy, _skillPoints > 0 ? new Color(255, 215, 0) : new Color(120, 120, 130));

        if (_skills.Count == 0)
        {
            DrawText(sb, "Нет навыков.", cx + cw / 2 - (int)(font.MeasureString("Нет навыков.").X / 2),
                cy + HeaderH + 30, new Color(120, 120, 130));
            return;
        }

        var (nodes, byId) = Layout(mouse);

        // Линии связи (рисуем до узлов)
        foreach (var n in nodes)
        {
            if (n.ParentRect != default)
            {
                int x1 = n.ParentRect.X + n.ParentRect.Width / 2;
                int y1 = n.ParentRect.Y + n.ParentRect.Height / 2;
                int x2 = n.Rect.X + n.Rect.Width / 2;
                int y2 = n.Rect.Y + n.Rect.Height / 2;
                Color lineCol = n.Available ? new Color(90, 130, 200) : new Color(70, 70, 80);
                if (y1 <= y2)
                {
                    sb.Draw(SpriteCache.Pixel, new Rectangle(x1 - 1, y1, 2, y2 - y1), lineCol);
                    sb.Draw(SpriteCache.Pixel, new Rectangle(Math.Min(x1, x2), y2 - 1, Math.Abs(x2 - x1) + 2, 2), lineCol);
                }
                else
                {
                    sb.Draw(SpriteCache.Pixel, new Rectangle(x2 - 1, y2, 2, y1 - y2), lineCol);
                    sb.Draw(SpriteCache.Pixel, new Rectangle(Math.Min(x1, x2), y1 - 1, Math.Abs(x2 - x1) + 2, 2), lineCol);
                }
            }
        }

        // Заголовки: название пути + «Активные» / «Пассивные» над каждой подколонкой
        int colX = cx;
        foreach (var pg in _pathGroups)
        {
            var aw = font.MeasureString("Активные").X;
            var pw2 = font.MeasureString("Пассивные").X;
            int subColW = Math.Max(NodeW, Math.Max((int)aw, (int)pw2) + 10);

            int totalW = subColW * 2 + SubColGapX;
            var pw = font.MeasureString(pg.PathTitle).X;
            totalW = Math.Max(totalW, (int)pw + 16);

            int activeX = colX + (totalW - subColW * 2 - SubColGapX) / 2;
            int passiveX = activeX + subColW + SubColGapX;

            // Название пути (по центру над обеими подколонками)
            int pathTitleX = colX + (totalW - (int)pw) / 2;
            DrawText(sb, pg.PathTitle, pathTitleX, cy + HeaderH + 2, new Color(220, 200, 130));

            // «Активные» / «Пассивные»
            int labelY = cy + HeaderH + PathTitleH;
            int activeLabelX = activeX + (subColW - (int)aw) / 2;
            DrawText(sb, "Активные", activeLabelX, labelY, new Color(180, 180, 200));
            int passiveLabelX = passiveX + (subColW - (int)pw2) / 2;
            DrawText(sb, "Пассивные", passiveLabelX, labelY, new Color(160, 160, 180));

            colX += totalW + ColGapX;
        }

        // Узлы
        _hoverNode = null;
        foreach (var n in nodes)
        {
            if (n.Rect.Contains(mouse.X, mouse.Y)) _hoverNode = n;

            var skill = n.Skill;
            bool hover = n.Rect.Contains(mouse.X, mouse.Y);
            Color bg = !n.Available ? new Color(34, 34, 40)
                      : skill.Learned ? (hover ? new Color(40, 75, 50) : new Color(34, 55, 44))
                      : hover ? new Color(75, 65, 35) : new Color(55, 50, 34);
            sb.Draw(SpriteCache.Pixel, n.Rect, bg);
            sb.Draw(SpriteCache.Pixel, new Rectangle(n.Rect.X, n.Rect.Y, n.Rect.Width, 2),
                n.Available ? (skill.Learned ? new Color(80, 180, 100) : new Color(180, 160, 80)) : new Color(70, 70, 80));

            // Рамка выделения при наведении
            if (hover)
                DrawRect(sb, n.Rect, skill.Learned ? new Color(100, 220, 130) : new Color(220, 200, 100), 2);

            // Иконка (по типу или дефолтная)
            var spr = !string.IsNullOrEmpty(skill.IconName) ? SpriteCache.Get(skill.IconName)
                      : SpriteCache.ForItemType(skill.Type);
            if (spr != null)
            {
                int iconSize = 42;
                int iconX = n.Rect.X + (n.Rect.Width - iconSize) / 2;
                int iconY = n.Rect.Y + (n.Rect.Height - iconSize) / 2;
                sb.Draw(spr, new Rectangle(iconX, iconY, iconSize, iconSize), Color.White);
            }

            // Затемнение если навык не изучен
            if (!skill.Learned)
                sb.Draw(SpriteCache.Pixel, n.Rect, new Color(0, 0, 0, 120));

            // Ранг навыка (I, II, III)
            if (skill.Learned && skill.MaxRank > 1 && skill.Rank > 0)
            {
                string rankStr = skill.Rank switch { 1 => "I", 2 => "II", 3 => "III", _ => $"{skill.Rank}" };
                DrawText(sb, rankStr, n.Rect.X + n.Rect.Width - 16, n.Rect.Y + 2,
                    skill.Rank >= skill.MaxRank ? new Color(255, 215, 0) : new Color(200, 200, 200));
            }
        }

        // Tooltip при наведении
        if (_hoverNode != null)
            DrawTooltip(sb, _hoverNode.Skill, mouse);

        // Кнопка «Сброс навыков»
        {
            string resetText = "Сброс навыков";
            var resetSz = font.MeasureString(resetText);
            int btnW = (int)resetSz.X + 16;
            int btnH = 22;
            int btnX = cx + cw - btnW;
            int btnY = cy + ch - btnH;
            var resetRect = new Rectangle(btnX, btnY, btnW, btnH);
            bool resetHover = resetRect.Contains(mouse.X, mouse.Y);
            Color resetBg = resetHover ? new Color(120, 50, 50) : new Color(80, 35, 35);
            sb.Draw(SpriteCache.Pixel, resetRect, resetBg);
            DrawRect(sb, resetRect, new Color(160, 70, 70), 1);
            DrawText(sb, resetText, btnX + 8, btnY + 3, new Color(220, 160, 160));
        }
    }

    private void DrawTooltip(SpriteBatch sb, ClientSkillInfo skill, MouseState mouse)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        bool isPassive = skill.Type.Contains("Пас");

        var lines = new List<string>
        {
            skill.Name,
            $"Тир {skill.Tier}  •  {(string.IsNullOrWhiteSpace(skill.Type) ? "Основные" : skill.Type.ToLowerInvariant())}"
        };

        // Ранг (только если изучен)
        if (skill.Learned)
            lines.Add($"Ранг {skill.Rank}/{skill.MaxRank}");

        // Статы активного навыка
        if (!isPassive)
        {
            double curDmg = skill.DamageMultiplier * (1.0 + (skill.Rank - 1) * 0.12);
            int curCd = (int)(skill.CooldownMs * (1.0 - (skill.Rank - 1) * 0.08)) / 1000;
            lines.Add($"МП {skill.MpCost}  •  КД {curCd}с  •  Урон x{curDmg:F2}");
        }

        lines.Add($"Мин. уровень {skill.MinLevel}");

        // Описание (с ранговой корректировкой чисел)
        if (!string.IsNullOrEmpty(skill.Description))
        {
            string desc = skill.Description;
            if (skill.Learned && skill.Rank > 1)
            {
                double mult = isPassive
                    ? 1.0 + (skill.Rank - 1) * 0.33
                    : 1.0 + (skill.Rank - 1) * 0.12;
                desc = RankAdjustDescription(desc, mult);
            }
            lines.Add(desc);
        }

        if (!string.IsNullOrEmpty(skill.ParentId))
            lines.Add("Требует родительский навык");

        // Статус: неизучен / можно улучшить / максимум
        if (!skill.Learned)
            lines.Add($"ПКМ — изучить ({skill.SkillPointCost} оч.)");
        else if (skill.Rank < skill.MaxRank)
        {
            string hint = $"ПКМ — улучшить ({skill.SkillPointCost} оч.)";
            if (!isPassive)
            {
                double nextDmg = skill.DamageMultiplier * (1.0 + skill.Rank * 0.12);
                int nextCd = (int)(skill.CooldownMs * (1.0 - skill.Rank * 0.08)) / 1000;
                hint += $"  →  x{nextDmg:F2}  КД {nextCd}с";
            }
            lines.Add(hint);
        }
        else
            lines.Add("Максимальный ранг");

        int maxW = 260;
        int pad = 8;
        int lineH = 18;

        var wrapped = new List<(string text, Color color)>();
        for (int i = 0; i < lines.Count; i++)
        {
            Color color = i == 0 || i == lines.Count - 1 ? new Color(230, 220, 140) : Color.White;
            string text = lines[i];
            if (font.MeasureString(text).X <= maxW - pad * 2)
            {
                wrapped.Add((text, color));
            }
            else
            {
                var words = text.Split(' ');
                string current = "";
                foreach (var w in words)
                {
                    string test = string.IsNullOrEmpty(current) ? w : current + " " + w;
                    if (font.MeasureString(test).X > maxW - pad * 2 && !string.IsNullOrEmpty(current))
                    {
                        wrapped.Add((current, color));
                        current = w;
                    }
                    else
                    {
                        current = test;
                    }
                }
                if (!string.IsNullOrEmpty(current))
                    wrapped.Add((current, color));
            }
        }

        int ww = maxW;
        int th = wrapped.Count * lineH + pad * 2;
        int tx = mouse.X + 16;
        int ty = mouse.Y + 16;
        var g = GameMain.Instance?.Graphics;
        if (g != null)
        {
            if (tx + ww > g.PreferredBackBufferWidth) tx = g.PreferredBackBufferWidth - ww - 4;
            if (ty + th > g.PreferredBackBufferHeight) ty = g.PreferredBackBufferHeight - th - 4;
        }

        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, th), new Color(20, 22, 30, 235));
        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, 2), new Color(90, 150, 220));
        for (int i = 0; i < wrapped.Count; i++)
        {
            sb.DrawString(font, wrapped[i].text, new Vector2(tx + pad, ty + pad + i * lineH), wrapped[i].color);
        }
    }

    // Заменяет все числа с % в описании, умножая на ранговый коэффициент.
    private static string RankAdjustDescription(string desc, double mult)
    {
        if (Math.Abs(mult - 1.0) < 0.001) return desc;
        return Regex.Replace(desc, @"(\d+(?:[.,]\d+)?)\s*%", m =>
        {
            if (double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                return $"{val * mult:F0}%";
            return m.Value;
        });
    }

    private static void DrawRect(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
        => UIHelper.DrawRectOutline(sb, rect, color, thickness);
}
