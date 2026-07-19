using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;

namespace RPGGame.ClientMonoGame.Windows;

public class SkillsWindow : GameWindow
{
    private List<ClientSkillInfo> _skills = new();
    private int _playerLevel = 1;
    private MouseState _prevMouse;
    private KeyboardState _prevKey;
    private NodeLayout? _hoverNode;

    private const int HeaderH = 24;
    private const int NodeW = 150;
    private const int NodeH = 56;
    private const int NodeGapY = 26;
    private const int ColGapX = 16;
    private const int BranchHeaderH = 22;

    // Школы/ветки (колонки дерева). Ключ — Type навыка.
    private class Branch
    {
        public string Name = "";
        public List<ClientSkillInfo> Nodes = new();
    }

    private List<Branch> _branches = new();

    public Action<string>? UseSkill { get; set; }
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

    public void UpdateData(List<ClientSkillInfo> skills)
    {
        _skills = skills ?? new();
        RebuildTree();
    }

    private void RebuildTree()
    {
        _branches = new();
        var byType = new Dictionary<string, Branch>();
        foreach (var s in _skills)
        {
            string key = string.IsNullOrWhiteSpace(s.Type) ? "Основные" : s.Type;
            if (!byType.TryGetValue(key, out var b))
            {
                b = new Branch { Name = key };
                byType[key] = b;
                _branches.Add(b);
            }
            b.Nodes.Add(s);
        }
        // Сортируем ветки и узлы внутри (по тиру, затем по мин. уровню)
        _branches.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        foreach (var b in _branches)
            b.Nodes.Sort((a, c) => a.Tier != c.Tier ? a.Tier.CompareTo(c.Tier) : a.MinLevel.CompareTo(c.MinLevel));
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible)
        {
            if (_dragNode != null)
            {
                _dragNode = null;
                SkillDragStateChanged?.Invoke(null!);
                SkillDragEnded?.Invoke();
            }
            _prevMouse = mouse;
            _prevKey = keyboard;
            return;
        }

        bool pressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        var (nodes, _) = Layout(mouse);

