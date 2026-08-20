using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Networking;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Windows;

/// <summary>
/// Компактное окно осмотра другого игрока: имя, класс, уровень, HP/MP,
/// характеристики (на всю ширину), ниже слева снаряжение ячейками (как в
/// окне экипировки, с тултипами при наведении) и справа атрибуты в одну
/// колонку. Только чтение.
/// </summary>
public class InspectWindow : GameWindow
{
    private StatusData? _data;

    private const int RowH = 21;
    private const int SectionH = 18;
    private const int EquipCols = 3;
    private const int EquipGap = 4;
    private const int EquipCell = 56;

    private Rectangle[] _slotRects = Array.Empty<Rectangle>();
    private Item? _hoverItem;

    private static readonly Color TitleGold = new Color(220, 200, 120);
    private static readonly Color StatColor = new Color(200, 200, 210);
    private static readonly Color DimColor = new Color(150, 150, 160);
    private static readonly Color HpFill = new Color(200, 50, 50);
    private static readonly Color MpFill = new Color(60, 120, 220);
    private static readonly Color SectionBg = new Color(35, 37, 45);
    private static readonly Color RowBg = new Color(40, 42, 52);
    private static readonly Color EmptySlotBg = new Color(30, 32, 40);

    public InspectWindow()
    {
        Title = "Осмотр";
        Width = 460;
        Height = 556;
        Visible = false;
    }

    public void OpenInspect(StatusData data)
    {
        _data = data;
        Title = $"Осмотр: {data.Name}";
        Visible = true;
    }

    private int EquipAreaWidth() => EquipCell * EquipCols + (EquipCols - 1) * EquipGap;

    private void ComputeSlotRects(int top)
    {
        int x = ContentX;
        int count = EquipmentSlots.All.Count;
        var rects = new Rectangle[count];
        for (int i = 0; i < count; i++)
        {
            int r = i / EquipCols, c = i % EquipCols;
            rects[i] = new Rectangle(x + c * (EquipCell + EquipGap), top + r * (EquipCell + EquipGap), EquipCell, EquipCell);
        }
        _slotRects = rects;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible || _data == null) return;
        var mouse = Mouse.GetState();
        base.Draw(sb, mouse);

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int cx = ContentX, cw = ContentW;
        int cy = ContentY + 6;

        // === Имя, класс, уровень ===
        DrawText(sb, _data.Name ?? "????", cx, cy, TitleGold);
        cy += 17;
        if (!string.IsNullOrEmpty(_data.ClassName))
        {
            DrawText(sb, _data.ClassName, cx, cy, new Color(160, 200, 220));
            cy += 17;
        }
        DrawText(sb, $"Уровень {_data.Level}", cx, cy, StatColor);
        cy += 20;

        // === HP/MP ===
        DrawBar(sb, cx, cy, cw, 16, _data.Health, _data.MaxHealth, HpFill, "Здоровье");
        DrawBar(sb, cx, cy + 24, cw, 16, _data.Mana, _data.MaxMana, MpFill, "Манна");
        cy += 24 + 16 + 8;

        // === Характеристики (на всю ширину, 2 колонки) ===
        cy = DrawSection(sb, "ХАРАКТЕРИСТИКИ", cx, cy, cw);

        var combat = new (string, string)[]
        {
            ("Физ.Атака", $"{_data.PhysAttack}"),
            ("Маг.Атака", $"{_data.MagAttack}"),
            ("Защита", $"{_data.Defense} ({(CombatMath.CalcDefenseReduction(_data.Defense) * 100):F0}%)"),
            ("Сопротив.", $"{_data.Resistance} ({(CombatMath.CalcDefenseReduction(_data.Resistance) * 100):F0}%)"),
            ("Крит %", $"{_data.CritChance:0.##}"),
            ("Крит урон %", $"{_data.CritDamage * 100:F0}"),
            ("Уклон %", $"{_data.EvadeChance:0.##}"),
            ("Блок %", $"{_data.BlockChance:0.##}"),
            ("Парир %", $"{_data.ParryChance:0.##}"),
            ("Точность %", $"{_data.Accuracy - 100:0.##}"),
            ("Стойк %", $"{_data.Tenacity:0.##}"),
            ("Пробив %", $"{_data.ArmorPenetration:0.##}"),
            ("Откат %", $"{_data.CooldownReduction:0.##}"),
            ("Реген ХП %", $"{_data.HealthRegen:0.##}"),
            ("Реген МП %", $"{_data.ManaRegen:0.##}"),
            ("Скор. атк %", $"{_data.AttackSpeed * 100:F0}"),
        };
        for (int i = 0; i < combat.Length; i++)
        {
            int col = i % 2;
            int row = i / 2;
            int rx = cx + col * (cw / 2);
            int ry = cy + row * (RowH + 1);
            sb.Draw(SpriteCache.Pixel, new Rectangle(rx, ry, cw / 2 - 4, RowH), RowBg);
            DrawText(sb, combat[i].Item1, rx + 6, ry + 2, DimColor);
            var valFont = SpriteCache.FontSmall ?? SpriteCache.Font;
            float valW = valFont != null ? valFont.MeasureString(combat[i].Item2).X : 0;
            DrawText(sb, combat[i].Item2, (int)(rx + cw / 2 - 4 - valW), ry + 2, StatColor);
        }
        cy += 8 * (RowH + 1) + 4;

