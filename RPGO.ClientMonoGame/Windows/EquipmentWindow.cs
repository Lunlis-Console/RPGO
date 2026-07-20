using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Windows;

public class EquipmentWindow : GameWindow
{
    private EquipmentData? _data;

    public Action<string>? UnequipItem; // slot id
    public Action? CloseRequested;

    // Тип предмета, который сейчас перетаскивается (для подсветки слотов)
    public string? DraggingType { get; set; }

    // Источник перетаскивания из этого окна (для глобального оверлея)
    public Action<Item?>? DragStateChanged;

    // true, если точка находится над окном инвентаря (для дропа "снять")
    public Func<Point, bool>? IsOverInventory;

    private Rectangle[] _rowRects = Array.Empty<Rectangle>();
    private Rectangle _closeRect;
    private Item? _hoverItem;

    private MouseState _prevMouseLocal;

    // Состояние перетаскивания ПРЕДМЕТА ИЗ СЛОТА (для снятия drag-n-drop)
    private string? _dragSlotId;
    private Item? _dragItem;
    private Point _dragStart;
    private bool _dragging;

    private const int Cols = 3;
    private const int Gap = 5;
    private const int DragThreshold = 6;

    public EquipmentWindow()
    {
        Title = "Снаряжение";
        Width = 306;
        Height = 459;
    }

    public void UpdateData(EquipmentData data) => _data = data;

    private int CellSize()
    {
        int cw = ContentW;
        return (cw - (Cols - 1) * Gap) / Cols;
    }

    private void ComputeLayout()
    {
        int count = EquipmentSlots.All.Count;
        var cells = new Rectangle[count];
        int cell = CellSize();
        int cx = ContentX;
        int top = ContentY + 4;
        for (int i = 0; i < count; i++)
        {
            int r = i / Cols, c = i % Cols;
            int x = cx + c * (cell + Gap);
            int y = top + r * (cell + Gap);
            cells[i] = new Rectangle(x, y, cell, cell);
        }
        _rowRects = cells;
        _closeRect = new Rectangle(ContentX, Y + Height - 26, ContentW, 22);
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible)
        {
            _prevMouseLocal = mouse;
            return;
        }

        ComputeLayout();

        bool leftPressed = mouse.LeftButton == ButtonState.Pressed && _prevMouseLocal.LeftButton == ButtonState.Released;
        bool rightPressed = mouse.RightButton == ButtonState.Pressed && _prevMouseLocal.RightButton == ButtonState.Released;
        bool leftReleased = mouse.LeftButton == ButtonState.Released && _prevMouseLocal.LeftButton == ButtonState.Pressed;

        // Кнопка "Закрыть"
        if (leftPressed && _closeRect.Contains(mouse.X, mouse.Y))
        {
            Visible = false;
            CloseRequested?.Invoke();
            _prevMouseLocal = mouse;
            return;
        }

        // Правая кнопка по надетому слоту — снять
        if (rightPressed && _data != null)
        {
            for (int i = 0; i < _rowRects.Length; i++)
            {
                if (_rowRects[i].Contains(mouse.X, mouse.Y))
                {
                    var slot = EquipmentSlots.All[i];
                    if (_data.Slots.TryGetValue(slot.Id, out var it) && it != null)
                        UnequipItem?.Invoke(slot.Id);
                    break;
                }
            }
        }

        // Левая кнопка по надетому слоту — начало перетаскивания (снятие drag-n-drop)
        if (leftPressed && _data != null && _dragSlotId == null)
        {
            for (int i = 0; i < _rowRects.Length; i++)
            {
                if (_rowRects[i].Contains(mouse.X, mouse.Y))
                {
                    var slot = EquipmentSlots.All[i];
                    if (_data.Slots.TryGetValue(slot.Id, out var it) && it != null)
                    {
                        _dragSlotId = slot.Id;
                        _dragItem = it;
                        _dragStart = new Point(mouse.X, mouse.Y);
                        _dragging = false;
                    }
                    break;
                }
            }
        }

