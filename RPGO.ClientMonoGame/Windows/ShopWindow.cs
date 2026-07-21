using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Windows;

public class ShopWindow : GameWindow
{
    private List<Item> _items = new();
    private string _merchantName = "";
    private int _playerGold;
    private int _discount;

    private int _scrollOffset;
    private const int GridCols = 8;
    private const int GridRows = 7;
    private const int ScrollbarWidth = 10;
    private const int HeaderHeight = 56;
    private const int CellGap = 4;

    private int _hoverItem = -1;
    private Rectangle[,] _slotRects = new Rectangle[GridCols, GridRows];
    private MouseState _prevMouse;
    private int _lastClickIdx = -1;
    private TimeSpan _lastClickTime;

    // Drag-to-buy
    private int _dragIndex = -1;
    private Point _dragStart;
    private Point _dragOffset;
    private Point _dragPos;

    public Action<string, int>? BuyItem { get; set; }
    public Action? SellAllTrophies { get; set; }
    public Action? Closed { get; set; }
    // Возвращает true, если предмет брошен в окно инвентаря (покупка)
    public Func<Point, Item, bool>? DropOnInventory;
    // Запрос на покупку стакающегося предмета (диалог количества)
    public Action<Item, int>? PendingBuy;
    // Уведомляет о начале/конце перетаскивания (item == null — конец)
    public Action<Item?>? DragStateChanged;

    public ShopWindow()
    {
        Title = "Торговец";
        Width = 520;
        Height = 620;
        Visible = false;
    }

    public void UpdateData(ShopData data)
    {
        _merchantName = data.MerchantName ?? "Торговец";
        _items = data.Items ?? new List<Item>();
        _playerGold = data.PlayerGold;
        _discount = data.Discount;
        _scrollOffset = 0;
        Visible = true;
    }

    public override bool IsDragging => _dragIndex >= 0;

    protected override void OnClose()
    {
        Closed?.Invoke();
        base.OnClose();
    }

    public int DiscountedPrice(Item item) => item.Value - (item.Value * _discount / 100);

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;
        base.Update(gameTime, keyboard, mouse);

        int gridX = ContentX + 8;
        int gridY = ContentY + HeaderHeight;
        int gridW = ContentW - 16 - ScrollbarWidth - 4;
        int cell = (gridW - (GridCols - 1) * CellGap) / GridCols;
        int listH = GridRows * cell + (GridRows - 1) * CellGap;
        int maxScroll = Math.Max(0, (_items.Count + GridCols - 1) / GridCols - GridRows);

        // Скролл колесом в области сетки
        if (mouse.X >= gridX && mouse.X <= gridX + gridW && mouse.Y >= gridY && mouse.Y <= gridY + listH)
        {
            int wheel = mouse.ScrollWheelValue;
            int prevWheel = _prevMouse.ScrollWheelValue;
            _scrollOffset -= (wheel - prevWheel) / (cell * 2);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
        }

        bool pressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

