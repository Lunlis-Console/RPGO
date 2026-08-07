using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Windows;

public class StorageWindow : GameWindow
{
    private List<Item> _inventoryItems = new();
    private List<Item> _storageItems = new();
    private List<(Item item, int count)> _invStacks = new();
    private List<(Item item, int count)> _storeStacks = new();
    private int _inventoryScroll;
    private int _storageScroll;
    private int _maxSlots = 60;

    private const int InvCols = 10;
    private const int InvRows = 10;
    private const int StoreCols = 8;
    private const int StoreRows = 7;
    private const int CellGap = 4;
    private const int PanelHeaderH = 28;
    private const int ScrollbarW = 10;
    private const int MiddleGap = 16;
    private const int BottomBarH = 36;

    private int _invCellSize;
    private int _storeCellSize;
    private Rectangle _invPanelRect;
    private Rectangle _storagePanelRect;

    private Rectangle[,] _invSlotRects = new Rectangle[InvCols, InvRows];
    private Rectangle[,] _storeSlotRects = new Rectangle[StoreCols, StoreRows];

    private int _hoverInvIdx = -1;
    private int _hoverStoreIdx = -1;
    private Item? _tooltipItem;
    private Rectangle _tooltipSlotRect;
    private Rectangle _tooltipPanelRect;

    private int _dragFromPanel = -1;
    private int _dragIdx = -1;
    private Point _dragOffset;
    private Point _dragPos;

    private new MouseState _prevMouse;
    private int _lastClickInvIdx = -1;
    private int _lastClickStoreIdx = -1;
    private TimeSpan _lastClickInvTime;
    private TimeSpan _lastClickStoreTime;

    public Action<string, int>? DepositItem { get; set; }
    public Action<string, int>? WithdrawItem { get; set; }
    public Action<Item, int>? PendingDeposit { get; set; }
    public Action<Item, int>? PendingWithdraw { get; set; }

    public override bool IsDragging => _dragIdx >= 0;

    public StorageWindow()
    {
        Title = "Склад";
        Width = 860;
        Height = 620;
        Visible = false;
    }

    public void UpdateData(List<Item>? inventoryItems, List<Item>? storageItems, int maxSlots)
    {
        _inventoryItems = inventoryItems ?? new List<Item>();
        _storageItems = storageItems ?? new List<Item>();
        _maxSlots = maxSlots;
        Visible = true;
        _inventoryScroll = 0;
        _storageScroll = 0;
    }

