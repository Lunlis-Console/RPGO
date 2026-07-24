using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Windows;

public class InventoryWindow : GameWindow
{
    private InventoryData? _data;
    private string _filter = "all";

    public Action<string>? EquipItem;
    public Action<string>? UseItem;
    public Action<string>? DeleteItem;
    public Action? SortItems;
    public Action<string, int>? SellItem;

    public override bool IsDragging => _dragIndex >= 0;

    // Возвращает true, если перетаскивание завершилось успешным надеванием
    // (вызывается при отпускании перетаскиваемого предмета вне сетки/корзины)
    public Func<Point, Item, bool>? DropOnEquip;

    // Возвращает true, если предмет брошен в окно торговца (продажа)
    public Func<Point, Item, bool>? DropOnSell;

    // Уведомляет о начале/конце перетаскивания (item == null означает конец)
    public Action<Item?>? DragStateChanged;

    // true, когда открыт режим торговли с NPC
    public bool ShopMode { get; set; }

    // Запрос на продажу стакающегося предмета (показать диалог количества)
    public Action<Item, int>? PendingSell;

    // Запрос на выброс стакающегося предмета (показать диалог количества)
    public Action<Item, int>? PendingDrop;

    private int _lastClickIdx = -1;
    private TimeSpan _lastClickTime;

    private MouseState _prevMouse;
    private KeyboardState _prevKey;

    private const int GridCols = 10;
    private const int GridRows = 10;
    private const int TabH = 24;
    private const int BottomH = 40;

    private Rectangle[] _tabRects = Array.Empty<Rectangle>();
    private Rectangle[,] _slotRects = new Rectangle[GridCols, GridRows];
    private Rectangle _sortRect;
    private Rectangle _trashRect;
    private Item? _hoverItem;

    // Стек displayed предметов: Item-шаблон + количество
    private List<(Item item, int count)> _stacks = new();

    // Drag'n'drop
    private int _dragIndex = -1;
    private Point _dragOffset;
    private Point _dragPos;
    private Point _dragStart;

    // Подтверждение удаления
    private (Item item, int count)? _confirm;
    private Rectangle _confirmYes, _confirmNo;

    public InventoryWindow()
    {
        Title = "Инвентарь";
        Width = 480;
        Height = 600;
    }

    public void UpdateData(InventoryData data) => _data = data;

    private string[] Filters => new[] { "all", "equipment", "consumable", "material" };
    private string[] FilterLabels => new[] { "Все", "Экип.", "Расх.", "Мат." };

    private bool MatchesFilter(Item i) => _filter switch
    {
        "equipment" => EquipmentSlots.IsEquippableType(i.Type),
        "consumable" => i.Type == "consumable",
        "material" => i.Type is "material" or "collectible" or "trophy",
        _ => true
    };

    private List<(Item item, int count)> BuildStacks()
    {
        var result = new List<(Item, int)>();
        if (_data?.Items == null) return result;
        var items = _data.Items.Where(MatchesFilter).ToList();
        foreach (var it in items)
        {
            int qty = Math.Max(1, it.Quantity);
            if (IsStackable(it))
            {
                int idx = result.FindIndex(s => SameItem(s.Item1, it));
                if (idx >= 0)
                {
                    result[idx] = (result[idx].Item1, result[idx].Item2 + qty);
                    continue;
                }
                result.Add((it, qty));
            }
            else
            {
                // Нестакаемые предметы (экипировка) — каждый экземпляр в отдельной ячейке,
                // без объединения в стек (даже если Quantity > 1).
                for (int k = 0; k < qty; k++)
                    result.Add((it, 1));
            }
        }
        return result;
    }

    // Стакаются только расходники/ресурсы (как на сервере в Balance.MaxStackForType).
    private static bool IsStackable(Item it) =>
        it.Type is "consumable" or "collectible" or "trophy" or "material";