        // === Ниже: слева снаряжение, справа атрибуты (одна колонка, заполняет высоту) ===
        int equipW = EquipAreaWidth();
        int attrX = cx + equipW + 16;
        int attrW = cw - equipW - 16;
        int colTop = cy;

        // Снаряжение (слева)
        DrawSection(sb, "СНАРЯЖЕНИЕ", cx, colTop, equipW);
        ComputeSlotRects(colTop + SectionH + 2);
        _hoverItem = null;
        for (int i = 0; i < _slotRects.Length; i++)
        {
            var slot = EquipmentSlots.All[i];
            var r = _slotRects[i];

            sb.Draw(SpriteCache.Pixel, r, EmptySlotBg);
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, 1), new Color(55, 60, 72));
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, 1, r.Height), new Color(55, 60, 72));

            if (_data.EquippedItems.TryGetValue(slot.Id, out var item) && item != null)
            {
                var spr = SpriteCache.ForItem(item);
                if (spr != null)
                {
                    var iconRect = new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12);
                    sb.Draw(spr, iconRect, Color.White);
                    var qFrame = SpriteCache.ForQualityFrame(item.Quality);
                    if (qFrame != null)
                        sb.Draw(qFrame, iconRect, Color.White);
                }
                if (r.Contains(mouse.X, mouse.Y))
                    _hoverItem = item;
            }
            else if (font != null)
            {
                var lines = UIHelper.WrapText(font, slot.NameRu, r.Width - 8);
                int ly = r.Y + (r.Height - lines.Count * (int)font.LineSpacing) / 2;
                foreach (var line in lines)
                {
                    var sz = font.MeasureString(line);
                    sb.DrawString(font, line, new Vector2(r.X + (r.Width - sz.X) / 2, ly), new Color(95, 100, 115));
                    ly += (int)font.LineSpacing;
                }
            }
        }

        // Атрибуты (справа) — строки растянуты по высоте, чтобы заполнить колонку без пустоты
        DrawSection(sb, "АТРИБУТЫ", attrX, colTop, attrW);

        int gridH = 4 * (EquipCell + EquipGap);
        int attrRowH = Math.Max(RowH, (gridH - 8) / 6);

        var attrs = new (string, int)[]
        {
            ("Сила", _data.Strength),
            ("Выносл.", _data.Endurance),
            ("Ловкость", _data.Agility),
            ("Хитрость", _data.Cunning),
            ("Интеллект", _data.Intellect),
            ("Мудрость", _data.Wisdom),
        };
        int ay = colTop + SectionH + 2;
        foreach (var attr in attrs)
        {
            sb.Draw(SpriteCache.Pixel, new Rectangle(attrX, ay, attrW, attrRowH), RowBg);
            DrawText(sb, attr.Item1, attrX + 6, ay + (attrRowH - 13) / 2, DimColor);
            DrawText(sb, attr.Item2.ToString(), attrX + 90, ay + (attrRowH - 13) / 2, StatColor);
            ay += attrRowH + 1;
        }

        if (_hoverItem != null)
            DrawTooltip(sb, _hoverItem, mouse);
    }

    private int DrawSection(SpriteBatch sb, string title, int x, int y, int w)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, SectionH), SectionBg);
        DrawText(sb, title, x + 6, y + 2, TitleGold);
        return y + SectionH;
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