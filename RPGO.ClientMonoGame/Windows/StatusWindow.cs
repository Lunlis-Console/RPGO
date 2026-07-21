using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Windows;

public class StatusWindow : GameWindow
{
    private StatusData? _data;
    private MouseState _prevMouse;
    private Rectangle[] _attrBtnRects = Array.Empty<Rectangle>();

    private int _scrollY;
    private int _contentHeight;
    private int _prevWheel;
    private bool _scrollDragging;
    private int _scrollDragStartY;
    private int _scrollDragStart;

    private const int RowH = 24;
    private const int BtnW = 26;
    private const int BtnH = 20;
    private const int ScrollW = 10;

    private static readonly Color TitleGold = new Color(220, 200, 120);
    private static readonly Color StatColor = new Color(200, 200, 210);
    private static readonly Color DimColor = new Color(150, 150, 160);
    private static readonly Color AttrBtnBg = new Color(0, 150, 50);
    private static readonly Color HpFill = new Color(200, 50, 50);
    private static readonly Color MpFill = new Color(60, 120, 220);
    private static readonly Color SectionBg = new Color(35, 37, 45);
    private static readonly Color RowBg = new Color(40, 42, 52);

    public Action<string>? AllocateAttribute { get; set; }

    public StatusWindow()
    {
        Title = "Персонаж";
        Width = 440;
        Height = 640;
        Visible = false;
    }

    public void UpdateData(StatusData data) => _data = data;

    private int Viewport => Height - TitleH - 8;
    private int MaxScroll() => Math.Max(0, _contentHeight - Viewport);

    private void ClampScroll()
    {
        int max = MaxScroll();
        if (_scrollY < 0) _scrollY = 0;
        if (_scrollY > max) _scrollY = max;
    }

    private (Rectangle track, Rectangle thumb) GetScrollBarRects()
    {
        int trackX = X + Width - ScrollW - 3;
        int trackY = Y + TitleH + 2;
        int trackH = Height - TitleH - 6;
        var track = new Rectangle(trackX, trackY, ScrollW, trackH);

        int max = MaxScroll();
        int thumbH = max <= 0 ? trackH : Math.Max(28, trackH * Viewport / Math.Max(1, _contentHeight));
        int thumbY = trackY;
        if (max > 0)
            thumbY = trackY + (int)((float)_scrollY / max * (trackH - thumbH));
        var thumb = new Rectangle(trackX, thumbY, ScrollW, thumbH);
        return (track, thumb);
    }

    private (string Key, string Label, int Value, string Desc)[] GetAttrDefs()
    {
        if (_data == null) return Array.Empty<(string, string, int, string)>();
        return new (string, string, int, string)[]
        {
            ("strength",  "Сила",      _data.Strength,  "+физ.атака, +крит.урон"),
            ("endurance", "Выносл.",   _data.Endurance,  "+HP, +физ.сопр."),
            ("agility",   "Ловкость",  _data.Agility,   "+физ.атака, +скор.атк"),
            ("cunning",   "Хитрость",  _data.Cunning,   "+крит%, +уклон."),
            ("intellect", "Интеллект", _data.Intellect, "+маг.атака, +маг.эффект"),
            ("wisdom",    "Мудрость",  _data.Wisdom,    "+MP, +маг.сопр."),
        };
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible || _data == null)
        {
            _prevMouse = mouse;
            _prevWheel = mouse.ScrollWheelValue;
            return;
        }

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        bool overWindow = Contains(new Point(mouse.X, mouse.Y));

        // Колесо мыши
        int wheel = mouse.ScrollWheelValue;
        if (overWindow && wheel != _prevWheel)
        {
            _scrollY -= (wheel - _prevWheel) / 120 * RowH;
            ClampScroll();
        }
        _prevWheel = wheel;