        if (pressed)
        {
            foreach (var n in nodes)
            {
                if (n.Rect.Contains(mouse.X, mouse.Y) && n.Available)
                {
                    _dragNode = n;
                    _dragStart = new Point(mouse.X, mouse.Y);
                    SkillDragStateChanged?.Invoke(n.Skill);
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

        base.Update(gameTime, keyboard, mouse);
        _prevMouse = mouse;
        _prevKey = keyboard;
    }

    // Вычисляет позиции всех узлов дерева. Возвращает список узлов и карту Id->узел.
    private (List<NodeLayout> nodes, Dictionary<string, NodeLayout> byId) Layout(MouseState mouse)
    {
        var nodes = new List<NodeLayout>();
        var byId = new Dictionary<string, NodeLayout>();

        int startX = ContentX;
        int startY = ContentY + HeaderH;

        int colX = startX;
        foreach (var branch in _branches)
        {
            int y = startY + BranchHeaderH;
            // заголовок ветки (для замера ширины)
            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            int colW = NodeW;
            if (font != null)
            {
                var hs = font.MeasureString(branch.Name);
                colW = Math.Max(NodeW, (int)hs.X + 16);
            }

            foreach (var skill in branch.Nodes)
            {
                var rect = new Rectangle(colX, y, NodeW, NodeH);
                bool available = skill.MinLevel <= _playerLevel;
                var nl = new NodeLayout { Skill = skill, Rect = rect, Available = available, Branch = branch.Name };
                nodes.Add(nl);
                if (!byId.ContainsKey(skill.Id)) byId[skill.Id] = nl;
                y += NodeH + NodeGapY;
            }

            colX += colW + ColGapX;
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

        if (_skills.Count == 0)
        {
            DrawText(sb, "Нет изученных навыков.", cx + cw / 2 - (int)(font.MeasureString("Нет изученных навыков.").X / 2),
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
                int y1 = n.ParentRect.Bottom;
                int x2 = n.Rect.X + n.Rect.Width / 2;
                int y2 = n.Rect.Y;
                Color lineCol = n.Available ? new Color(90, 130, 200) : new Color(70, 70, 80);
                sb.Draw(SpriteCache.Pixel, new Rectangle(x1 - 1, y1, 2, y2 - y1), lineCol);
                sb.Draw(SpriteCache.Pixel, new Rectangle(Math.Min(x1, x2), y2 - 1, Math.Abs(x2 - x1) + 2, 2), lineCol);
                sb.Draw(SpriteCache.Pixel, new Rectangle(x2 - 1, y2 - 6, 2, 6), lineCol);
            }
        }

        // Заголовки веток
        int colX = cx;
        foreach (var branch in _branches)
        {
            int colW = NodeW;
            var hs = font.MeasureString(branch.Name);
            colW = Math.Max(NodeW, (int)hs.X + 16);
            DrawText(sb, branch.Name, colX, cy + HeaderH, new Color(180, 180, 200));
            colX += colW + ColGapX;
        }

        // Узлы
        _hoverNode = null;
        foreach (var n in nodes)
        {
            if (n.Rect.Contains(mouse.X, mouse.Y)) _hoverNode = n;

            var skill = n.Skill;
            bool hover = n.Rect.Contains(mouse.X, mouse.Y);
            Color bg = !n.Available ? new Color(34, 34, 40)
                      : hover ? new Color(60, 70, 95)
                      : new Color(44, 48, 64);
            sb.Draw(SpriteCache.Pixel, n.Rect, bg);
            sb.Draw(SpriteCache.Pixel, new Rectangle(n.Rect.X, n.Rect.Y, n.Rect.Width, 2),
                n.Available ? new Color(90, 150, 220) : new Color(70, 70, 80));

            // Рамка выделения при наведении
            if (hover)
                DrawRect(sb, n.Rect, new Color(150, 200, 255), 2);

            // Иконка (по типу или дефолтная)
            var spr = !string.IsNullOrEmpty(skill.IconName) ? SpriteCache.Get(skill.IconName)
                      : SpriteCache.ForItemType(skill.Type);
            if (spr != null)
                sb.Draw(spr, new Rectangle(n.Rect.X + 6, n.Rect.Y + 8, 28, 28), Color.White);

            Color nameColor = n.Available ? new Color(150, 190, 255) : new Color(110, 110, 120);
            DrawText(sb, skill.Name, n.Rect.X + 40, n.Rect.Y + 6, nameColor);

            DrawText(sb, $"Тир {skill.Tier}", n.Rect.X + 40, n.Rect.Y + 24, new Color(160, 160, 175));
            DrawText(sb, $"МП {skill.MpCost}  КД {skill.CooldownMs}мс", n.Rect.X + 6, n.Rect.Y + 40,
                n.Available ? new Color(120, 200, 130) : new Color(90, 110, 95));

            if (!n.Available)
                DrawText(sb, $"нужен ур. {skill.MinLevel}", n.Rect.X + 40, n.Rect.Y + 24 + 14, new Color(200, 120, 120));
        }

        // Tooltip при наведении
        if (_hoverNode != null)
            DrawTooltip(sb, _hoverNode.Skill, mouse);
    }

    private void DrawTooltip(SpriteBatch sb, ClientSkillInfo skill, MouseState mouse)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        var lines = new List<string>
        {
            skill.Name,
            $"Тир {skill.Tier}  |  {(string.IsNullOrWhiteSpace(skill.Type) ? "Основные" : skill.Type)}",
            $"МП: {skill.MpCost}   КД: {skill.CooldownMs} мс   x{skill.DamageMultiplier:F1}",
            $"Мин. уровень: {skill.MinLevel}"
        };
        if (!string.IsNullOrEmpty(skill.Description))
            lines.Add(skill.Description);
        if (!string.IsNullOrEmpty(skill.ParentId))
            lines.Add("Требует родительский навык");

        int pad = 8;
        float tw = 0;
        foreach (var l in lines) tw = Math.Max(tw, font.MeasureString(l).X);
        int th = lines.Count * 18 + pad * 2;
        int tx = mouse.X + 16;
        int ty = mouse.Y + 16;
        int ww = (int)tw + pad * 2;
        var g = GameMain.Instance?.Graphics;
        if (g != null)
        {
            if (tx + ww > g.PreferredBackBufferWidth) tx = g.PreferredBackBufferWidth - ww - 4;
            if (ty + th > g.PreferredBackBufferHeight) ty = g.PreferredBackBufferHeight - th - 4;
        }

        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, th), new Color(20, 22, 30, 235));
        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, 2), new Color(90, 150, 220));
        for (int i = 0; i < lines.Count; i++)
        {
            var color = i == 0 ? new Color(230, 220, 140) : Color.White;
            sb.DrawString(font, lines[i], new Vector2(tx + pad, ty + pad + i * 18), color);
        }
    }

    private static void DrawRect(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }
}