    public void UpdateInventory(List<Item>? inventoryItems)
    {
        _inventoryItems = inventoryItems ?? new List<Item>();
        _inventoryScroll = 0;
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;
        base.Update(gameTime, keyboard, mouse);

        _invStacks = BuildStacks(_inventoryItems);
        _storeStacks = BuildStacks(_storageItems);

        int totalW = Width - 32;
        int invPanelW = (int)(totalW * 0.58f);
        int storePanelW = totalW - invPanelW - MiddleGap;

        _invCellSize = (invPanelW - (InvCols - 1) * CellGap - ScrollbarW - 4) / InvCols;
        _storeCellSize = (storePanelW - (StoreCols - 1) * CellGap - ScrollbarW - 4) / StoreCols;

        int invPanelH = PanelHeaderH + InvRows * _invCellSize + (InvRows - 1) * CellGap + BottomBarH;
        int storePanelH = PanelHeaderH + StoreRows * _storeCellSize + (StoreRows - 1) * CellGap + BottomBarH;

        _invPanelRect = new Rectangle(X + 16, Y + TitleH + 4, invPanelW, invPanelH);
        _storagePanelRect = new Rectangle(X + 16 + invPanelW + MiddleGap, Y + TitleH + 4, storePanelW, storePanelH);

        ComputeInvSlots();
        ComputeStoreSlots();

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        bool rightPressed = mouse.RightButton == ButtonState.Pressed
            && _prevMouse.RightButton == ButtonState.Released;

        _hoverInvIdx = FindSlotAt(mouse.X, mouse.Y, _invSlotRects, InvCols, InvRows);
        _hoverStoreIdx = FindSlotAt(mouse.X, mouse.Y, _storeSlotRects, StoreCols, StoreRows);

        if (clicked)
        {
            if (_hoverInvIdx >= 0 && _hoverInvIdx < _invStacks.Count)
            {
                var now = gameTime.TotalGameTime;
                if (_lastClickInvIdx == _hoverInvIdx && (now - _lastClickInvTime).TotalMilliseconds < 400)
                {
                    var (item, count) = _invStacks[_hoverInvIdx];
                    if (item.MaxStack > 1 && count > 1)
                        PendingDeposit?.Invoke(item, count);
                    else
                        DepositItem?.Invoke(item.Id, 1);
                    _lastClickInvIdx = -1;
                }
                else
                {
                    _lastClickInvIdx = _hoverInvIdx;
                    _lastClickInvTime = now;
                    _dragFromPanel = 0;
                    _dragIdx = _hoverInvIdx;
                    int col = _hoverInvIdx % InvCols;
                    int row = _hoverInvIdx / InvCols;
                    _dragOffset = new Point(mouse.X - _invSlotRects[col, row].X, mouse.Y - _invSlotRects[col, row].Y);
                    _dragPos = new Point(mouse.X, mouse.Y);
                }
            }
            else if (_hoverStoreIdx >= 0 && _hoverStoreIdx < _storeStacks.Count)
            {
                var now = gameTime.TotalGameTime;
                if (_lastClickStoreIdx == _hoverStoreIdx && (now - _lastClickStoreTime).TotalMilliseconds < 400)
                {
                    var (item, count) = _storeStacks[_hoverStoreIdx];
                    if (item.MaxStack > 1 && count > 1)
                        PendingWithdraw?.Invoke(item, count);
                    else
                        WithdrawItem?.Invoke(item.Id, 1);
                    _lastClickStoreIdx = -1;
                }
                else
                {
                    _lastClickStoreIdx = _hoverStoreIdx;
                    _lastClickStoreTime = now;
                    _dragFromPanel = 1;
                    _dragIdx = _hoverStoreIdx;
                    int col = _hoverStoreIdx % StoreCols;
                    int row = _hoverStoreIdx / StoreCols;
                    _dragOffset = new Point(mouse.X - _storeSlotRects[col, row].X, mouse.Y - _storeSlotRects[col, row].Y);
                    _dragPos = new Point(mouse.X, mouse.Y);
                }
            }
        }

        if (_dragIdx >= 0 && mouse.LeftButton == ButtonState.Pressed)
        {
            _dragPos = new Point(mouse.X, mouse.Y);
        }

        if (_dragIdx >= 0 && released)
        {
            bool droppedOnOther = false;
            if (_dragFromPanel == 0 && _storagePanelRect.Contains(mouse.X, mouse.Y))
                droppedOnOther = true;
            else if (_dragFromPanel == 1 && _invPanelRect.Contains(mouse.X, mouse.Y))
                droppedOnOther = true;

            if (droppedOnOther)
            {
                int srcIdx = _dragIdx;
                if (_dragFromPanel == 0)
                {
                    if (srcIdx >= 0 && srcIdx < _invStacks.Count)
                    {
                        var (item, count) = _invStacks[srcIdx];
                        if (item.MaxStack > 1 && count > 1)
                            PendingDeposit?.Invoke(item, count);
                        else
                            DepositItem?.Invoke(item.Id, 1);
                    }
                }
                else
                {
                    if (srcIdx >= 0 && srcIdx < _storeStacks.Count)
                    {
                        var (item, count) = _storeStacks[srcIdx];
                        if (item.MaxStack > 1 && count > 1)
                            PendingWithdraw?.Invoke(item, count);
                        else
                            WithdrawItem?.Invoke(item.Id, 1);
                    }
                }
            }
            _dragIdx = -1;
            _dragFromPanel = -1;
        }

        // Правая кнопка мыши — открыть выбор количества для переноса
        // (в обе стороны: из инвентаря на склад и со склада в инвентарь).
        // Shift+ПКМ — перенести весь стак ячейки сразу, без диалога.
        if (rightPressed && _dragIdx < 0)
        {
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (_hoverInvIdx >= 0 && _hoverInvIdx < _invStacks.Count)
            {
                var (item, count) = _invStacks[_hoverInvIdx];
                if (shift)
                    DepositItem?.Invoke(item.Id, count);
                else if (item.MaxStack > 1 && count > 1)
                    PendingDeposit?.Invoke(item, count);
                else
                    DepositItem?.Invoke(item.Id, 1);
            }
            else if (_hoverStoreIdx >= 0 && _hoverStoreIdx < _storeStacks.Count)
            {
                var (item, count) = _storeStacks[_hoverStoreIdx];
                if (shift)
                    WithdrawItem?.Invoke(item.Id, count);
                else if (item.MaxStack > 1 && count > 1)
                    PendingWithdraw?.Invoke(item, count);
                else
                    WithdrawItem?.Invoke(item.Id, 1);
            }
        }

        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        base.Draw(sb);

        var font = SpriteCache.Font;
        var fontS = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        DrawInvPanel(sb, font, fontS);
        DrawStorePanel(sb, font, fontS);

        if (_tooltipItem != null)
        {
            DrawItemTooltip(sb, _tooltipItem, _tooltipSlotRect, _tooltipPanelRect, fontS);
            _tooltipItem = null;
        }

        if (_dragIdx >= 0)
        {
            var stacks = _dragFromPanel == 0 ? _invStacks : _storeStacks;
            Item? item = _dragIdx < stacks.Count ? stacks[_dragIdx].item : null;
            if (item != null)
            {
                var spr = SpriteCache.ForItem(item);
                int sz = 36;
                var dst = new Rectangle(_dragPos.X - _dragOffset.X, _dragPos.Y - _dragOffset.Y, sz, sz);
                if (spr != null)
                {
                    sb.Draw(spr, dst, Color.White);
                    var qFrame = SpriteCache.ForQualityFrame(item.Quality);
                    if (qFrame != null)
                        sb.Draw(qFrame, dst, Color.White);
                }
                else
                    sb.Draw(SpriteCache.Pixel, dst, new Color(180, 140, 60, 200));
                if (item.Quantity > 1 && fontS != null)
                    sb.DrawString(fontS, item.Quantity.ToString(),
                        new Vector2(dst.Right - 14, dst.Bottom - 14), Color.White);
            }
        }
    }