        // Движение перетаскивания
        if (_dragSlotId != null && mouse.LeftButton == ButtonState.Pressed)
        {
            int moved = Math.Abs(mouse.X - _dragStart.X) + Math.Abs(mouse.Y - _dragStart.Y);
            if (!_dragging && moved >= DragThreshold)
            {
                _dragging = true;
                DragStateChanged?.Invoke(_dragItem); // поднимаем оверлей + подсветку
            }
        }

        // Отпускание — дроп на инвентарь = снять
        if (leftReleased && _dragSlotId != null)
        {
            if (_dragging)
            {
                var pt = new Point(mouse.X, mouse.Y);
                if (IsOverInventory?.Invoke(pt) == true)
                    UnequipItem?.Invoke(_dragSlotId);
                DragStateChanged?.Invoke(null); // гасим оверлей/подсветку
            }
            _dragSlotId = null;
            _dragItem = null;
            _dragging = false;
        }

        base.Update(gameTime, keyboard, mouse);
        _prevMouseLocal = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        base.Draw(sb, Mouse.GetState());

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;
        var mouse = Mouse.GetState();

        ComputeLayout();

        bool dragging = DraggingType != null;
        var validTargets = dragging
            ? new HashSet<string>(EquipmentSlots.SlotsForItemType(DraggingType))
            : null;

        _hoverItem = null;

