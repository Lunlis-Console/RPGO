using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Networking;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Windows;

public class EquipmentWindow : GameWindow
{
    private EquipmentData? _data;

    public Action<string>? UnequipItem; // slot id
    public Action<string, string>? MoveToSlot; // source slot id, target slot id
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

        // Отпускание — дроп на инвентарь = снять; на другой слот = перенести
        if (leftReleased && _dragSlotId != null)
        {
            if (_dragging)
            {
                var pt = new Point(mouse.X, mouse.Y);
                if (IsOverInventory?.Invoke(pt) == true)
                    UnequipItem?.Invoke(_dragSlotId);
                else if (TryGetSlotAt(pt, _dragItem?.Type, out var target) && target != null && target != _dragSlotId)
                    MoveToSlot?.Invoke(_dragSlotId, target);
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
                var spr = SpriteCache.ForItem(it);
                if (spr != null)
                {
                    var iconRect = new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12);
                    sb.Draw(spr, iconRect, Color.White);
                    var qFrame = SpriteCache.ForQualityFrame(it.Quality);
                    if (qFrame != null)
                        sb.Draw(qFrame, iconRect, Color.White);
                }

                // Тултип при наведении (когда не тащим)
                if (!_dragging && _dragSlotId == null && r.Contains(mouse.X, mouse.Y))
                    _hoverItem = it;
            }
            else
            {
                // Пустой слот — название слота по центру ячейки (тускло, с переносом)
                var lines = UIHelper.WrapText(font, slot.NameRu, r.Width - 8);
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

            DrawButtonHover(sb, "Закрыть", _closeRect, mouse);

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

    private void DrawTooltip(SpriteBatch sb, Item item, MouseState mouse)
    {
        var lines = ItemTooltip.BuildLines(item);
        var g = GameMain.Instance;
        int wRight = g?.Graphics.PreferredBackBufferWidth ?? 1920;
        int wBottom = g?.Graphics.PreferredBackBufferHeight ?? 1080;
        TooltipRenderer.Draw(sb, lines, mouse, wRight, wBottom);
    }
}
