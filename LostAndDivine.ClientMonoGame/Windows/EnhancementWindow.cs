using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.Shared.Models;
using LostAndDivine.ClientMonoGame.Rendering;

namespace LostAndDivine.ClientMonoGame.Windows;

/// <summary>
/// Окно заточки/усиления предметов у Кузнеца.
/// В левый слот кладётся снаряжение, в правый — камень усиления
/// (cristal_weapon для оружия, cristal_armor для брони).
/// Кнопка «Улучшить» шлёт запрос на сервер (upgrade_item).
/// </summary>
public class EnhancementWindow : GameWindow
{
    private Item? _item;
    private Item? _stone;

    // (itemId, stoneId)
    public Action<string, string>? UpgradeRequested;

    // Перетаскиваемый из инвентаря предмет (выставляется из GameScreen по DragStateChanged).
    // Используется для подсветки слотов-приёмников во время drag-n-drop.
    public Item? DraggedItem { get; set; }

    private Rectangle _itemSlot;
    private Rectangle _stoneSlot;
    private Rectangle _upgradeBtn;

    private string _status = "";

    // Drag-out из окна заточки обратно в инвентарь
    public Action<Item?>? DragStateChanged;
    public Func<Point, bool>? IsOverInventory;
    private Item? _dragItem;
    private int _dragSlot = -1; // 0=item, 1=stone
    private Point _dragOffset;
    private Point _dragPos;
    private Point _dragStart;
    private MouseState _prevMousePrivate;
    private string? _pendingStoneIcon; // для автоподмены стаков одного типа кристалла

    public EnhancementWindow()
    {
        Title = "Заточка — Кузнец";
        Width = 360;
        Height = 320;
        X = 250;
        Y = 120;
    }

    /// <summary>Сброс состояния при закрытии окна.</summary>
    public void Reset()
    {
        _item = null;
        _stone = null;
        _status = "";
        _pendingStoneIcon = null;
        _dragSlot = -1;
        _dragItem = null;
        DragStateChanged?.Invoke(null);
    }

    /// <summary>Попытка положить предмет в окно (из инвентаря drag-n-drop).</summary>
    public bool AddItem(Item item)
    {
        if (item == null) return false;

        bool isStone = item.Type == "material" &&
            (item.Icon == "cristal_weapon" || item.Icon == "cristal_armor");

        if (isStone)
        {
            _stone = item;
            _pendingStoneIcon = item.Icon;
            _status = "";
            return true;
        }

        bool isGear = item.Type != "material" && item.Type != "consumable"
            && item.Type != "collectible" && item.Type != "trophy";
        if (!isGear) return false;

        _item = item;
        _status = "";
        return true;
    }

    /// <summary>Обновляет ссылки на предметы по актуальному инвентарю (после ответа сервера).</summary>
    public void Refresh(List<Item> inventory)
    {
        if (_item != null)
            _item = inventory.FirstOrDefault(i => i.Id == _item.Id);
        if (_stone != null)
        {
            var still = inventory.FirstOrDefault(i => i.Id == _stone.Id);
            if (still == null && !string.IsNullOrEmpty(_pendingStoneIcon ?? _stone.Icon))
            {
                string icon = _pendingStoneIcon ?? _stone.Icon;
                still = inventory.FirstOrDefault(i => i.Icon == icon && i.Type == "material");
            }
            _stone = still;
            _pendingStoneIcon = _stone?.Icon;
            if (_stone == null) _pendingStoneIcon = null;
        }
        else if (_pendingStoneIcon != null)
        {
            var refill = inventory.FirstOrDefault(i => i.Icon == _pendingStoneIcon && i.Type == "material");
            if (refill != null) { _stone = refill; }
            else _pendingStoneIcon = null;
        }
        if (_item == null) _status = "";
    }