        for (int i = 0; i < _rowRects.Length; i++)
        {
            var slot = EquipmentSlots.All[i];
            var r = _rowRects[i];

            // Фон ячейки (тусклый)
            sb.Draw(SpriteCache.Pixel, r, new Color(30, 32, 40));
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, 1), new Color(55, 60, 72));
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, 1, r.Height), new Color(55, 60, 72));

            Item? it = null;
            bool filled = _data != null && _data.Slots.TryGetValue(slot.Id, out it) && it != null;

            if (filled && it != null)
            {
                // Надетый предмет — иконка строго по центру (подпись слота скрыта)
                var spr = SpriteCache.ForItemType(it.Type);
                if (spr != null)
                    sb.Draw(spr, new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12), Color.White);

                // Тултип при наведении (когда не тащим)
                if (!_dragging && _dragSlotId == null && r.Contains(mouse.X, mouse.Y))
                    _hoverItem = it;
            }
            else
            {
                // Пустой слот — название слота по центру ячейки (тускло, с переносом)
                var lines = WrapText(font, slot.NameRu, r.Width - 8);
                int ly = r.Y + (r.Height - lines.Count * (int)font.LineSpacing) / 2;
                foreach (var line in lines)
                {
                    var sz = font.MeasureString(line);
                    sb.DrawString(font, line, new Vector2(r.X + (r.Width - sz.X) / 2, ly), new Color(95, 100, 115));
                    ly += (int)font.LineSpacing;
                }
            }

            // Подсветка допустимой цели при перетаскивании
            if (dragging && validTargets != null && validTargets.Contains(slot.Id))
            {
                bool over = r.Contains(mouse.X, mouse.Y);
                var border = over ? new Color(120, 220, 120) : new Color(70, 120, 70);
                sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, 2), border);
                sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y + r.Height - 2, r.Width, 2), border);
                sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, 2, r.Height), border);
                sb.Draw(SpriteCache.Pixel, new Rectangle(r.X + r.Width - 2, r.Y, 2, r.Height), border);
                if (over)
                    sb.Draw(SpriteCache.Pixel, r, new Color(60, 120, 60, 70));
            }
        }

        DrawButton(sb, "Закрыть", _closeRect, mouse);

        if (_hoverItem != null)
            DrawTooltip(sb, _hoverItem, mouse);
    }

    public bool TryGetSlotAt(Point p, string? itemType, out string? slotId)
    {
        ComputeLayout();
        slotId = null;
        var t = itemType ?? DraggingType;
        if (string.IsNullOrEmpty(t)) return false;
        for (int i = 0; i < _rowRects.Length; i++)
        {
            if (_rowRects[i].Contains(p))
            {
                var slot = EquipmentSlots.All[i];
                if (EquipmentSlots.SlotsForItemType(t).Contains(slot.Id))
                {
                    slotId = slot.Id;
                    return true;
                }
                return false;
            }
        }
        return false;
    }

    private static List<string> WrapText(SpriteFont font, string text, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ');
            var cur = "";
            foreach (var w in words)
            {
                var test = cur.Length == 0 ? w : cur + " " + w;
                if (font.MeasureString(test).X > maxWidth && cur.Length > 0)
                {
                    lines.Add(cur);
                    cur = w;
                }
                else
                {
                    cur = test;
                }
            }
            if (cur.Length > 0) lines.Add(cur);
        }
        return lines;
    }

    private void DrawButton(SpriteBatch sb, string text, Rectangle r, MouseState mouse)
    {
        bool hover = r.Contains(mouse.X, mouse.Y);
        var bg = new Color(60, 80, 120);
        sb.Draw(SpriteCache.Pixel, r, hover ? Color.Lerp(bg, Color.White, 0.15f) : bg);
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font != null)
        {
            var sz = font.MeasureString(text);
            sb.DrawString(font, text, new Vector2(r.X + (r.Width - sz.X) / 2, r.Y + (r.Height - sz.Y) / 2), Color.White);
        }
    }

    private void DrawTooltip(SpriteBatch sb, Item item, MouseState mouse)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        var lines = new List<string>
        {
            item.Name,
            $"Тип: {TypeLabel(item.Type)}",
            $"Цена: {item.Value} золота"
        };
        if (item.Attack > 0) lines.Add($"Атака: +{item.Attack}");
        if (item.Defense > 0) lines.Add($"Защита: +{item.Defense}");
        if (item.MaxHealthBonus > 0) lines.Add($"Здоровье: +{item.MaxHealthBonus}");
        if (item.HealAmount > 0) lines.Add($"Лечение: +{item.HealAmount}");
        if (!string.IsNullOrEmpty(item.Description))
            lines.Add(item.Description);

        int pad = 8;
        float tw = 0;
        foreach (var l in lines) tw = Math.Max(tw, font.MeasureString(l).X);
        int th = lines.Count * 18 + pad * 2;
        int tx = mouse.X + 16;
        int ty = mouse.Y + 16;
        int ww = (int)tw + pad * 2;
        if (tx + ww > GameMain.Instance!.Graphics.PreferredBackBufferWidth)
            tx = GameMain.Instance!.Graphics.PreferredBackBufferWidth - ww - 4;
        if (ty + th > GameMain.Instance!.Graphics.PreferredBackBufferHeight)
            ty = GameMain.Instance!.Graphics.PreferredBackBufferHeight - th - 4;

        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, th), new Color(20, 22, 30, 230));
        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, 2), new Color(80, 120, 200));
        for (int i = 0; i < lines.Count; i++)
        {
            var color = i == 0 ? new Color(230, 220, 140) : Color.White;
            sb.DrawString(font, lines[i], new Vector2(tx + pad, ty + pad + i * 18), color);
        }
    }

    private static string TypeLabel(string t) => t switch
    {
        "weapon" => "Оружие",
        "twohand" => "Двуручное оружие",
        "shield" => "Щит",
        "helmet" => "Шлем",
        "cloak" => "Плащ",
        "chest" => "Нагрудник",
        "legs" => "Поножи",
        "boots" => "Сапоги",
        "glove_r" => "Правая перчатка",
        "glove_l" => "Левая перчатка",
        "necklace" => "Ожерелье",
        "ring" => "Кольцо",
        "accessory" => "Аксессуар",
        "armor" => "Броня",
        "consumable" => "Расходник",
        "collectible" => "Коллекция",
        "material" => "Материал",
        "trophy" => "Трофей",
        _ => t
    };
}