    private void DrawInvPanel(SpriteBatch sb, SpriteFont font, SpriteFont fontS)
    {
        sb.Draw(SpriteCache.Pixel, _invPanelRect, new Color(22, 24, 30));
        DrawBorder(sb, _invPanelRect, new Color(60, 70, 90), 2);
        sb.DrawString(font, "Инвентарь", new Vector2(_invPanelRect.X + 8, _invPanelRect.Y + 6), Color.White);

        int maxScroll = Math.Max(0, (_invStacks.Count + InvCols - 1) / InvCols - InvRows);
        if (_inventoryScroll > maxScroll) _inventoryScroll = maxScroll;

        for (int row = 0; row < InvRows; row++)
        {
            for (int col = 0; col < InvCols; col++)
            {
                int idx = (row + _inventoryScroll) * InvCols + col;
                var rect = _invSlotRects[col, row];
                bool hover = idx == _hoverInvIdx;
                bool filled = idx < _invStacks.Count;
                bool dragging = _dragFromPanel == 0 && idx == _dragIdx;

                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(55, 60, 80) : new Color(35, 38, 48));
                DrawBorder(sb, rect, new Color(55, 60, 75), 1);

                if (filled && !dragging)
                {
                    var (item, count) = _invStacks[idx];
                    var spr = SpriteCache.ForItem(item);
                    if (spr != null)
                    {
                        var iconRect = new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
                        sb.Draw(spr, iconRect, Color.White);
                        var qFrame = SpriteCache.ForQualityFrame(item.Quality);
                        if (qFrame != null)
                            sb.Draw(qFrame, iconRect, Color.White);
                    }
                    if (count > 1 && fontS != null)
                        sb.DrawString(fontS, count.ToString(),
                            new Vector2(rect.X + rect.Width - 16, rect.Y + rect.Height - 16),
                            new Color(230, 230, 120));
                    if (hover)
                    {
                        _tooltipItem = item;
                        _tooltipSlotRect = rect;
                        _tooltipPanelRect = _invPanelRect;
                    }
                }
            }
        }
    }

    private void DrawStorePanel(SpriteBatch sb, SpriteFont font, SpriteFont fontS)
    {
        sb.Draw(SpriteCache.Pixel, _storagePanelRect, new Color(22, 24, 30));
        DrawBorder(sb, _storagePanelRect, new Color(60, 70, 90), 2);
        sb.DrawString(font, $"Склад ({_storageItems.Count}/{_maxSlots})",
            new Vector2(_storagePanelRect.X + 8, _storagePanelRect.Y + 6), Color.White);

        int maxScroll = Math.Max(0, (_storeStacks.Count + StoreCols - 1) / StoreCols - StoreRows);
        if (_storageScroll > maxScroll) _storageScroll = maxScroll;

        for (int row = 0; row < StoreRows; row++)
        {
            for (int col = 0; col < StoreCols; col++)
            {
                int idx = (row + _storageScroll) * StoreCols + col;
                var rect = _storeSlotRects[col, row];
                bool hover = idx == _hoverStoreIdx;
                bool filled = idx < _storeStacks.Count;
                bool dragging = _dragFromPanel == 1 && idx == _dragIdx;

                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(55, 60, 80) : new Color(35, 38, 48));
                DrawBorder(sb, rect, new Color(55, 60, 75), 1);

                if (filled && !dragging)
                {
                    var (item, count) = _storeStacks[idx];
                    var spr = SpriteCache.ForItem(item);
                    if (spr != null)
                    {
                        var iconRect = new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
                        sb.Draw(spr, iconRect, Color.White);
                        var qFrame = SpriteCache.ForQualityFrame(item.Quality);
                        if (qFrame != null)
                            sb.Draw(qFrame, iconRect, Color.White);
                    }
                    if (count > 1 && fontS != null)
                        sb.DrawString(fontS, count.ToString(),
                            new Vector2(rect.X + rect.Width - 16, rect.Y + rect.Height - 16),
                            new Color(230, 230, 120));
                    if (hover)
                    {
                        _tooltipItem = item;
                        _tooltipSlotRect = rect;
                        _tooltipPanelRect = _storagePanelRect;
                    }
                }
            }
        }
    }

    private static void DrawItemTooltip(SpriteBatch sb, Item item, Rectangle slotRect, Rectangle panelRect, SpriteFont? fontS)
    {
        var lines = ItemTooltip.BuildLines(item);
        var g = GameMain.Instance;
        int wRight = g?.Graphics.PreferredBackBufferWidth ?? 1920;
        int wBottom = g?.Graphics.PreferredBackBufferHeight ?? 1080;
        TooltipRenderer.Draw(sb, lines, Mouse.GetState(), wRight, wBottom);
    }

    private static bool IsStackable(Item it) =>
        it.Type is "consumable" or "collectible" or "trophy" or "material";

    private static List<(Item item, int count)> BuildStacks(List<Item> items)
    {
        var result = new List<(Item, int)>();
        foreach (var it in items)
        {
            int qty = Math.Max(1, it.Quantity);
            if (IsStackable(it))
            {
                // Каждая запись сервера — отдельный стек; разбиваем по MaxStack
                // на случай записи с превышением лимита (10 + 2, а не один стек 12).
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

    private void ComputeInvSlots()
    {
        int startX = _invPanelRect.X + 8;
        int startY = _invPanelRect.Y + PanelHeaderH;
        for (int row = 0; row < InvRows; row++)
            for (int col = 0; col < InvCols; col++)
                _invSlotRects[col, row] = new Rectangle(
                    startX + col * (_invCellSize + CellGap),
                    startY + row * (_invCellSize + CellGap),
                    _invCellSize, _invCellSize);
    }

    private void ComputeStoreSlots()
    {
        int startX = _storagePanelRect.X + 8;
        int startY = _storagePanelRect.Y + PanelHeaderH;
        for (int row = 0; row < StoreRows; row++)
            for (int col = 0; col < StoreCols; col++)
                _storeSlotRects[col, row] = new Rectangle(
                    startX + col * (_storeCellSize + CellGap),
                    startY + row * (_storeCellSize + CellGap),
                    _storeCellSize, _storeCellSize);
    }

    private static int FindSlotAt(int mx, int my, Rectangle[,] slotRects, int cols, int rows)
    {
        for (int i = 0; i < cols * rows; i++)
        {
            int col = i % cols;
            int row = i / cols;
            if (slotRects[col, row].Contains(mx, my))
                return i;
        }
        return -1;
    }

    private static void DrawBorder(SpriteBatch sb, Rectangle r, Color color, int thickness)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, thickness, r.Height), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
    }
}
