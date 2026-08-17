using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Windows;

/// <summary>
/// Окно личного склада: сетка как у инвентаря (10x10), тот же размер окна,
/// строка фильтров. Открывается слева от инвентаря, как магазин — предметы
/// переносятся перетаскиванием между окнами.
/// </summary>
public class StorageWindow : GameWindow
{
    private List<Item> _storageItems = new();
    private List<(Item item, int count)> _stacks = new();
    private readonly ItemFilterBar _filterBar = new();
    private int _maxSlots = 60;

    private const int GridCols = 10;
    private const int GridRows = 10;
    private const int BottomH = 40;

    private Rectangle[,] _slotRects = new Rectangle[GridCols, GridRows];
    private int _hoverIdx = -1;
    private Item? _hoverItem;

    // Drag'n'drop (перетаскивание в окно инвентаря = забрать предмет)
    private int _dragIdx = -1;
    private Point _dragOffset;
    private Point _dragPos;
    private Point _dragStart;
    private int _lastClickIdx = -1;
    private TimeSpan _lastClickTime;

    private new MouseState _prevMouse;

    public Action<string, int>? WithdrawItem { get; set; }
    public Action<Item, int>? PendingWithdraw { get; set; }
    public Action<Item?>? DragStateChanged { get; set; }
    /// <summary>Проверка, что точка над окном инвентаря (туда можно перетащить предмет).</summary>
    public Func<Point, bool>? IsOverInventory { get; set; }

    /// <summary>Поле поиска активно — Esc обрабатывает его (очистка/снятие фокуса), а не закрывает окно.</summary>
    public bool ConsumesEscape => _filterBar.ConsumesEscape;

    public override bool IsDragging => _dragIdx >= 0;

    public StorageWindow()
    {
        Title = "Склад";
        Width = 480;
        Height = 600;
        Visible = false;
    }

    public void UpdateData(List<Item>? storageItems, int maxSlots)
    {
        _storageItems = storageItems ?? new List<Item>();
        _maxSlots = maxSlots;
        Visible = true;
    }

    private static bool IsStackable(Item it) =>
        it.Type is "consumable" or "collectible" or "trophy" or "material";

    private List<(Item item, int count)> BuildStacks()
    {
        var result = new List<(Item, int)>();
        var items = _filterBar.Filter(_storageItems);
        foreach (var it in items)
        {
            int qty = Math.Max(1, it.Quantity);
            if (IsStackable(it))
            {
                int cap = it.MaxStack > 1 ? it.MaxStack : 10;
                while (qty > 0)
                {
                    int chunk = Math.Min(cap, qty);
                    result.Add((it, chunk));
                    qty -= chunk;
                }
            }
            else
            {
                for (int k = 0; k < qty; k++)
                    result.Add((it, 1));
            }
        }
        return result;
    }

    private void RequestWithdraw(Item item, int stackCount)
    {
        if (stackCount > 1)
            PendingWithdraw?.Invoke(item, stackCount);
        else
            WithdrawItem?.Invoke(item.Id, 1);
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible)
        {
            _prevMouse = mouse;
            return;
        }

        base.Update(gameTime, keyboard, mouse);

        _stacks = BuildStacks();

        int cx = ContentX, cw = ContentW;

        // Строка фильтров (общий компонент: поиск, категория, уровень, цена, сброс)
        bool filterConsumed = _filterBar.Update(mouse, keyboard, _prevMouse, new Rectangle(cx, ContentY, cw, 22));

        bool down = mouse.LeftButton == ButtonState.Pressed;
        bool pressed = down && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

        ComputeSlotRects();
        _hoverIdx = -1;
        _hoverItem = null;

        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                int idx = r * GridCols + c;
                var rect = _slotRects[c, r];
                if (!rect.Contains(mouse.X, mouse.Y)) continue;

                _hoverIdx = idx;
                if (idx < _stacks.Count) _hoverItem = _stacks[idx].item;