        _hoverItem = -1;
        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                int idx = (r + _scrollOffset) * GridCols + c;
                var rect = _slotRects[c, r];
                if (idx < _items.Count && rect.Contains(mouse.X, mouse.Y))
                {
                    _hoverItem = idx;
                    if (pressed)
                    {
                        _dragIndex = idx;
                        _dragStart = new Point(mouse.X, mouse.Y);
                        _dragOffset = new Point(mouse.X - rect.X, mouse.Y - rect.Y);
                        _dragPos = new Point(mouse.X, mouse.Y);
                        DragStateChanged?.Invoke(_items[idx]);
                    }
                }
            }
        }

        if (mouse.LeftButton == ButtonState.Pressed && _dragIndex >= 0)
            _dragPos = new Point(mouse.X, mouse.Y);

        if (released && _dragIndex >= 0)
        {
            int idx = _dragIndex;
            _dragIndex = -1;
            DragStateChanged?.Invoke(null);
            if (idx < _items.Count)
                DropOnInventory?.Invoke(new Point(mouse.X, mouse.Y), _items[idx]);
        }

        // ПКМ — покупка сразу; ЛКМ — только двойной клик (одиночный ЛКМ запускает drag)
        if (_hoverItem >= 0 && _hoverItem < _items.Count)
        {
            if (mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released)
                RequestBuy(_items[_hoverItem]);
            else if (pressed)
                HandleBuyClick(_items[_hoverItem]);
        }

        var sellAllRect = GetSellAllRect();
        if (pressed && sellAllRect.Contains(mouse.X, mouse.Y))
        {
            SellAllTrophies?.Invoke();
            _prevMouse = mouse;
            return;
        }

        _prevMouse = mouse;
    }

    private Rectangle GetSellAllRect()
    {
        return new Rectangle(ContentX + 12, Y + Height - 38, 200, 22);
    }

    private void HandleBuyClick(Item item)
    {
        int idx = _items.IndexOf(item);
        var now = DateTime.Now.TimeOfDay;
        bool isDouble = idx == _lastClickIdx && (_lastClickTime - now).TotalMilliseconds is > -500 and < 500;
        _lastClickIdx = idx;
        _lastClickTime = now;
        if (isDouble)
        {
            _lastClickIdx = -1;
            RequestBuy(item);
        }
    }

    private void RequestBuy(Item item)
    {
        int price = _discount > 0 ? DiscountedPrice(item) : item.Value;
        int stock = Math.Max(1, item.Stock);
        int maxAffordable = price > 0 ? _playerGold / price : stock;
        int max = Math.Min(stock, Math.Max(1, maxAffordable));
        if (stock > 1 || item.MaxStack > 1)
            PendingBuy?.Invoke(item, max);
        else
            BuyItem?.Invoke(item.Id ?? "", 1);
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        var mouse = Mouse.GetState();
        base.Draw(sb, mouse);

        int gridX = ContentX + 8;
        int gridY = ContentY + HeaderHeight;
        int gridW = ContentW - 16 - ScrollbarWidth - 4;
        int cell = (gridW - (GridCols - 1) * CellGap) / GridCols;
        int listH = GridRows * cell + (GridRows - 1) * CellGap;

        DrawText(sb, _merchantName, ContentX + 12, ContentY + 8, new Color(200, 170, 100));
        DrawText(sb, $"Золото: {_playerGold}", ContentX + 12, ContentY + 28, new Color(220, 190, 60));
        if (_discount > 0)
            DrawText(sb, $"Скидка: {_discount}%", ContentX + ContentW - 120, ContentY + 28, new Color(100, 200, 100));

        var sellAllRect = GetSellAllRect();
        bool sellHover = sellAllRect.Contains(mouse.X, mouse.Y);
        Color sellBg = sellHover ? new Color(150, 115, 55) : new Color(120, 90, 40);
        sb.Draw(SpriteCache.Pixel, sellAllRect, sellBg);
        sb.Draw(SpriteCache.Pixel, new Rectangle(sellAllRect.X, sellAllRect.Y, sellAllRect.Width, 2), new Color(180, 150, 90));
        DrawText(sb, "Продать все трофеи", sellAllRect.X + 10, sellAllRect.Y + (sellAllRect.Height - 14) / 2, Color.White);

        sb.Draw(SpriteCache.Pixel, new Rectangle(gridX - 2, gridY - 2, gridW + 4, listH + 4), new Color(160, 130, 80));

        int maxScroll = Math.Max(0, (_items.Count + GridCols - 1) / GridCols - GridRows);

        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                int x = gridX + c * (cell + CellGap);
                int y = gridY + r * (cell + CellGap);
                var rect = new Rectangle(x, y, cell, cell);
                _slotRects[c, r] = rect;

                int idx = (r + _scrollOffset) * GridCols + c;
                bool filled = idx < _items.Count;
                bool hover = filled && idx == _hoverItem && _dragIndex < 0;

                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(60, 55, 75) : new Color(45, 40, 55));
                sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(90, 75, 50));

                if (filled)
                {
                    var item = _items[idx];
                    var spr = SpriteCache.ForItemType(item.Type);
                    if (spr != null)
                        sb.Draw(spr, new Rectangle(rect.X + 6, rect.Y + 6, rect.Width - 12, rect.Height - 12), Color.White);
                    int stock = Math.Max(1, item.Stock);
                    if (stock > 1)
                        DrawText(sb, stock.ToString(), rect.X + rect.Width - 16, rect.Y + rect.Height - 16, new Color(230, 230, 120));
                    // Цена мелко внизу
                    var f = SpriteCache.FontSmall ?? SpriteCache.Font;
                    if (f != null)
                    {
                        string price = DiscountedPrice(item).ToString();
                        var sz = f.MeasureString(price);
                        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X + 2, rect.Y + rect.Height - 14, (int)sz.X + 4, 12), new Color(0, 0, 0, 150));
                        sb.DrawString(f, price, new Vector2(rect.X + 4, rect.Y + rect.Height - 13), new Color(240, 220, 120));
                    }
                }
            }
        }

        if (_items.Count == 0)
            DrawText(sb, "Нет товаров", gridX + gridW / 2 - 40, gridY + listH / 2 - 10, new Color(150, 140, 130));

        // Скроллбар
        sb.Draw(SpriteCache.Pixel, new Rectangle(ContentX + ContentW - ScrollbarWidth - 4, gridY, ScrollbarWidth, listH), new Color(50, 45, 60));
        if (maxScroll > 0)
        {
            float ratio = (float)GridRows / ((_items.Count + GridCols - 1) / GridCols);
            int thumbH = Math.Max(20, (int)(listH * ratio));
            int thumbY = gridY + (int)((float)_scrollOffset / maxScroll * (listH - thumbH));
            sb.Draw(SpriteCache.Pixel, new Rectangle(ContentX + ContentW - ScrollbarWidth - 4, thumbY, ScrollbarWidth, thumbH), new Color(130, 110, 80));
        }

        string hint = "Перетащите товар в инвентарь, чтобы купить";
        var hf = SpriteCache.FontSmall ?? SpriteCache.Font;
        var hintSize = hf != null ? hf.MeasureString(hint) : Vector2.Zero;
        DrawText(sb, hint, ContentX + (ContentW - (int)hintSize.X) / 2, Y + Height - 66, new Color(150, 140, 130));

        if (_hoverItem >= 0 && _hoverItem < _items.Count && _dragIndex < 0)
            DrawTooltip(sb, _items[_hoverItem], mouse);

        // Перетаскиваемый предмет рисуется глобально поверх всех окон (GameScreen).
    }

    private void DrawTooltip(SpriteBatch sb, Item item, MouseState mouse)
    {
        int stock = Math.Max(1, item.Stock);
        var lines = ItemTooltip.BuildLines(item, overrideValue: DiscountedPrice(item), stockOverride: stock);
        var g = GameMain.Instance;
        int wRight = g?.Graphics.PreferredBackBufferWidth ?? 1920;
        int wBottom = g?.Graphics.PreferredBackBufferHeight ?? 1080;
        TooltipRenderer.Draw(sb, lines, mouse, wRight, wBottom);
    }
}
