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
    private MouseState _prevMouse;

    private Rectangle _weaponSlot;
    private Rectangle _armorSlot;
    private Rectangle _accessorySlot;
    private Item? _hoverItem;

    public Action<string>? UnequipItem;

    // Тип предмета, который сейчас перетаскивают из инвентаря (для подсветки ячейки)
    public string? DraggingType { get; set; }

    public EquipmentWindow()
    {
        Title = "Снаряжение";
        Width = 380;
        Height = 460;
        Visible = false;
    }

    public void UpdateData(EquipmentData data) => _data = data;

    // Возвращает true, если точка попадает в слот снаряжения; slot — имя слота
    public bool TryGetSlotAt(Point p, out string slot)
    {
        slot = "";
        if (_data == null) return false;
        if (_weaponSlot.Contains(p)) { slot = "weapon"; return true; }
        if (_armorSlot.Contains(p)) { slot = "armor"; return true; }
        if (_accessorySlot.Contains(p)) { slot = "accessory"; return true; }
        return false;
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible || _data == null)
        {
            _prevMouse = mouse;
            return;
        }

        bool rclicked = mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
        if (rclicked)
        {
            if (_weaponSlot.Contains(mouse.X, mouse.Y) && _data.Weapon != null)
                UnequipItem?.Invoke("weapon");
            else if (_armorSlot.Contains(mouse.X, mouse.Y) && _data.Armor != null)
                UnequipItem?.Invoke("armor");
            else if (_accessorySlot.Contains(mouse.X, mouse.Y) && _data.Accessory != null)
                UnequipItem?.Invoke("accessory");
        }

        base.Update(gameTime, keyboard, mouse);
        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible || _data == null) return;
        var mouse = Mouse.GetState();
        base.Draw(sb, mouse);

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int cx = ContentX, cy = ContentY, cw = ContentW;

        int dollW = 150;
        int dollX = cx + (cw - dollW) / 2;
        DrawPaperDoll(sb, dollX, cy, dollW);

        // Размер слота
        int slot = 56;
        int slotY = cy + 70;

        // Оружие — в правой руке
        _weaponSlot = new Rectangle(dollX + dollW - slot - 4, slotY + 40, slot, slot);
        // Броня — на торсе
        _armorSlot = new Rectangle(dollX + (dollW - slot) / 2, slotY, slot, slot);
        // Аксессуар — на шее
        _accessorySlot = new Rectangle(dollX + (dollW - slot) / 2, cy + 18, slot, slot);

        _hoverItem = null;
        DrawSlot(sb, _weaponSlot, _data.Weapon, "weapon", mouse);
        DrawSlot(sb, _armorSlot, _data.Armor, "armor", mouse);
        DrawSlot(sb, _accessorySlot, _data.Accessory, "accessory", mouse);

        // Подсветка подходящей ячейки при перетаскивании предмета
        if (!string.IsNullOrEmpty(DraggingType))
        {
            Rectangle? hl = DraggingType switch
            {
                "weapon" => _weaponSlot,
                "armor" => _armorSlot,
                "accessory" => _accessorySlot,
                _ => null
            };
            if (hl.HasValue)
                sb.Draw(SpriteCache.Pixel, new Rectangle(hl.Value.X - 2, hl.Value.Y - 2, hl.Value.Width + 4, hl.Value.Height + 4), new Color(80, 200, 80, 180));
        }

        int listX = cx;
        int listY = cy + 250;
        DrawText(sb, "=== БОНУСЫ ОТ СНАРЯЖЕНИЯ ===", listX, listY, new Color(220, 200, 120));
        listY += 24;

        int atk = (_data.Weapon?.Attack ?? 0) + (_data.Armor?.Attack ?? 0) + (_data.Accessory?.Attack ?? 0);
        int def = (_data.Weapon?.Defense ?? 0) + (_data.Armor?.Defense ?? 0) + (_data.Accessory?.Defense ?? 0);
        int hp = (_data.Weapon?.MaxHealthBonus ?? 0) + (_data.Armor?.MaxHealthBonus ?? 0) + (_data.Accessory?.MaxHealthBonus ?? 0);
        int str = (_data.Weapon?.BonusStrength ?? 0) + (_data.Armor?.BonusStrength ?? 0) + (_data.Accessory?.BonusStrength ?? 0);
        int sta = (_data.Weapon?.BonusStamina ?? 0) + (_data.Armor?.BonusStamina ?? 0) + (_data.Accessory?.BonusStamina ?? 0);
        int agi = (_data.Weapon?.BonusAgility ?? 0) + (_data.Armor?.BonusAgility ?? 0) + (_data.Accessory?.BonusAgility ?? 0);
        int wis = (_data.Weapon?.BonusWisdom ?? 0) + (_data.Armor?.BonusWisdom ?? 0) + (_data.Accessory?.BonusWisdom ?? 0);
        int wil = (_data.Weapon?.BonusWill ?? 0) + (_data.Armor?.BonusWill ?? 0) + (_data.Accessory?.BonusWill ?? 0);
        int crit = (int)((_data.Weapon?.BonusCritChance ?? 0) + (_data.Armor?.BonusCritChance ?? 0) + (_data.Accessory?.BonusCritChance ?? 0));
        int eva = (int)((_data.Weapon?.BonusEvadeChance ?? 0) + (_data.Armor?.BonusEvadeChance ?? 0) + (_data.Accessory?.BonusEvadeChance ?? 0));

        var lines = new List<string>();
        if (atk > 0) lines.Add($"Атака: +{atk}");
        if (def > 0) lines.Add($"Защита: +{def}");
        if (hp > 0) lines.Add($"Здоровье: +{hp}");
        if (str > 0) lines.Add($"Сила: +{str}");
        if (sta > 0) lines.Add($"Выносл.: +{sta}");
        if (agi > 0) lines.Add($"Ловкость: +{agi}");
        if (wis > 0) lines.Add($"Мудрость: +{wis}");
        if (wil > 0) lines.Add($"Воля: +{wil}");
        if (crit > 0) lines.Add($"Крит %: +{crit}");
        if (eva > 0) lines.Add($"Уклон %: +{eva}");
        if (lines.Count == 0) lines.Add("Нет надетого снаряжения");

        foreach (var l in lines)
        {
            DrawText(sb, l, listX, listY, Color.White);
            listY += 20;
        }

        DrawText(sb, "ПКМ по слоту — снять предмет", cx, Y + Height - 22, new Color(150, 150, 160));

        if (_hoverItem != null)
            DrawTooltip(sb, _hoverItem, mouse);
    }

    private void DrawPaperDoll(SpriteBatch sb, int x, int y, int w)
    {
        var skin = new Color(60, 64, 78);
        // Голова
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + w / 2 - 16, y, 32, 32), skin);
        // Шея
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + w / 2 - 8, y + 32, 16, 10), skin);
        // Торс
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + w / 2 - 26, y + 42, 52, 70), skin);
        // Руки
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + w / 2 - 40, y + 44, 14, 60), skin);
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + w / 2 + 26, y + 44, 14, 60), skin);
        // Ноги
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + w / 2 - 22, y + 112, 18, 60), skin);
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + w / 2 + 4, y + 112, 18, 60), skin);
    }

    private void DrawSlot(SpriteBatch sb, Rectangle r, Item? item, string type, MouseState mouse)
    {
        bool hover = r.Contains(mouse.X, mouse.Y);
        sb.Draw(SpriteCache.Pixel, r, hover ? new Color(70, 75, 95) : new Color(40, 42, 52));
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, 2), new Color(90, 95, 115));

        var spr = SpriteCache.ForItemType(type);
        if (spr != null)
            sb.Draw(spr, new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12), item != null ? Color.White : new Color(120, 120, 130) * 0.4f);

        if (item != null)
        {
            _hoverItem = hover ? item : _hoverItem;
            var f = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (f != null)
            {
                var sz = f.MeasureString(item.Name);
                int tx = r.X + (r.Width - (int)sz.X) / 2;
                int ty = r.Y + r.Height + 2;
                sb.Draw(SpriteCache.Pixel, new Rectangle(tx - 3, ty - 1, (int)sz.X + 6, (int)sz.Y + 2), new Color(20, 22, 30, 210));
                sb.DrawString(f, item.Name, new Vector2(tx, ty), Color.White);
            }
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
        if (item.BonusStrength > 0) lines.Add($"Сила: +{item.BonusStrength}");
        if (item.BonusStamina > 0) lines.Add($"Выносл.: +{item.BonusStamina}");
        if (item.BonusAgility > 0) lines.Add($"Ловкость: +{item.BonusAgility}");
        if (item.BonusWisdom > 0) lines.Add($"Мудрость: +{item.BonusWisdom}");
        if (item.BonusWill > 0) lines.Add($"Воля: +{item.BonusWill}");
        if (item.BonusCritChance > 0) lines.Add($"Крит %: +{item.BonusCritChance}");
        if (item.BonusEvadeChance > 0) lines.Add($"Уклон %: +{item.BonusEvadeChance}");
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
        "armor" => "Броня",
        "accessory" => "Аксессуар",
        "consumable" => "Расходник",
        "collectible" => "Коллекция",
        "material" => "Материал",
        _ => t
    };
}