                if (pressed && !filterConsumed && idx < _stacks.Count)
                {
                    _dragIdx = idx;
                    _dragStart = new Point(mouse.X, mouse.Y);
                    _dragOffset = new Point(mouse.X - rect.X, mouse.Y - rect.Y);
                    _dragPos = new Point(mouse.X, mouse.Y);
                    DragStateChanged?.Invoke(_stacks[idx].item);
                }
            }
        }

        if (down && _dragIdx >= 0)
            _dragPos = new Point(mouse.X, mouse.Y);

        if (released && _dragIdx >= 0)
        {
            int idx = _dragIdx;
            _dragIdx = -1;
            DragStateChanged?.Invoke(null);

            var moved = Math.Abs(mouse.X - _dragStart.X) + Math.Abs(mouse.Y - _dragStart.Y);
            if (moved >= 6 && idx < _stacks.Count)
            {
                // Перетаскивание в окно инвентаря — забрать предмет
                if (IsOverInventory?.Invoke(new Point(mouse.X, mouse.Y)) ?? false)
                    RequestWithdraw(_stacks[idx].item, _stacks[idx].count);
            }
            else if (moved < 6 && idx < _stacks.Count)
            {
                var (item, count) = _stacks[idx];
                var now = gameTime.TotalGameTime;
                if (_lastClickIdx == idx && (now - _lastClickTime).TotalMilliseconds < 400)
                {
                    RequestWithdraw(item, count);
                    _lastClickIdx = -1;
                }
                else
                {
                    _lastClickIdx = idx;
                    _lastClickTime = now;
                }
            }
        }

        // Правая кнопка — забрать (Shift+ПКМ — весь стак сразу, без диалога)
        bool rightPressed = mouse.RightButton == ButtonState.Pressed
            && _prevMouse.RightButton == ButtonState.Released;
        if (rightPressed && _dragIdx < 0 && _hoverIdx >= 0 && _hoverIdx < _stacks.Count)
        {
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            var (item, count) = _stacks[_hoverIdx];
            if (shift)
                WithdrawItem?.Invoke(item.Id, count);
            else
                RequestWithdraw(item, count);
        }

        _prevMouse = mouse;
    }

    private void ComputeSlotRects()
    {
        int cx = ContentX, cw = ContentW;
        int gridTop = ContentY + 30;
        int gridAreaH = Height - 30 - BottomH - 12 - 40;
        int cell = (cw - (GridCols - 1) * 4) / GridCols;
        cell = Math.Min(cell, (gridAreaH - (GridRows - 1) * 4) / GridRows);
        int gridW = GridCols * cell + (GridCols - 1) * 4;
        int gridX = cx + (cw - gridW) / 2;

        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                int x = gridX + c * (cell + 4);
                int y = gridTop + r * (cell + 4);
                _slotRects[c, r] = new Rectangle(x, y, cell, cell);
            }
        }
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        var mouse = Mouse.GetState();
        base.Draw(sb, mouse);

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int cx = ContentX, cy = ContentY, cw = ContentW;

        // Сетка
        int gridTop = cy + 30;
        int gridAreaH = Height - 30 - BottomH - 12 - 40;
        int cell = (cw - (GridCols - 1) * 4) / GridCols;
        cell = Math.Min(cell, (gridAreaH - (GridRows - 1) * 4) / GridRows);
        int gridW = GridCols * cell + (GridCols - 1) * 4;
        int gridX = cx + (cw - gridW) / 2;

        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                int x = gridX + c * (cell + 4);
                int y = gridTop + r * (cell + 4);
                var rect = new Rectangle(x, y, cell, cell);
                _slotRects[c, r] = rect;

                int idx = r * GridCols + c;
                bool filled = idx < _stacks.Count;
                bool hover = idx == _hoverIdx && _dragIdx < 0;
                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(55, 60, 80) : new Color(35, 38, 48));
                sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(60, 65, 80));

                if (filled && idx != _dragIdx)
                {
                    var stack = _stacks[idx];
                    var spr = SpriteCache.ForItem(stack.item);
                    if (spr != null)
                    {
                        var iconRect = new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
                        sb.Draw(spr, iconRect, Color.White);
                        var qFrame = SpriteCache.ForQualityFrame(stack.item.Quality);
                        if (qFrame != null)
                            sb.Draw(qFrame, iconRect, Color.White);
                    }
                    if (stack.count > 1)
                        DrawText(sb, stack.count.ToString(), rect.X + rect.Width - 16, rect.Y + rect.Height - 16, new Color(230, 230, 120));
                    if (hover) _hoverItem = stack.item;
                }
            }
        }

        DrawText(sb, $"Склад: {_storageItems.Count} / {_maxSlots}", cx, gridTop + gridAreaH - 16, new Color(220, 200, 80));

        // Нижняя подсказка
        string hint = "Перетащите предмет в инвентарь, чтобы забрать";
        var hintSize = font.MeasureString(hint);
        DrawText(sb, hint, cx + (cw - (int)hintSize.X) / 2, Y + Height - BottomH - 6, new Color(150, 140, 130));

        // Строка фильтров и её выпадающие списки — поверх сетки
        _filterBar.Draw(sb, mouse, new Rectangle(cx, ContentY, cw, 22));

        if (_hoverItem != null && _dragIdx < 0)
            DrawTooltip(sb, _hoverItem, mouse);
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