using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Windows;

public class LootWindow : GameWindow
{
    private string _corpseId = "";
    private string _monsterName = "";
    private int _damagePct;
    private List<LootItemInfo> _items = new();
    private int _gold;
    private int _goldTaken;

    private int _dragIndex = -1;
    private Point _dragStart;
    private Point _dragPos;
    private Point _dragOffset;
    private int _lastClickIdx = -1;
    private TimeSpan _lastClickTime;
    private LootItemInfo? _hoverItem;

    public Action<string, bool, string[], bool>? TakeLoot;
    public Action<LootItemInfo>? TakeItem;
    public Action<Point, LootItemInfo>? DropOnInventory;

    public string CorpseId => _corpseId;

    public void RemoveItem(LootItemInfo item)
    {
        _items.RemoveAll(i => i.Id == item.Id);
        if (_items.Count == 0 && _gold == 0)
            Visible = false;
    }

    public override bool IsDragging => _dragIndex >= 0;
    public Action<Item?>? DragStateChanged;

    private MouseState _prevMouse;
    private const int GridCols = 3;
    private const int GridRows = 3;
    private const int Cell = 60;
    private const int Gap = 6;

    public LootWindow()
    {
        Title = "Лут";
        Width = 8 + GridCols * Cell + (GridCols - 1) * Gap + 8;
        Height = 28 + 6 + GridRows * Cell + (GridRows - 1) * Gap + 6 + 56;
        Visible = false;
    }

    public void Setup(string corpseId, string monsterName, int damagePct, List<LootItemInfo> items, int gold)
    {
        _corpseId = corpseId;
        _monsterName = monsterName;
        _damagePct = damagePct;
        _items = items ?? new List<LootItemInfo>();
        _gold = gold;
        _goldTaken = 0;
        _dragIndex = -1;

        Title = damagePct > 0
            ? $"Лут: {monsterName} ({damagePct}% урона)"
            : $"Лут: {monsterName}";

        Visible = true;
    }

    private Rectangle GetSlotRect(int c, int r)
    {
        int gx = ContentX;
        int gy = ContentY;
        return new Rectangle(gx + c * (Cell + Gap), gy + r * (Cell + Gap), Cell, Cell);
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        bool down = mouse.LeftButton == ButtonState.Pressed;
        bool up = mouse.LeftButton == ButtonState.Released;
        bool pressed = down && _prevMouse.LeftButton == ButtonState.Released;
        bool released = up && _prevMouse.LeftButton == ButtonState.Pressed;

        var mouseRect = new Rectangle(ContentX, ContentY, GridCols * (Cell + Gap) - Gap, GridRows * (Cell + Gap) - Gap);

        // Начало перетаскивания
        if (pressed && mouseRect.Contains(mouse.X, mouse.Y))
        {
            for (int r = 0; r < GridRows; r++)
            {
                for (int c = 0; c < GridCols; c++)
                {
                    int idx = r * GridCols + c;
                    var rect = GetSlotRect(c, r);
                    if (rect.Contains(mouse.X, mouse.Y) && idx < _items.Count)
                    {
                        _dragIndex = idx;
                        _dragStart = new Point(mouse.X, mouse.Y);
                        _dragOffset = new Point(mouse.X - rect.X, mouse.Y - rect.Y);
                        _dragPos = new Point(mouse.X, mouse.Y);
                        DragStateChanged?.Invoke(ToItem(_items[idx]));
                    }
                }
            }
        }

        if (down && _dragIndex >= 0)
            _dragPos = new Point(mouse.X, mouse.Y);

        if (released && _dragIndex >= 0)
        {
            int idx = _dragIndex;
            _dragIndex = -1;
            DragStateChanged?.Invoke(null);
            var moved = Math.Abs(mouse.X - _dragStart.X) + Math.Abs(mouse.Y - _dragStart.Y);

            if (moved < 6 && idx < _items.Count)
            {
                // Двойной клик — забрать предмет
                var item = _items[idx];
                var now = DateTime.Now.TimeOfDay;
                bool isDouble = idx == _lastClickIdx && (_lastClickTime - now).TotalMilliseconds is > -500 and < 500;
                _lastClickIdx = idx;
                _lastClickTime = now;
                if (isDouble)
                {
                    _lastClickIdx = -1;
                    TakeItem?.Invoke(item);
                }
            }
            else if (idx < _items.Count)
            {
                // Перетаскивание — пробуем бросить в инвентарь
                DropOnInventory?.Invoke(new Point(mouse.X, mouse.Y), _items[idx]);
            }
        }

        // Кнопка "Взять все"
        var btnRect = GetTakeAllRect();
        if (pressed && btnRect.Contains(mouse.X, mouse.Y))
        {
            var all = new string[_items.Count];
            for (int i = 0; i < _items.Count; i++)
                all[i] = _items[i].Id;
            TakeLoot?.Invoke(_corpseId, true, all, _gold > 0);
            _items.Clear();
            _gold = 0;
            Visible = false;
            return;
        }

        base.Update(gameTime, keyboard, mouse);
        _prevMouse = mouse;
    }

    private Rectangle GetTakeAllRect()
    {
        int btnW = 120;
        int btnH = 30;
        int bx = X + (Width - btnW) / 2;
        int by = Y + Height - btnH - 6;
        return new Rectangle(bx, by, btnW, btnH);
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;

        base.Draw(sb, Mouse.GetState());
        var mouse = Mouse.GetState();
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        _hoverItem = null;

        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                int idx = r * GridCols + c;
                var rect = GetSlotRect(c, r);
                bool filled = idx < _items.Count;
                bool hover = rect.Contains(mouse.X, mouse.Y) && _dragIndex < 0;
                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(55, 60, 80) : new Color(35, 38, 48));
                sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(60, 65, 80));

                if (filled && idx != _dragIndex)
                {
                    var item = _items[idx];
                    var spr = SpriteCache.ForItemType(item.Type);
                    if (spr != null)
                        sb.Draw(spr, new Rectangle(rect.X + 6, rect.Y + 6, rect.Width - 12, rect.Height - 12), Color.White);
                    if (hover) _hoverItem = item;
                }
            }
        }

        if (_gold > 0)
            DrawText(sb, $"Золото: {_gold}", ContentX, Y + Height - 64, new Color(220, 200, 80));

        var btnRect = GetTakeAllRect();
        DrawButton(sb, "Взять все", btnRect, new Color(120, 100, 40), mouse, _prevMouse);

        if (_hoverItem != null)
            DrawTooltip(sb, _hoverItem, mouse);
    }

    private void DrawTooltip(SpriteBatch sb, LootItemInfo item, MouseState mouse)
    {
        var lines = ItemTooltip.BuildLinesForLoot(item.Name, item.Type, item.Value, item.Description);
        var g = GameMain.Instance;
        int wRight = g?.Graphics.PreferredBackBufferWidth ?? 1920;
        int wBottom = g?.Graphics.PreferredBackBufferHeight ?? 1080;
        TooltipRenderer.Draw(sb, lines, mouse, wRight, wBottom);
    }

    private Item ToItem(LootItemInfo info)
    {
        return new Item
        {
            Id = info.Id,
            Name = info.Name,
            Type = info.Type,
            Value = info.Value,
            HealAmount = 0,
            Description = info.Description,
            BonusPhysAttack = 0,
            BonusDefense = 0
        };
    }
}