    private bool CanUpgrade()
    {
        if (_item == null || _stone == null) return false;
        if (!EnhancementHelper.CanEnhance(_item)) return false;
        bool isWeapon = !string.IsNullOrEmpty(_item.WeaponSubtype) || _item.Type == "weapon";
        string need = isWeapon ? "cristal_weapon" : "cristal_armor";
        return _stone.Icon == need;
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        var prev = _prevMouse;
        base.Update(gameTime, keyboard, mouse);
        if (!Visible) return;

        int slot = 96;
        int sy = Y + TitleH + 30;
        int sx = X + (Width - 2 * slot - 24) / 2;
        _itemSlot = new Rectangle(sx, sy, slot, slot);
        _stoneSlot = new Rectangle(sx + slot + 24, sy, slot, slot);
        _upgradeBtn = new Rectangle(X + 40, sy + slot + 40, Width - 80, 36);

        bool leftPressed = mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
        bool leftReleased = mouse.LeftButton == ButtonState.Released && prev.LeftButton == ButtonState.Pressed;
        bool rightPressed = mouse.RightButton == ButtonState.Pressed && prev.RightButton == ButtonState.Released;
        bool down = mouse.LeftButton == ButtonState.Pressed;

        // ПКМ по слоту — вернуть предмет в инвентарь (просто очищаем слот, в инвентаре он и так остался)
        if (rightPressed)
        {
            if (_itemSlot.Contains(mouse.X, mouse.Y) && _item != null)
            {
                _item = null;
                _status = "";
            }
            else if (_stoneSlot.Contains(mouse.X, mouse.Y) && _stone != null)
            {
                _stone = null;
                _pendingStoneIcon = null;
                _status = "";
            }
        }

        // Начало перетаскивания из окна заточки обратно в инвентарь
        if (leftPressed && _dragSlot < 0)
        {
            if (_itemSlot.Contains(mouse.X, mouse.Y) && _item != null)
            {
                _dragSlot = 0;
                _dragItem = _item;
                _dragStart = new Point(mouse.X, mouse.Y);
                _dragOffset = new Point(mouse.X - _itemSlot.X, mouse.Y - _itemSlot.Y);
                _dragPos = new Point(mouse.X, mouse.Y);
                _item = null;
                DragStateChanged?.Invoke(_dragItem);
            }
            else if (_stoneSlot.Contains(mouse.X, mouse.Y) && _stone != null)
            {
                _dragSlot = 1;
                _dragItem = _stone;
                _dragStart = new Point(mouse.X, mouse.Y);
                _dragOffset = new Point(mouse.X - _stoneSlot.X, mouse.Y - _stoneSlot.Y);
                _dragPos = new Point(mouse.X, mouse.Y);
                _stone = null;
                if (_dragSlot == 1) _pendingStoneIcon = null;
                DragStateChanged?.Invoke(_dragItem);
            }
        }

        if (down && _dragSlot >= 0)
            _dragPos = new Point(mouse.X, mouse.Y);

        if (leftReleased && _dragSlot >= 0)
        {
            var pt = new Point(mouse.X, mouse.Y);
            bool overInv = IsOverInventory?.Invoke(pt) ?? false;
            if (overInv)
            {
                // Сброс — предмет просто остаётся в инвентаре, слот уже очищен
                _status = "";
            }
            else
            {
                // Возврат в тот же слот, если бросили мимо инвентаря
                if (_dragSlot == 0) _item = _dragItem;
                else if (_dragSlot == 1) { _stone = _dragItem; _pendingStoneIcon = _dragItem?.Icon; }
            }
            _dragSlot = -1;
            _dragItem = null;
            DragStateChanged?.Invoke(null);
        }

        bool clicked = leftPressed;

        if (clicked && CanUpgrade() && _upgradeBtn.Contains(mouse.X, mouse.Y))
        {
            // Запоминаем иконку камня, чтобы после расхода одного стака найти следующий того же типа
            _pendingStoneIcon = _stone!.Icon;
            UpgradeRequested?.Invoke(_item!.Id, _stone!.Id);
            _status = "Идёт заточка...";
        }
        _prevMousePrivate = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        base.Draw(sb);
        if (!Visible) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        // Слоты
        DrawSlot(sb, _itemSlot, _item, "Снаряжение");
        DrawSlot(sb, _stoneSlot, _stone, "Камень");

        // Подсветка слотов-приёмников во время перетаскивания из инвентаря
        var drag = DraggedItem;
        if (drag != null)
        {
            bool isStone = drag.Type == "material" &&
                (drag.Icon == "cristal_weapon" || drag.Icon == "cristal_armor");
            bool isGear = !isStone && drag.Type != "consumable" && drag.Type != "collectible"
                && drag.Type != "trophy";

            var green = new Color(80, 220, 110);
            var red = new Color(235, 70, 70);

            if (isGear)
            {
                // Подходящее снаряжение (оружие/броня) — подсвечиваем слот предмета зелёным
                UIHelper.DrawRectOutline(sb, _itemSlot, green, 3);
            }
            else if (isStone)
            {
                // Кристалл — он кладётся в слот камня (зелёный).
                UIHelper.DrawRectOutline(sb, _stoneSlot, green, 3);
                // Совместимость с уже лежащим предметом: слот предмета зелёный, либо
                // красный, если кристалл не подходит (оружие ≠ камень брони и наоборот).
                bool compatible = true;
                if (_item != null)
                {
                    bool itemWeapon = !string.IsNullOrEmpty(_item.WeaponSubtype) || _item.Type == "weapon";
                    string need = itemWeapon ? "cristal_weapon" : "cristal_armor";
                    compatible = drag.Icon == need;
                }
                UIHelper.DrawRectOutline(sb, _itemSlot, compatible ? green : red, 3);
            }
        }

        int sy = Y + TitleH + 30;
        int slot = 96;
        // Стрелка между слотами
        var arrow = font.MeasureString("→");
        sb.DrawString(font, "→", new Vector2(_itemSlot.Right + (24 - arrow.X) / 2, sy + slot / 2 - arrow.Y / 2), Color.White);

        // Шанс и кнопка
        int target = (_item?.EnhancementLevel ?? 0) + 1;
        string chanceText = _item == null ? "Нет снаряжения"
            : !EnhancementHelper.CanEnhance(_item) ? "Макс. заточка (+10)"
            : $"Шанс +{target}: {EnhancementHelper.SuccessChance(target):0.##}%";

        sb.DrawString(font, chanceText, new Vector2(X + 16, _upgradeBtn.Y - 22), Color.LightGray);

        Color btnBg = CanUpgrade() ? new Color(60, 120, 70) : new Color(70, 70, 80);
        DrawButtonHover(sb, "Улучшить", _upgradeBtn, Mouse.GetState(), btnBg);

        if (!string.IsNullOrEmpty(_status))
            sb.DrawString(font, _status, new Vector2(X + 16, _upgradeBtn.Y + _upgradeBtn.Height + 8), new Color(255, 200, 120));
    }