    private static bool SameItem(Item a, Item b) =>
        a.Name == b.Name && a.Type == b.Type &&
        a.BonusPhysAttack == b.BonusPhysAttack && a.BonusDefense == b.BonusDefense &&
        a.MaxHealthBonus == b.MaxHealthBonus && a.HealAmount == b.HealAmount &&
        a.Value == b.Value && a.Description == b.Description;

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible || _data == null)
        {
            _prevMouse = mouse;
            _prevKey = keyboard;
            return;
        }

        bool down = mouse.LeftButton == ButtonState.Pressed;
        bool up = mouse.LeftButton == ButtonState.Released;
        bool pressed = down && _prevMouse.LeftButton == ButtonState.Released;
        bool released = up && _prevMouse.LeftButton == ButtonState.Pressed;

        // Подтверждение удаления перехватывает клики
        if (_confirm.HasValue)
        {
            if (pressed)
            {
                if (_confirmYes.Contains(mouse.X, mouse.Y))
                {
                    DeleteItem?.Invoke(_confirm.Value.item.Id);
                    _confirm = null;
                }
                else if (_confirmNo.Contains(mouse.X, mouse.Y))
                {
                    _confirm = null;
                }
            }
            base.Update(gameTime, keyboard, mouse);
            _prevMouse = mouse;
            _prevKey = keyboard;
            return;
        }

        int cx = ContentX, cy = ContentY, cw = ContentW;
        _stacks = BuildStacks();
        ComputeTabRects();

        // Вкладки
        for (int i = 0; i < 4; i++)
            if (pressed && _tabRects[i].Contains(mouse.X, mouse.Y))
                _filter = Filters[i];

        // Слоты
        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                int idx = r * GridCols + c;
                var rect = _slotRects[c, r];
                if (!rect.Contains(mouse.X, mouse.Y)) continue;

                if (pressed && idx < _stacks.Count)
                {
                    _dragIndex = idx;
                    _dragStart = new Point(mouse.X, mouse.Y);
                    _dragOffset = new Point(mouse.X - rect.X, mouse.Y - rect.Y);
                    _dragPos = new Point(mouse.X, mouse.Y);
                    DragStateChanged?.Invoke(_stacks[idx].item);
                }
            }
        }

        if (down && _dragIndex >= 0)
            _dragPos = new Point(mouse.X, mouse.Y);

        if (released && _dragIndex >= 0)
        {
            int idx = _dragIndex;
            _dragIndex = -1;
            var moved = Math.Abs(mouse.X - _dragStart.X) + Math.Abs(mouse.Y - _dragStart.Y);

            if (_trashRect.Contains(mouse.X, mouse.Y))
            {
                var item = _stacks[idx].item;
                if (item.Quantity > 1 && item.MaxStack > 1)
                    PendingDrop?.Invoke(item, item.Quantity);
                else
                    _confirm = (item, item.Quantity);
            }
            else if (moved >= 6 && idx < _stacks.Count)
            {
                // Перетаскивание — пробуем продать в магазин или надеть на слот снаряжения
                var item = _stacks[idx].item;
                if (ShopMode && IsStackable(item) && item.Quantity > 1)
                {
                    PendingSell?.Invoke(item, item.Quantity);
                }
                else
                {
                    bool handled = DropOnSell?.Invoke(new Point(mouse.X, mouse.Y), item) ?? false;
                    if (!handled)
                        DropOnEquip?.Invoke(new Point(mouse.X, mouse.Y), item);
                }
                // иначе — возврат в инвентарь (ничего не делаем)
            }
                else if (moved < 6 && idx < _stacks.Count)
                {
                    // Клик (без перетаскивания)
                    var item = _stacks[idx].item;
                    if (ShopMode)
                    {
                        // В режиме торговли одиночный клик ничего не делает;
                        // двойной клик — продажа
                        HandleShopClick(item, mouse);
                    }
                    else
                    {
                        // Надевание/использование — только по двойному клику,
                        // чтобы не мешать drag-n-drop
                        HandleInventoryClick(item);
                    }
                }

                // Сброс состояния перетаскивания — ПОСЛЕ всех действий дропа,
                // иначе DraggingType обнуляется раньше времени и TryGetSlotAt не сработает.
                DragStateChanged?.Invoke(null);
        }

        // Правая кнопка мыши — мгновенное надевание/использование
        bool rightPressed = mouse.RightButton == ButtonState.Pressed
            && _prevMouse.RightButton == ButtonState.Released;
        if (rightPressed && _dragIndex < 0)
        {
            for (int r = 0; r < GridRows && rightPressed; r++)
            {
                for (int c = 0; c < GridCols && rightPressed; c++)
                {
                    int idx = r * GridCols + c;
                    if (idx >= _stacks.Count) continue;
                    if (!_slotRects[c, r].Contains(mouse.X, mouse.Y)) continue;

                    var item = _stacks[idx].item;
                    if (ShopMode)
                        RequestSell(item);
                    else if (EquipmentSlots.IsEquippableType(item.Type))
                        EquipItem?.Invoke(item.Id);
                    else if (item.Type == "consumable" && item.HealAmount > 0)
                        UseItem?.Invoke(item.Id);
                    rightPressed = false;
                }
            }
        }

        // Кнопка сортировки
        if (pressed && _sortRect.Contains(mouse.X, mouse.Y))
            SortItems?.Invoke();

        base.Update(gameTime, keyboard, mouse);
        _prevMouse = mouse;
        _prevKey = keyboard;
    }

    private void HandleInventoryClick(Item item)
    {
        int idx = _stacks.FindIndex(s => s.item == item);
        var now = DateTime.Now.TimeOfDay;
        bool isDouble = idx == _lastClickIdx && (_lastClickTime - now).TotalMilliseconds is > -500 and < 500;
        _lastClickIdx = idx;
        _lastClickTime = now;
        if (!isDouble) return;

        _lastClickIdx = -1;
        if (EquipmentSlots.IsEquippableType(item.Type))
            EquipItem?.Invoke(item.Id);
        else if (item.Type == "consumable" && item.HealAmount > 0)
            UseItem?.Invoke(item.Id);
    }

    private void HandleShopClick(Item item, MouseState mouse)
    {
        int idx = _stacks.FindIndex(s => s.item == item);
        var now = DateTime.Now.TimeOfDay;
        bool isDouble = idx == _lastClickIdx && (_lastClickTime - now).TotalMilliseconds is > -500 and < 500;
        _lastClickIdx = idx;
        _lastClickTime = now;
        if (isDouble)
        {
            _lastClickIdx = -1;
            RequestSell(item);
        }
    }

    private void RequestSell(Item item)
    {
        if (item.Quantity > 1)
            PendingSell?.Invoke(item, item.Quantity);
        else
            SellItem?.Invoke(item.Id, 1);
    }

    private void ComputeTabRects()
    {
        int cx = ContentX, cw = ContentW;
        int btnW = cw / 4 - 2;
        _tabRects = new Rectangle[4];
        for (int i = 0; i < 4; i++)
            _tabRects[i] = new Rectangle(cx + i * (btnW + 2), ContentY, btnW, TabH);
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible || _data == null) return;
        var mouse = Mouse.GetState();
        base.Draw(sb, mouse);

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int cx = ContentX, cy = ContentY, cw = ContentW;

        // Вкладки
        ComputeTabRects();
        for (int i = 0; i < 4; i++)
        {
            var r = _tabRects[i];
            bool active = _filter == Filters[i];
            sb.Draw(SpriteCache.Pixel, r, active ? new Color(80, 120, 200) : new Color(50, 55, 65));
            var sz = font.MeasureString(FilterLabels[i]);
            sb.DrawString(font, FilterLabels[i], new Vector2(r.X + (r.Width - sz.X) / 2, r.Y + 3), Color.White);
        }

        // Сетка
        int gridTop = cy + TabH + 8;
        int gridAreaH = Height - TabH - 8 - BottomH - 12 - 40;
        int cell = (cw - (GridCols - 1) * 4) / GridCols;
        cell = Math.Min(cell, (gridAreaH - (GridRows - 1) * 4) / GridRows);
        int gridW = GridCols * cell + (GridCols - 1) * 4;
        int gridX = cx + (cw - gridW) / 2;

        _stacks = BuildStacks();
        _hoverItem = null;
        int count = _stacks.Count;

        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                int x = gridX + c * (cell + 4);
                int y = gridTop + r * (cell + 4);
                var rect = new Rectangle(x, y, cell, cell);
                _slotRects[c, r] = rect;

                bool filled = r * GridCols + c < count;
                bool hover = rect.Contains(mouse.X, mouse.Y) && _dragIndex < 0;
                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(55, 60, 80) : new Color(35, 38, 48));
                sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(60, 65, 80));

                if (filled && !(r * GridCols + c == _dragIndex))
                {
                    var stack = _stacks[r * GridCols + c];
                    var spr = SpriteCache.ForItemType(stack.item.Type);
                    if (spr != null)
                        sb.Draw(spr, new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8), Color.White);
                    if (stack.count > 1)
                        DrawText(sb, stack.count.ToString(), rect.X + rect.Width - 16, rect.Y + rect.Height - 16, new Color(230, 230, 120));
                    if (hover) _hoverItem = stack.item;
                }
            }
        }

        DrawText(sb, $"Золото: {_data.Gold}", cx, gridTop + gridAreaH - 16, new Color(220, 200, 80));

        // Нижняя панель: сортировка + зона корзины
        int bottomY = Y + Height - BottomH - 6;
        int sortW = cw / 2 - 4;
        _sortRect = new Rectangle(cx, bottomY, sortW, BottomH);
        DrawButton(sb, "Сортировать", _sortRect, mouse);

        _trashRect = new Rectangle(cx + sortW + 8, bottomY, sortW, BottomH);
        bool overTrash = _trashRect.Contains(mouse.X, mouse.Y);
        sb.Draw(SpriteCache.Pixel, _trashRect, overTrash ? new Color(150, 70, 70) : new Color(70, 50, 50));
        sb.Draw(SpriteCache.Pixel, new Rectangle(_trashRect.X, _trashRect.Y, _trashRect.Width, 2), new Color(120, 90, 90));
        DrawTrashIcon(sb, _trashRect, overTrash);
        var basketLabel = "Корзина";
        var basketSize = font.MeasureString(basketLabel);
        DrawText(sb, basketLabel, _trashRect.X + (_trashRect.Width - (int)basketSize.X) / 2, _trashRect.Y + _trashRect.Height - 16, Color.White);

        // Перетаскиваемый предмет рисуется глобально поверх всех окон (GameScreen),
        // здесь исходная ячейка просто остаётся пустой, чтобы не дублировать картинку.

        // Тултип
        if (_hoverItem != null)
            DrawTooltip(sb, _hoverItem, mouse);

        // Окно подтверждения
        if (_confirm.HasValue)
            DrawConfirm(sb, _confirm.Value, mouse);
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

    private void DrawTrashIcon(SpriteBatch sb, Rectangle zone, bool hot)
    {
        int w = 18, h = 20;
        int ix = zone.X + zone.Width / 2 - w / 2;
        int iy = zone.Y + 6;
        Color col = hot ? new Color(255, 220, 220) : new Color(200, 180, 180);
        // Крышка
        sb.Draw(SpriteCache.Pixel, new Rectangle(ix - 2, iy, w + 4, 3), col);
        sb.Draw(SpriteCache.Pixel, new Rectangle(ix + 2, iy - 3, 6, 3), col);
        // Тело
        sb.Draw(SpriteCache.Pixel, new Rectangle(ix, iy + 4, w, h - 4), col);
        // Полосы
        sb.Draw(SpriteCache.Pixel, new Rectangle(ix + 4, iy + 7, 2, h - 9), Color.Black * 0.4f);
        sb.Draw(SpriteCache.Pixel, new Rectangle(ix + 9, iy + 7, 2, h - 9), Color.Black * 0.4f);
        sb.Draw(SpriteCache.Pixel, new Rectangle(ix + 13, iy + 7, 2, h - 9), Color.Black * 0.4f);
    }

    private void DrawConfirm(SpriteBatch sb, (Item item, int count) confirm, MouseState mouse)
    {
        var g = GameMain.Instance!.Graphics;
        int pw = 280, ph = 120;
        int px = X + (Width - pw) / 2;
        int py = Y + (Height - ph) / 2;
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, Height), new Color(0, 0, 0, 150));
        sb.Draw(SpriteCache.Pixel, new Rectangle(px, py, pw, ph), new Color(35, 38, 48));
        sb.Draw(SpriteCache.Pixel, new Rectangle(px, py, pw, 2), new Color(200, 90, 90));

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        string text = confirm.count > 1
            ? $"Выбросить {confirm.count} x {confirm.item.Name}?"
            : $"Выбросить {confirm.item.Name}?";
        if (font != null)
            sb.DrawString(font, text, new Vector2(px + 12, py + 14), Color.White);

        int bw = (pw - 36) / 2;
        _confirmYes = new Rectangle(px + 12, py + ph - 40, bw, 28);
        _confirmNo = new Rectangle(px + 24 + bw, py + ph - 40, bw, 28);
        DrawButton(sb, "Да", _confirmYes, mouse);
        DrawButton(sb, "Нет", _confirmNo, mouse);
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
