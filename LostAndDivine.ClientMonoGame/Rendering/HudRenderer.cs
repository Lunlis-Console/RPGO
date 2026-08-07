using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Data;
using LostAndDivine.ClientMonoGame.Networking;
using System.Text.RegularExpressions;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Rendering;

public class HudRenderer
{
    private StatusData? _status;
    private bool _inCombat;
    private string? _targetName;
    private string? _targetId;
    private int _targetHp, _targetMaxHp;
    private PartyInfo? _party;
    private EntityInfo? _selectedEntity;
    private List<DebuffInfo>? _targetDebuffs;
    private double? _instanceExpiresAtUtcMs;

    // Хитбоксы иконок дебаффов для тултипа
    private readonly List<(Rectangle Rect, DebuffInfo Debuff)> _playerDebuffHits = new();
    private readonly List<(Rectangle Rect, DebuffInfo Debuff)> _targetDebuffHits = new();

    // Данные хотбара для тултипа
    private string?[] _hotbarSlots = Array.Empty<string?>();
    private int _hotbarHoverSlot = -1;
    private Input.InputManager? _inputManager;

    // Позиции UI-элементов
    private const float LeftPanelX = 4;
    private const float BarH = 18;
    private const float BarSpacing = 3;

    private const int DebuffIconSize = 20;
    private const int DebuffIconGap = 3;
    private const int DebuffBarH = 4;
    private const int DebuffRowH = DebuffIconSize + DebuffBarH + 2;

    public void UpdateStatus(StatusData status) => _status = status;
    public bool InCombat => _inCombat;

    public void UpdateInstanceTimer(double? expiresAtUtcMs)
    {
        _instanceExpiresAtUtcMs = expiresAtUtcMs;
    }

    public void UpdateCombatState(bool inCombat, string? targetName, int hp, int maxHp, string? targetId)
    {
        _inCombat = inCombat; _targetName = targetName; _targetHp = hp; _targetMaxHp = maxHp; _targetId = targetId;
    }
    public void ClearTarget() { _selectedEntity = null; _targetDebuffs = null; }
    public void UpdateParty(PartyInfo party)
    {
        // Лидер всегда первым в списке (для HUD-панели группы).
        if (party.Members.Count > 1)
        {
            var ordered = party.Members
                .OrderBy(m => m.PlayerId == party.LeaderId ? 0 : 1)
                .ThenBy(m => m.Name)
                .ToList();
            _party = new PartyInfo
            {
                LeaderId = party.LeaderId,
                LeaderName = party.LeaderName,
                Members = ordered
            };
        }
        else
        {
            _party = party;
        }
    }
    public void ClearParty() => _party = null;
    public PartyInfo? Party => _party;
    public void SetSelectedEntity(EntityInfo? entity) => _selectedEntity = entity;
    public void UpdateTargetDebuffs(List<DebuffInfo>? debuffs) => _targetDebuffs = debuffs;

    public void DrawLeftPanel(SpriteBatch sb, float x, float y, float w, float h)
    {
        var font = SpriteCache.Font;
        var fontSmall = SpriteCache.FontSmall ?? font;
        if (font == null) return;

        // Фон панели
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(225, 230, 240));

        if (_status == null) return;

        float curY = y + 4;

        // HP bar
        DrawBar(sb, font, x + 4, curY, w - 8, BarH, _status.Health, _status.MaxHealth,
            new Color(220, 60, 60), "HP");
        curY += BarH + BarSpacing;

        // MP bar
        DrawBar(sb, font, x + 4, curY, w - 8, BarH, _status.Mana, _status.MaxMana,
            new Color(70, 130, 220), "Мана");
        curY += BarH + BarSpacing;

        // XP bar
        int need = _status.Level * 50;
        DrawBar(sb, font, x + 4, curY, w - 8, BarH, _status.Experience, need,
            new Color(90, 180, 90), "Опыт");
        curY += BarH + BarSpacing + 4;

        // Режим боя
        string combatText = _inCombat ? "Режим: Бой" : "Режим: Мирный";
        Color combatColor = _inCombat ? Color.Red : Color.LimeGreen;
        sb.DrawString(font, combatText, new Vector2(x + 4, curY), combatColor);
        curY += 16;

        // Кнопки
        string[] buttons = { "Статус", "Инвентарь (I)", "Журнал (J)", "Навыки (K)" };
        for (int i = 0; i < buttons.Length; i++)
        {
            var btnRect = new Rectangle((int)(x + 4), (int)curY, (int)(w - 8), 28);
            sb.Draw(SpriteCache.Pixel, btnRect, new Color(0, 120, 215));
            var textSize = font.MeasureString(buttons[i]);
            sb.DrawString(font, buttons[i], new Vector2(btnRect.X + (btnRect.Width - textSize.X) / 2, btnRect.Y + (btnRect.Height - textSize.Y) / 2), Color.White);
            curY += 30;
        }