        // Полоса прокрутки
        var (track, thumb) = GetScrollBarRects();
        if (clicked && track.Contains(mouse.X, mouse.Y))
        {
            if (!thumb.Contains(mouse.X, mouse.Y))
            {
                int max = MaxScroll();
                if (max > 0)
                {
                    float ratio = (mouse.Y - track.Y - thumb.Height / 2) / (float)(track.Height - thumb.Height);
                    _scrollY = (int)(ratio * max);
                    ClampScroll();
                }
            }
            _scrollDragging = true;
            _scrollDragStartY = mouse.Y;
            _scrollDragStart = _scrollY;
        }
        if (_scrollDragging && mouse.LeftButton == ButtonState.Pressed)
        {
            int max = MaxScroll();
            int span = track.Height - thumb.Height;
            if (max > 0 && span > 0)
            {
                float ratio = (mouse.Y - _scrollDragStartY) / (float)span;
                _scrollY = _scrollDragStart + (int)(ratio * max);
                ClampScroll();
            }
        }
        if (released) _scrollDragging = false;

        // Распределение атрибутов
        if (clicked && !_scrollDragging && _data.AttributePoints > 0)
        {
            var attrs = GetAttrDefs();
            for (int i = 0; i < _attrBtnRects.Length && i < attrs.Length; i++)
            {
                if (_attrBtnRects[i].Contains(mouse.X, mouse.Y))
                {
                    AllocateAttribute?.Invoke(attrs[i].Key);
                    break;
                }
            }
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

        var oldScissor = sb.GraphicsDevice.ScissorRectangle;
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
        var clip = new Rectangle(X, Y + TitleH, Width, Height - TitleH - 2);
        sb.GraphicsDevice.ScissorRectangle = clip;

        int cx = ContentX, cw = ContentW;
        int startCy = ContentY - _scrollY;
        int cy = startCy;

        // === Шапка: портрет + имя/уровень ===
        int portrait = 92;
        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, portrait, portrait), new Color(25, 27, 35));
        var portraitSpr = SpriteCache.GetPlayerSprite();
        if (portraitSpr != null)
            sb.Draw(portraitSpr, new Rectangle(cx + 6, cy + 6, portrait - 12, portrait - 12), Color.White);

        int infoX = cx + portrait + 12;
        DrawText(sb, _data.Name ?? "????", infoX, cy + 4, TitleGold);
        DrawText(sb, $"Уровень {_data.Level}", infoX, cy + 26, StatColor);
        DrawText(sb, $"Золото: {_data.Gold}", infoX, cy + 48, new Color(220, 200, 80));
        DrawText(sb, $"Опыт: {_data.Experience}", infoX, cy + 70, DimColor);

        // Полоски HP/MP под портретом
        int barX = cx;
        int barY = cy + portrait + 8;
        DrawBar(sb, barX, barY, cw, 20, _data.Health, _data.MaxHealth, HpFill, "Здоровье");
        DrawBar(sb, barX, barY + 26, cw, 20, _data.Mana, _data.MaxMana, MpFill, "Манна");

        cy = barY + 26 + 24;

        // === Боевые характеристики (сетка 2 колонки) ===
        cy += 4;
        cy = DrawSection(sb, "ХАРАКТЕРИСТИКИ", cx, cy, cw);

        var combat = new (string, string)[]
        {
            ("Физ.Атака", $"{_data.PhysAttack}"),
            ("Маг.Атака", $"{_data.MagAttack}"),
            ("Защита", $"{_data.Defense}"),
            ("Сопротив.", $"{_data.Resistance}"),
            ("Крит %", $"{_data.CritChance}"),
            ("Крит x", $"{_data.CritDamage}"),
            ("Уклон %", $"{_data.EvadeChance}"),
            ("Скор. атк", $"{_data.AttackSpeed}"),
        };
        for (int i = 0; i < combat.Length; i++)
        {
            int col = i % 2;
            int row = i / 2;
            int rx = cx + col * (cw / 2);
            int ry = cy + row * (RowH + 2);
            sb.Draw(SpriteCache.Pixel, new Rectangle(rx, ry, cw / 2 - 4, RowH), RowBg);
            DrawText(sb, combat[i].Item1, rx + 6, ry + 3, DimColor);
            DrawText(sb, combat[i].Item2, rx + cw / 2 - 40, ry + 3, StatColor);
        }
        cy += (combat.Length / 2 + combat.Length % 2) * (RowH + 2) + 4;

        // === Снаряжение (сводка) ===
        cy += 4;
        cy = DrawSection(sb, "СНАРЯЖЕНИЕ", cx, cy, cw);

        foreach (var slot in RPGGame.Shared.Models.EquipmentSlots.All)
        {
            _data.Equipped.TryGetValue(slot.Id, out var name);
            DrawEquipRow(sb, slot.NameRu, name, SlotIconType(slot.Id), cx, cy, cw);
            cy += RowH + 2;
        }
        cy += RowH + 4;

        // === Атрибуты с распределением ===
        cy += 4;
        string pts = _data.AttributePoints > 0
            ? $"АТРИБУТЫ (свободно: {_data.AttributePoints})"
            : "АТРИБУТЫ";
        cy = DrawSection(sb, pts, cx, cy, cw);

        var attrs = GetAttrDefs();
        _attrBtnRects = new Rectangle[attrs.Length];
        for (int i = 0; i < attrs.Length; i++)
        {
            var attr = attrs[i];
            int ry = cy;
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, ry, cw, RowH), RowBg);
            DrawText(sb, attr.Label, cx + 6, ry + 3, DimColor);
            DrawText(sb, attr.Value.ToString(), cx + 130, ry + 3, StatColor);
            DrawText(sb, attr.Desc, cx + 175, ry + 3, new Color(120, 160, 120));

            if (_data.AttributePoints > 0)
            {
                var btnRect = new Rectangle(cx + cw - BtnW, ry + 2, BtnW, BtnH);
                _attrBtnRects[i] = btnRect;
                bool hover = btnRect.Contains(mouse.X, mouse.Y);
                Color btnBg = hover ? Color.Lerp(AttrBtnBg, Color.White, 0.2f) : AttrBtnBg;
                DrawButton(sb, "+", cx + cw - BtnW, ry + 2, BtnW, BtnH, btnBg, mouse, _prevMouse);
            }
            cy += RowH + 2;
        }

        DrawBreakdown(sb, ref cy, cx, cw);

        DrawDebuffs(sb, ref cy, cx, cw);

        _contentHeight = cy - startCy;

        // Полоса прокрутки
        var (track, thumb) = GetScrollBarRects();
        sb.Draw(SpriteCache.Pixel, track, new Color(50, 52, 62));
        sb.Draw(SpriteCache.Pixel, thumb, new Color(120, 130, 150));

        sb.End();
        sb.GraphicsDevice.ScissorRectangle = oldScissor;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    private void DrawEquipRow(SpriteBatch sb, string slot, string? name, string type, int x, int y, int w)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, RowH), RowBg);
        var spr = SpriteCache.ForItemType(type);
        if (spr != null)
            sb.Draw(spr, new Rectangle(x + 4, y + 2, RowH - 4, RowH - 4), Color.White);
        DrawText(sb, slot, x + RowH + 4, y + 3, DimColor);
        DrawText(sb, name ?? "— пусто —", x + 110, y + 3, name != null ? StatColor : new Color(100, 100, 110));
    }

    private static string SlotIconType(string slotId) => slotId switch
    {
        "rhand" or "lhand" => "weapon",
        "head" or "torso" or "legs" or "feet" => "armor",
        _ => "accessory"
    };

    private void DrawButton(SpriteBatch sb, string text, int x, int y, int w, int h, Color bg, MouseState mouse, MouseState prevMouse)
    {
        var rect = new Rectangle(x, y, w, h);
        bool hover = rect.Contains(mouse.X, mouse.Y);
        sb.Draw(SpriteCache.Pixel, rect, hover ? Color.Lerp(bg, Color.White, 0.15f) : bg);
        var f = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (f != null)
        {
            var sz = f.MeasureString(text);
            sb.DrawString(f, text, new Vector2(x + (w - sz.X) / 2, y + (h - sz.Y) / 2), Color.White);
        }
    }

    private int DrawSection(SpriteBatch sb, string title, int x, int y, int w)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, RowH + 4), SectionBg);
        DrawText(sb, title, x + 6, y + 5, TitleGold);
        return y + RowH + 4;
    }

    private void DrawBreakdown(SpriteBatch sb, ref int cy, int cx, int cw)
    {
        var b = _data?.Breakdown;
        if (b == null) return;

        cy += 4;
        cy = DrawSection(sb, "ХАРАКТЕРИСТИКИ", cx, cy, cw);

        DrawBreakdownRow(sb, ref cy, cx, cw, "Физ.Атака", b.PhysAttack);
        DrawBreakdownRow(sb, ref cy, cx, cw, "Маг.Атака", b.MagAttack);
        DrawBreakdownRow(sb, ref cy, cx, cw, "Защита", b.Defense);
        DrawBreakdownRow(sb, ref cy, cx, cw, "Сопротив.", b.Resistance);
        DrawBreakdownRow(sb, ref cy, cx, cw, "Крит %", b.Crit);
        DrawBreakdownRow(sb, ref cy, cx, cw, "Крит x", b.CritDmg);
        DrawBreakdownRow(sb, ref cy, cx, cw, "Уклон %", b.Evade);
        DrawAttackSpeedBreakdown(sb, ref cy, cx, cw);
        cy += 4;

        var eff = b.Effective;
        if (eff != null)
        {
            cy += 4;
            cy = DrawSection(sb, "ЭФФЕКТЫ (С ЭКИП.)", cx, cy, cw);

            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (font != null)
            {
                var full = $"Сила {eff.Strength}, Выносл {eff.Endurance}, Ловк {eff.Agility}, Хитр {eff.Cunning}, Инт {eff.Intellect}, Мудр {eff.Wisdom}";
                var words = full.Split(' ');
                var cur = "";
                var lines = new List<string>();
                foreach (var word in words)
                {
                    string test = cur.Length == 0 ? word : cur + " " + word;
                    if (font.MeasureString(test).X > cw - 12 && cur.Length > 0)
                    {
                        lines.Add(cur);
                        cur = word;
                    }
                    else cur = test;
                }
                if (cur.Length > 0) lines.Add(cur);

                int rowH = RowH;
                int blockH = lines.Count * rowH + 6;
                sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, blockH), RowBg);
                for (int i = 0; i < lines.Count; i++)
                    DrawText(sb, lines[i], cx + 6, cy + 3 + i * rowH, DimColor);
                cy += blockH + 4;
            }
        }
    }

    private void DrawAttackSpeedBreakdown(SpriteBatch sb, ref int cy, int cx, int cw)
    {
        if (_data == null) return;
        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, RowH), RowBg);
        DrawText(sb, "Скор. атк", cx + 6, cy + 3, DimColor);
        DrawText(sb, $"атк.скор {_data.AttackSpeed}", cx + 120, cy + 3, DimColor);
        DrawText(sb, $"оруж.множ. {_data.WeaponSpeedModifier:F1}x", cx + 240, cy + 3, DimColor);
        DrawText(sb, $"= {_data.AttackIntervalMs}мс", cx + cw - 80, cy + 3, StatColor);
        cy += RowH;
    }

    private void DrawBreakdownRow(SpriteBatch sb, ref int cy, int cx, int cw, string name, BreakdownPart? p)
    {
        if (p == null) return;
        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, RowH), RowBg);
        DrawText(sb, name, cx + 6, cy + 3, DimColor);
        DrawText(sb, $"база {p.Base}", cx + 120, cy + 3, DimColor);
        DrawText(sb, $"атриб {p.AttrBonus} + экип {p.EquipBonus}", cx + 200, cy + 3, DimColor);
        DrawText(sb, $"= {p.Total}", cx + cw - 70, cy + 3, StatColor);
        cy += RowH;
    }

    private void DrawDebuffs(SpriteBatch sb, ref int cy, int cx, int cw)
    {
        var debuffs = _data?.ActiveDebuffs;
        if (debuffs == null || debuffs.Count == 0) return;

        cy += 4;
        cy = DrawSection(sb, "ДЕБАФФЫ", cx, cy, cw);

        foreach (var d in debuffs)
        {
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, RowH), RowBg);
            DrawText(sb, d.DisplayName, cx + 6, cy + 3, new Color(220, 120, 80));

            float progress = d.DurationMs > 0 ? (float)d.RemainingMs / d.DurationMs : 1f;
            int barW = (int)(cw * 0.35f);
            int barX = cx + 130;
            int barH = 6;
            int barY = cy + (RowH - barH) / 2;
            sb.Draw(SpriteCache.Pixel, new Rectangle(barX, barY, barW, barH), new Color(50, 50, 55));
            sb.Draw(SpriteCache.Pixel, new Rectangle(barX, barY, (int)(barW * progress), barH), new Color(200, 100, 60));

            DrawText(sb, $"{d.RemainingMs / 1000}s", cx + 130 + barW + 6, cy + 3, DimColor);
            cy += RowH + 2;
        }
        cy += 2;
    }
}