    private void DrawSlot(SpriteBatch sb, Rectangle rect, Item? item, string placeholder)
    {
        sb.Draw(SpriteCache.Pixel, rect, new Color(20, 22, 30));
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), new Color(80, 90, 110));

        if (item == null)
        {
            var ph = SpriteCache.FontSmall?.MeasureString(placeholder) ?? Vector2.Zero;
            sb.DrawString(SpriteCache.FontSmall, placeholder,
                new Vector2(rect.X + (rect.Width - ph.X) / 2, rect.Y + (rect.Height - ph.Y) / 2), new Color(110, 110, 120));
            return;
        }

        var tex = SpriteCache.ForItem(item);
        if (tex != null)
        {
            int s = Math.Min(rect.Width, rect.Height) - 12;
            sb.Draw(tex, new Rectangle(rect.X + (rect.Width - s) / 2, rect.Y + (rect.Height - s) / 2, s, s), Color.White);
        }

        if (item.EnhancementLevel > 0)
        {
            var en = SpriteCache.FontSmall?.MeasureString("+" + item.EnhancementLevel) ?? Vector2.Zero;
            sb.DrawString(SpriteCache.FontSmall, "+" + item.EnhancementLevel,
                new Vector2(rect.Right - en.X - 4, rect.Y + 2), new Color(255, 170, 60));
        }
    }
}