        curY += 8;

        // Пати
        if (_party != null && _party.Members.Count > 0)
        {
            sb.Draw(SpriteCache.Pixel, new Rectangle((int)(x + 4), (int)curY, (int)(w - 8), 20 + _party.Members.Count * 30), new Color(40, 50, 65));
            sb.DrawString(font, $"Пати ({_party.Members.Count}/5)", new Vector2(x + 8, curY + 2), new Color(220, 200, 100));
            curY += 18;

            foreach (var m in _party.Members)
            {
                bool isLeader = m.PlayerId == _party.LeaderId;
                string nameStr = (isLeader ? "★ " : "  ") + m.Name + $" (ур. {m.Level})";
                sb.DrawString(fontSmall, nameStr, new Vector2(x + 8, curY), isLeader ? new Color(220, 200, 100) : new Color(200, 200, 210));

                // HP bar
                float barW = w - 16;
                float hpPct = m.MaxHealth > 0 ? (float)m.Health / m.MaxHealth : 0;
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)(x + 8), (int)(curY + 12), (int)barW, 8), new Color(60, 30, 30));
                sb.Draw(SpriteCache.Pixel, new Rectangle((int)(x + 8), (int)(curY + 12), (int)(barW * hpPct), 8), new Color(180, 50, 50));
                sb.DrawString(fontSmall, $"{m.Health}/{m.MaxHealth}", new Vector2(x + 8 + barW / 2 - 15, curY + 11), Color.White);

                curY += 28;
            }
        }
    }

    public void DrawRightPanel(SpriteBatch sb, float x, float y, float w, float h)
    {
        var font = SpriteCache.Font;
        var fontSmall = SpriteCache.FontSmall ?? font;
        if (font == null) return;

        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(225, 230, 240));

        float curY = y + 8;

        if (_selectedEntity == null)
        {
            sb.DrawString(fontSmall, "Нет выбранной цели", new Vector2(x + 8, curY), new Color(120, 120, 130));
            return;
        }

        // Имя цели
        string typeStr = _selectedEntity.Type switch
        {
            "monster" => "Монстр", "merchant" => "Торговец", "npc" => "NPC",
            "collectible" => "Собираемый", "board" => "Доска заданий",
            "player" => "Игрок", "portal" => "Портал", _ => _selectedEntity.Type
        };
        string lvl = _selectedEntity.Level > 0 ? $" (Ур. {_selectedEntity.Level})" : "";
        sb.DrawString(font, _selectedEntity.Name + lvl, new Vector2(x + 8, curY), Color.White);
        curY += 14;
        sb.DrawString(fontSmall, typeStr, new Vector2(x + 8, curY), new Color(100, 100, 110));
        curY += 16;

        // Портал: куда ведёт
        if (_selectedEntity.Type == "portal" && !string.IsNullOrEmpty(_selectedEntity.Info))
        {
            sb.DrawString(fontSmall, $"Ведёт: {_selectedEntity.Info}", new Vector2(x + 8, curY), new Color(120, 160, 255));
            curY += 18;
        }

        // HP bar цели
        if (_selectedEntity.MaxHp > 0)
        {
            float hpPct = (float)_selectedEntity.Hp / _selectedEntity.MaxHp;
            sb.Draw(SpriteCache.Pixel, new Rectangle((int)(x + 8), (int)curY, (int)(w - 16), 12), new Color(60, 60, 70));
            sb.Draw(SpriteCache.Pixel, new Rectangle((int)(x + 8), (int)curY, (int)((w - 16) * hpPct), 12), new Color(220, 60, 60));
            sb.DrawString(fontSmall, $"HP {_selectedEntity.Hp}/{_selectedEntity.MaxHp}", new Vector2(x + 8 + (w - 16) / 2 - 20, curY - 1), Color.White);
            curY += 18;
        }

        // Кнопка взаимодействия
        string interactText = _selectedEntity.Type switch
        {
            "monster" => $"Атаковать {_selectedEntity.Name}",
            "merchant" => "Открыть магазин",
            "board" => "Квесты",
            "collectible" => "Собрать",
            "portal" => "Наступите на портал",
            _ => "Взаимодействовать"
        };
        var interactRect = new Rectangle((int)(x + 4), (int)(h - 40), (int)(w - 8), 36);
        Color interactBg = _selectedEntity.Type == "monster" ? new Color(180, 60, 60) : new Color(0, 120, 215);
        sb.Draw(SpriteCache.Pixel, interactRect, interactBg);
        var iSize = font.MeasureString(interactText);
        sb.DrawString(font, interactText, new Vector2(interactRect.X + (interactRect.Width - iSize.X) / 2, interactRect.Y + (interactRect.Height - iSize.Y) / 2), Color.White);
    }

    public void SetInputManager(Input.InputManager im) => _inputManager = im;

    private bool IsSkillBlocked(ClientSkillInfo skill)
    {
        if (_status == null || _inputManager == null) return false;
        bool noMana = skill.MpCost > 0 && _status.Mana < skill.MpCost;
        bool notLearned = !skill.Learned;
        bool isWeaponSkill = skill.Type.StartsWith("Лук") || skill.Type.StartsWith("Меч");
        bool noWeapon = isWeaponSkill && !_inputManager.HasWeaponEquipped();
        bool wrongWeapon = skill.Type.StartsWith("Лук") && !_inputManager.IsWieldingBow();
        wrongWeapon |= skill.Type.StartsWith("Меч") && _inputManager.IsWieldingBow();
        return noMana || notLearned || noWeapon || wrongWeapon;
    }

    public void DrawHotbar(SpriteBatch sb, float x, float y, float w, float h, string?[] hotbarSlots, Texture2D?[] icons, int[] counts,
        int hoverSlot = -1, int dragSlot = -1, int[]? cdRemain = null, int[]? cdTotal = null)
    {
        _hotbarSlots = hotbarSlots;
        _hotbarHoverSlot = hoverSlot;
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(50, 50, 60));

        int slotW = (int)(w / 10);
        int size = slotW - 6; // квадрат по ширине слота, чуть меньше для зазора
        for (int i = 0; i < 10; i++)
        {
            // Квадратная ячейка, центрированная по вертикали
            int cellX = (int)x + i * slotW + (slotW - size) / 2;
            int cellY = (int)y + ((int)h - size) / 2;
            var slotRect = new Rectangle(cellX, cellY, size, size);
            sb.Draw(SpriteCache.Pixel, slotRect, new Color(70, 72, 82));

            // Подсветка ячейки при наведении курсора (более тёмный фон)
            if (i == hoverSlot)
                sb.Draw(SpriteCache.Pixel, slotRect, new Color(20, 20, 28, 120));

            UIHelper.DrawRectOutline(sb, slotRect, new Color(90, 92, 102));

            // Жёлтая рамка для слота, над которым заготовлен (drag) навык
            if (i == dragSlot)
            {
                bool blocked = false;
                if (_status != null && _inputManager != null && hotbarSlots != null && i < hotbarSlots.Length && hotbarSlots[i]?.StartsWith("skill:") == true)
                {
                    var skillName = hotbarSlots[i]![6..];
                    var skill = _inputManager.GetSkillByName(skillName);
                    if (skill != null)
                        blocked = IsSkillBlocked(skill);
                }
                if (!blocked)
                    UIHelper.DrawRectOutline(sb, new Rectangle(slotRect.X - 1, slotRect.Y - 1, slotRect.Width + 2, slotRect.Height + 2), new Color(255, 215, 0));
            }

            bool hasContent = hotbarSlots != null && i < hotbarSlots.Length && !string.IsNullOrEmpty(hotbarSlots[i]);

            // Номер клавиши в левом верхнем углу
            string slotNum = (i + 1) % 10 == 0 ? "0" : (i + 1).ToString();
            sb.DrawString(font, slotNum, new Vector2(slotRect.X + 3, slotRect.Y + 2),
                hasContent ? new Color(180, 185, 200) : new Color(120, 125, 140));

            // Иконка вместо надписи
            Texture2D? icon = (icons != null && i < icons.Length) ? icons[i] : null;
            if (icon != null)
            {
                int pad = 6;
                int isz = size - pad * 2;
                sb.Draw(icon, new Rectangle(slotRect.X + pad, slotRect.Y + pad, isz, isz), Color.White);
            }

            // Затемнение иконки если навык недоступен
            bool skillBlocked = false;
            if (_status != null && _inputManager != null && hotbarSlots != null && i < hotbarSlots.Length && hotbarSlots[i]?.StartsWith("skill:") == true)
            {
                var skillName = hotbarSlots[i]![6..];
                var skill = _inputManager.GetSkillByName(skillName);
                if (skill != null)
                    skillBlocked = IsSkillBlocked(skill);
            }
            if (skillBlocked)
                sb.Draw(SpriteCache.Pixel, new Rectangle(slotRect.X, slotRect.Y, slotRect.Width, slotRect.Height), new Color(0, 0, 0, 140));

            // Количество предмета (для item-слотов)
            int cnt = (counts != null && i < counts.Length) ? counts[i] : 0;
            if (cnt > 1)
            {
                string s = cnt.ToString();
                var sz = font.MeasureString(s);
                sb.DrawString(font, s, new Vector2(slotRect.Right - sz.X - 2, slotRect.Bottom - sz.Y - 1), new Color(230, 230, 240));
            }

            // Анимация кулдауна (для навыков): тёмная маска сверху вниз
            int rem = (cdRemain != null && i < cdRemain.Length) ? cdRemain[i] : 0;
            int tot = (cdTotal != null && i < cdTotal.Length) ? cdTotal[i] : 0;
            if (rem > 0 && tot > 0)
            {
                float frac = Math.Clamp((float)rem / tot, 0f, 1f);
                int maskH = (int)(slotRect.Height * frac);
                sb.Draw(SpriteCache.Pixel, new Rectangle(slotRect.X, slotRect.Y, slotRect.Width, maskH), new Color(0, 0, 0, 150));
                int secs = (int)Math.Ceiling(rem / 1000f);
                string t = secs.ToString();
                var tsz = font.MeasureString(t);
                sb.DrawString(font, t, new Vector2(slotRect.X + (slotRect.Width - tsz.X) / 2, slotRect.Y + (slotRect.Height - tsz.Y) / 2), Color.White);
            }
        }
    }

    public void DrawHotbarTooltip(SpriteBatch sb)
    {
        var fontSmall = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (fontSmall == null || _inputManager == null) return;

        int slot = _hotbarHoverSlot;
        if (slot < 0 || slot >= _hotbarSlots.Length) return;

        var slotValue = _hotbarSlots[slot];
        if (string.IsNullOrEmpty(slotValue)) return;

        var lines = new List<(string Text, Color Color)>();

        if (slotValue.StartsWith("skill:"))
        {
            var skill = _inputManager.GetSkillById(slotValue[6..]);
            if (skill == null)
                skill = _inputManager.GetSkillByName(slotValue[6..]);
            if (skill == null) return;

            lines.Add((skill.Name, new Color(255, 215, 0)));

            // Ранг
            if (skill.Learned && skill.Rank > 1)
                lines.Add(($"Ранг {skill.Rank}/{skill.MaxRank}", new Color(200, 180, 100)));

            // Описание с ранговой корректировкой
            if (!string.IsNullOrEmpty(skill.Description))
            {
                string desc = skill.Description;
                if (skill.Learned && skill.Rank > 1)
                {
                    double mult = skill.Type == "Пассивные"
                        ? 1.0 + (skill.Rank - 1) * 0.33
                        : 1.0 + (skill.Rank - 1) * 0.12;
                    desc = RankAdjustDesc(desc, mult);
                }
                lines.Add((desc, new Color(180, 180, 200)));
            }

            bool isPassive = skill.Type == "Пассивные";
            if (!isPassive && skill.MpCost > 0)
                lines.Add(($"MP: {skill.MpCost}", new Color(120, 160, 255)));
            if (!isPassive && skill.CooldownMs > 0)
            {
                int cd = skill.CooldownMs;
                if (skill.Learned) cd = (int)(cd * (1.0 - (skill.Rank - 1) * 0.08));
                lines.Add(($"Кулдаун: {cd / 1000}с", new Color(200, 180, 120)));
            }
            if (!isPassive && skill.DamageMultiplier > 1)
            {
                double dmg = skill.DamageMultiplier;
                if (skill.Learned) dmg *= 1.0 + (skill.Rank - 1) * 0.12;
                lines.Add(($"x{dmg:F2} урон", new Color(220, 120, 120)));
            }
            lines.Add(($"Очки навыков: {skill.SkillPointCost}", new Color(170, 170, 180)));
        }
        else if (slotValue.StartsWith("item:"))
        {
            var item = _inputManager.GetItemByName(slotValue[5..]);
            if (item == null) return;

            var typeLabel = item.Type switch
            {
                "weapon" => "Оружие",
                "armor" => "Броня",
                "consumable" => "Расходник",
                "quest" => "Квестовый",
                _ => item.Type
            };
            lines.Add((item.Name, new Color(255, 215, 0)));
            lines.Add(($"[{typeLabel}]", new Color(140, 140, 160)));
            if (!string.IsNullOrEmpty(item.Description))
                lines.Add((item.Description, new Color(180, 180, 200)));
            if (item.HealAmount > 0)
                lines.Add(($"Лечение: {item.HealAmount}", new Color(120, 220, 120)));
            if (item.RestoreMana > 0)
                lines.Add(($"Мана: {item.RestoreMana}", new Color(100, 140, 240)));
            if (item.MaxHealthBonus > 0)
                lines.Add(($"+{item.MaxHealthBonus} HP", new Color(120, 220, 120)));
            if (item.BonusPhysAttack > 0) lines.Add(($"+{item.BonusPhysAttack} Физ.Атк", new Color(220, 120, 120)));
            if (item.BonusMagAttack > 0) lines.Add(($"+{item.BonusMagAttack} Маг.Атк", new Color(120, 120, 220)));
            if (item.BonusDefense > 0) lines.Add(($"+{item.BonusDefense} Защита", new Color(120, 160, 220)));
            if (item.BonusResistance > 0) lines.Add(($"+{item.BonusResistance} Сопротивление", new Color(140, 120, 220)));
            if (item.BonusStrength > 0) lines.Add(($"+{item.BonusStrength} Сила", new Color(200, 100, 100)));
            if (item.BonusEndurance > 0) lines.Add(($"+{item.BonusEndurance} Выносливость", new Color(180, 150, 80)));
            if (item.BonusAgility > 0) lines.Add(($"+{item.BonusAgility} Ловкость", new Color(100, 180, 100)));
            if (item.BonusCunning > 0) lines.Add(($"+{item.BonusCunning} Хитрость", new Color(140, 140, 140)));
            if (item.BonusIntellect > 0) lines.Add(($"+{item.BonusIntellect} Интеллект", new Color(80, 140, 220)));
            if (item.BonusWisdom > 0) lines.Add(($"+{item.BonusWisdom} Мудрость", new Color(180, 180, 100)));
            if (item.BonusCritChance > 0) lines.Add(($"+{item.BonusCritChance * 100:F0}% Крит", new Color(220, 180, 80)));
            if (item.BonusCritDamage > 0) lines.Add(($"+{item.BonusCritDamage * 100:F0}% Урон крита", new Color(220, 100, 80)));
            if (item.BonusEvadeChance > 0) lines.Add(($"+{item.BonusEvadeChance * 100:F0}% Уклонение", new Color(120, 200, 180)));
            if (item.BonusBlockChance > 0) lines.Add(($"+{item.BonusBlockChance * 100:F0}% Блок", new Color(160, 180, 220)));
            if (item.BonusParryChance > 0) lines.Add(($"+{item.BonusParryChance * 100:F0}% Парирование", new Color(200, 160, 220)));
        }
        else return;

        if (lines.Count == 0) return;

        int pad = 8;
        int lineGap = 3;
        float lineH = fontSmall.MeasureString("X").Y;
        int maxW = 260;

        var wrapped = new List<(string Text, Color Color)>();
        foreach (var (text, color) in lines)
        {
            if (fontSmall.MeasureString(text).X <= maxW - pad * 2)
            {
                wrapped.Add((text, color));
            }
            else
            {
                var words = text.Split(' ');
                string current = "";
                foreach (var w in words)
                {
                    string test = string.IsNullOrEmpty(current) ? w : current + " " + w;
                    if (fontSmall.MeasureString(test).X > maxW - pad * 2 && !string.IsNullOrEmpty(current))
                    {
                        wrapped.Add((current, color));
                        current = w;
                    }
                    else
                    {
                        current = test;
                    }
                }
                if (!string.IsNullOrEmpty(current))
                    wrapped.Add((current, color));
            }
        }

        int tipW = maxW;
        int tipH = (int)(lineH * wrapped.Count + lineGap * (wrapped.Count - 1) + pad * 2);

        var ms = Mouse.GetState();
        var vp = sb.GraphicsDevice.Viewport;
        int tipX = ms.X + 16;
        int tipY = ms.Y + 16;
        if (tipX + tipW > vp.Width) tipX = ms.X - tipW - 4;
        if (tipY + tipH > vp.Height) tipY = ms.Y - tipH - 4;
        if (tipX < 0) tipX = 0;
        if (tipY < 0) tipY = 0;

        var bg = new Rectangle(tipX, tipY, tipW, tipH);
        sb.Draw(SpriteCache.Pixel, bg, new Color(15, 15, 20, 235));
        UIHelper.DrawRectOutline(sb, bg, new Color(80, 80, 100));

        float cx = tipX + pad;
        float cy = tipY + pad;
        foreach (var (text, color) in wrapped)
        {
            sb.DrawString(fontSmall, text, new Vector2(cx, cy), color);
            cy += lineH + lineGap;
        }
    }

    public float DrawPlayerStatusPanel(SpriteBatch sb, float x, float y)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null || _status == null) return 0;

        int barH = 18;
        int barGap = 3;
        int barsTotalH = barH * 3 + barGap * 2;
        int square = barsTotalH;
        int barW = 200;
        int gap = 8;

        // Имя слева над квадратом уровня
        string playerName = GameMain.Instance?.Client.PlayerName ?? "";
        int nameH = 0;
        if (!string.IsNullOrEmpty(playerName))
        {
            var nameSize = font.MeasureString(playerName);
            nameH = (int)nameSize.Y + 2;
            sb.DrawString(font, playerName, new Vector2(x, y), new Color(220, 200, 120));
        }

        float panelY = y + nameH;

        // Квадрат с уровнем
        var sqRect = new Rectangle((int)x, (int)panelY, square, square);
        sb.Draw(SpriteCache.Pixel, sqRect, new Color(40, 44, 58));
        UIHelper.DrawRectOutline(sb, sqRect, new Color(90, 95, 115));
        string lvl = _status.Level.ToString();
        string lvlLabel = "УР";
        var lvlSize = font.MeasureString(lvl);
        var lblSize = font.MeasureString(lvlLabel);
        sb.DrawString(font, lvlLabel, new Vector2(sqRect.X + (sqRect.Width - lblSize.X) / 2, sqRect.Y + square * 0.18f), new Color(160, 200, 255));
        sb.DrawString(font, lvl, new Vector2(sqRect.X + (sqRect.Width - lvlSize.X) / 2, sqRect.Y + square * 0.38f), Color.White);

        // Бары справа от квадрата
        int bx = (int)x + square + gap;
        int by = (int)panelY;

        DrawBar(sb, font, bx, by, barW, barH, _status.Health, _status.MaxHealth, new Color(200, 50, 50), "Здоровье");
        by += barH + barGap;
        DrawBar(sb, font, bx, by, barW, barH, _status.Mana, _status.MaxMana, new Color(60, 120, 220), "Манна");
        by += barH + barGap;
        int need = _status.Level * 50;
        DrawBar(sb, font, bx, by, barW, barH, _status.Experience, need, new Color(90, 180, 90), "Опыт");
        return (by + barH) - y;
    }

    private void DrawBar(SpriteBatch sb, SpriteFont font, float x, float y, float w, float h, int value, int max, Color fillColor, string label)
    {
        float pct = max > 0 ? Math.Clamp((float)value / max, 0, 1) : 0;
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(60, 60, 70));
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, (int)(w * pct), (int)h), fillColor);
        var text = $"{label} {value}/{max}";
        var textSize = font.MeasureString(text);
        sb.DrawString(font, text, new Vector2(x + (w - textSize.X) / 2, y + (h - textSize.Y) / 2), Color.White);
    }

    public float DrawPlayerDebuffs(SpriteBatch sb, float x, float y, float maxW)
    {
        _playerDebuffHits.Clear();
        var debuffs = _status?.ActiveDebuffs;
        if (debuffs == null || debuffs.Count == 0) return 0;

        int cols = Math.Max(1, (int)((maxW + DebuffIconGap) / (DebuffIconSize + DebuffIconGap)));
        int cx = (int)x;
        int cy = (int)y;
        for (int i = 0; i < debuffs.Count; i++)
        {
            if (debuffs[i] == null) continue;
            if (i > 0 && i % cols == 0) { cx = (int)x; cy += DebuffRowH + DebuffIconGap; }
            var iconRect = new Rectangle(cx, cy, DebuffIconSize, DebuffIconSize);
            DrawDebuffIcon(sb, iconRect, debuffs[i]);
            _playerDebuffHits.Add((iconRect, debuffs[i]));
            cx += DebuffIconSize + DebuffIconGap;
        }
        return cy + DebuffRowH - y;
    }

    public float DrawTargetDebuffs(SpriteBatch sb, int screenW, float y)
    {
        _targetDebuffHits.Clear();
        if (_targetDebuffs == null || _targetDebuffs.Count == 0) return 0;

        int barW = 320;
        int startX = (screenW - barW) / 2;
        int cx = startX;
        int cy = (int)y;
        int cols = Math.Max(1, barW / (DebuffIconSize + DebuffIconGap));
        for (int i = 0; i < _targetDebuffs.Count; i++)
        {
            if (_targetDebuffs[i] == null) continue;
            if (i > 0 && i % cols == 0) { cx = startX; cy += DebuffRowH + DebuffIconGap; }
            var iconRect = new Rectangle(cx, cy, DebuffIconSize, DebuffIconSize);
            DrawDebuffIcon(sb, iconRect, _targetDebuffs[i]);
            _targetDebuffHits.Add((iconRect, _targetDebuffs[i]));
            cx += DebuffIconSize + DebuffIconGap;
        }
        return cy + DebuffRowH - y;
    }

    public void DrawDebuffTooltip(SpriteBatch sb)
    {
        var fontSmall = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (fontSmall == null) return;

        var ms = Microsoft.Xna.Framework.Input.Mouse.GetState();
        var hit = _playerDebuffHits.Concat(_targetDebuffHits).FirstOrDefault(h => h.Rect.Contains(ms.X, ms.Y));
        if (hit.Debuff == null) return;

        string line1 = hit.Debuff.DisplayName;
        string line2 = hit.Debuff.Description;
        bool showDuration = hit.Debuff.Type != "DualWieldBonus";
        string? line3 = showDuration ? $"Осталось: {hit.Debuff.RemainingMs / 1000}с" : null;

        var s1 = fontSmall.MeasureString(line1);
        var s2 = string.IsNullOrEmpty(line2) ? Vector2.Zero : fontSmall.MeasureString(line2);
        var s3 = string.IsNullOrEmpty(line3) ? Vector2.Zero : fontSmall.MeasureString(line3);

        int pad = 8;
        int lineGap = 4;
        float lineH = s1.Y;
        int lines = 1 + (string.IsNullOrEmpty(line2) ? 0 : 1) + (string.IsNullOrEmpty(line3) ? 0 : 1);

        int tipW = (int)Math.Max(s1.X, Math.Max(s2.X, s3.X)) + pad * 2;
        int tipH = (int)(lineH * lines + lineGap * (lines - 1) + pad * 2);
        int tipX = ms.X + 14;
        int tipY = ms.Y + 14;

        var bg = new Rectangle(tipX, tipY, tipW, tipH);
        sb.Draw(SpriteCache.Pixel, bg, new Color(15, 15, 20, 235));
        UIHelper.DrawRectOutline(sb, bg, new Color(80, 80, 100));

        float cx = tipX + pad;
        float cy = tipY + pad;
        sb.DrawString(fontSmall, line1, new Vector2(cx, cy), new Color(220, 200, 140));
        cy += lineH + lineGap;
        if (!string.IsNullOrEmpty(line2))
        {
            sb.DrawString(fontSmall, line2, new Vector2(cx, cy), new Color(170, 175, 190));
            cy += lineH + lineGap;
        }
        if (!string.IsNullOrEmpty(line3))
            sb.DrawString(fontSmall, line3, new Vector2(cx, cy), new Color(180, 140, 100));
    }

    private static void DrawDebuffIcon(SpriteBatch sb, Rectangle rect, DebuffInfo d)
    {
        Color bg = d.Type switch
        {
            "ArmorPenetration" => new Color(160, 80, 40),
            "DamageBonus" => new Color(160, 40, 40),
            "DamageReduction" => new Color(40, 80, 160),
            "AccuracyReduction" => new Color(120, 50, 160),
            "CleaveReady" => new Color(180, 160, 40),
            "AttackSpeedBonus" => new Color(60, 160, 80),
            "DualWieldBonus" => new Color(200, 140, 60),
            "Stun" => new Color(255, 200, 40),
            "Root" => new Color(100, 180, 100),
            "Slow" => new Color(80, 160, 40),
            "Dot" => new Color(60, 200, 60),
            _ => new Color(80, 80, 100)
        };
        sb.Draw(SpriteCache.Pixel, rect, bg);
        UIHelper.DrawRectOutline(sb, rect, bg * 1.4f);

        var fontSmall = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (fontSmall != null && !string.IsNullOrEmpty(d.DisplayName))
        {
            string ch = d.DisplayName[..1];
            var sz = fontSmall.MeasureString(ch);
            sb.DrawString(fontSmall, ch, new Vector2(rect.X + (rect.Width - sz.X) / 2, rect.Y + (rect.Height - sz.Y) / 2 - 1), Color.White);
        }

        float progress = d.DurationMs > 0 ? (float)d.RemainingMs / d.DurationMs : 1f;
        int barY = rect.Bottom + 1;
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, barY, rect.Width, DebuffBarH), new Color(30, 30, 35));
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, barY, (int)(rect.Width * progress), DebuffBarH), bg * 0.9f);
    }

    // Полоса здоровья цели по центру сверху (стиль ММО) — и в бою, и в мирном режиме при выбранной цели
    public void DrawTargetBar(SpriteBatch sb, int screenW)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        // Источник данных: боевая цель (в приоритете) либо выбранная сущность.
        // Если игрок выбрал мышью другого моба/игрока — показываем выбранного,
        // боевая цель остаётся источником для текущего таргета боя.
        string? name = null;
        int hp = 0, maxHp = 0;
        var sel = _selectedEntity;
        bool selIsTarget = sel != null && (sel.Type is "monster" or "player");
        bool selIsCombatTarget = selIsTarget && sel != null && sel.Id != null && sel.Id == _targetId;
        if (selIsTarget && !selIsCombatTarget)
        {
            name = sel!.Name;
            hp = sel.Hp; maxHp = sel.MaxHp;
        }
        else if (_inCombat && !string.IsNullOrEmpty(_targetName))
        {
            name = _targetName; hp = _targetHp; maxHp = _targetMaxHp;
        }
        else if (sel != null && sel.Type != "move")
        {
            name = sel.Name;
            hp = sel.Hp; maxHp = sel.MaxHp;
        }
        if (string.IsNullOrEmpty(name)) return;

        int lvl = _selectedEntity?.Level ?? 0;
        string displayName = (lvl > 0) ? $"{name} [{lvl}]" : name;

        int barW = 320;
        int barH = 18;
        int barX = (screenW - barW) / 2;
        int barY = 64;

        // Имя цели с уровнем
        var nameSize = font.MeasureString(displayName);
        sb.DrawString(font, displayName, new Vector2(barX + (barW - nameSize.X) / 2, barY - 16), Color.White);

        // Портал: куда ведёт
        if (_selectedEntity?.Type == "portal" && !string.IsNullOrEmpty(_selectedEntity.Info))
        {
            string destText = $"Ведёт: {_selectedEntity.Info}";
            var destSize = font.MeasureString(destText);
            sb.DrawString(font, destText, new Vector2(barX + (barW - destSize.X) / 2, barY - 16 - font.LineSpacing - 2), new Color(120, 200, 255));
        }

        // Фон полосы
        sb.Draw(SpriteCache.Pixel, new Rectangle(barX, barY, barW, barH), new Color(40, 20, 20));
        // Заполнение HP
        float pct = maxHp > 0 ? Math.Clamp((float)hp / maxHp, 0, 1) : 0;
        sb.Draw(SpriteCache.Pixel, new Rectangle(barX, barY, (int)(barW * pct), barH), new Color(200, 40, 40));
        // Рамка
        UIHelper.DrawRectOutline(sb, new Rectangle(barX, barY, barW, barH), new Color(120, 120, 130));

        // Текст HP
        string hpText = $"{hp} / {maxHp}";
        var hpSize = font.MeasureString(hpText);
        sb.DrawString(font, hpText, new Vector2(barX + (barW - hpSize.X) / 2, barY + (barH - hpSize.Y) / 2), Color.White);


    }

    public void DrawZoneIndicator(SpriteBatch sb, int screenW)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        if (_instanceExpiresAtUtcMs.HasValue && _instanceExpiresAtUtcMs > 0)
        {
            double nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int remainingMs = (int)(_instanceExpiresAtUtcMs.Value - nowMs);
            if (remainingMs < 0) remainingMs = 0;
            int totalSec = remainingMs / 1000;
            int min = totalSec / 60;
            int sec = totalSec % 60;
            string timer = $"Осталось {min:D2}:{sec:D2}";
            var tsz = font.MeasureString(timer);
            int tx = (int)((screenW - tsz.X) / 2);
            int ty = 4;
            Color tc = totalSec < 60 ? new Color(220, 60, 60) : new Color(255, 200, 80);
            sb.DrawString(font, timer, new Vector2(tx, ty), tc);
        }

        // Ping — bottom-right, above hotbar
        int ping = GameMain.Instance?.Network.PingMs ?? 0;
        if (ping > 0)
        {
            var pfont = SpriteCache.FontSmall ?? font;
            Color pc = ping < 60 ? Color.LimeGreen : (ping < 120 ? Color.Yellow : Color.OrangeRed);
            string pStr = $"{ping}ms";
            var ps = pfont.MeasureString(pStr);
            int screenH = GameMain.Instance?.Graphics?.PreferredBackBufferHeight ?? 720;
            sb.DrawString(pfont, pStr, new Vector2(screenW - ps.X - 10, screenH - 80), pc);
        }
    }

    private static string RankAdjustDesc(string desc, double mult)
    {
        if (Math.Abs(mult - 1.0) < 0.001) return desc;
        return Regex.Replace(desc, @"(\d+(?:[.,]\d+)?)\s*%", m =>
        {
            if (double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                return $"{val * mult:F0}%";
            return m.Value;
        });
    }
}
