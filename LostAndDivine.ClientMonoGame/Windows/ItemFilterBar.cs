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
/// Универсальная строка фильтров предметов: поиск по названию (RU/EN), категория,
/// диапазон требуемого уровня, сортировка по цене и сброс.
/// Одна реализация используется в магазине, инвентаре и складе.
/// </summary>
public class ItemFilterBar
{
    public bool EnableSearch { get; set; } = true;
    public bool EnableCategory { get; set; } = true;
    public bool EnableLevel { get; set; } = true;
    public bool EnablePriceSort { get; set; } = true;
    public bool EnableReset { get; set; } = true;

    private string _searchText = "";
    private bool _searchActive;
    private HashSet<uint> _prevSearchVks = new();
    private int _categoryFilter;   // 0 = Все, 1..4 см. CategoryLabels
    private int _levelFilter;      // 0 = Все, 1..N см. LevelLabels (диапазоны по 5)
    private int _priceSort;        // 0 = порядок сервера, 1 = по цене ▲, 2 = по цене ▼
    private bool _catDropdownOpen;
    private bool _levelDropdownOpen;

    private Rectangle _searchRect;
    private Rectangle _catRect;
    private Rectangle _levelRect;
    private Rectangle _sortRect;
    private Rectangle _resetRect;
    private Rectangle[] _catOptionRects = Array.Empty<Rectangle>();
    private Rectangle[] _levelOptionRects = Array.Empty<Rectangle>();

    private bool _lastBackUp;
    private bool _prevEscDown;
    private bool _prevEnterDown;

    /// <summary>Поле поиска активно — Esc обрабатывает его (очистка/снятие фокуса), а не закрывает окно.</summary>
    public bool ConsumesEscape => _searchActive;

    public bool IsSearchActive => _searchActive;

    public bool IsAnyDropdownOpen => _catDropdownOpen || _levelDropdownOpen;

    /// <summary>Есть ли применённые фильтры или открытые списки (для подсветки сброса).</summary>
    public bool IsActive
        => _searchText.Length > 0 || _categoryFilter != 0 || _levelFilter != 0 || _priceSort != 0
           || _catDropdownOpen || _levelDropdownOpen;

    private static readonly string[] CategoryLabels =
        { "Все", "Оружие", "Броня/щиты", "Расходники", "Материалы" };

    // Диапазоны требуемого уровня по 5: индекс 0 = Все, 1 = 1-5, 2 = 6-10 ...
    private static readonly string[] LevelLabels = BuildLevelLabels();
    private static string[] BuildLevelLabels()
    {
        var labels = new List<string> { "Все" };
        for (int i = 1; i <= 10; i++)
            labels.Add($"{(i - 1) * 5 + 1}-{i * 5}");
        return labels.ToArray();
    }

    /// <summary>Полный сброс всех фильтров.</summary>
    public void Reset()
    {
        _searchText = "";
        _searchActive = false;
        _categoryFilter = 0;
        _levelFilter = 0;
        _priceSort = 0;
        _catDropdownOpen = false;
        _levelDropdownOpen = false;
    }

    // ----- Фильтрация -----

    public bool Matches(Item item)
    {
        if (_categoryFilter != 0 && !MatchCategory(item.Type, _categoryFilter)) return false;
        if (_levelFilter != 0 && !MatchLevelBucket(item.RequiredLevel, _levelFilter)) return false;
        if (_searchText.Length > 0 && !item.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>Фильтрует список и применяет сортировку по цене, если она задана.</summary>
    public List<Item> Filter(IEnumerable<Item> items)
    {
        var list = items.Where(Matches).ToList();
        if (_priceSort == 1) list = list.OrderBy(i => i.Value).ToList();
        else if (_priceSort == 2) list = list.OrderByDescending(i => i.Value).ToList();
        return list;
    }

    private static bool MatchLevelBucket(int requiredLevel, int bucket)
    {
        if (bucket <= 0) return true;
        if (requiredLevel == 0) return true; // без ограничений — доступен на любом уровне
        return requiredLevel >= (bucket - 1) * 5 + 1 && requiredLevel <= bucket * 5;
    }

    private static bool MatchCategory(string itemType, int filter) => filter switch
    {
        1 => itemType is "weapon" or "twohand",
        2 => itemType is "shield" or "helmet" or "cloak" or "chest" or "legs" or "boots"
             or "glove" or "belt" or "necklace" or "ring" or "armor",
        3 => itemType == "consumable",
        4 => itemType is "material" or "collectible" or "trophy",
        _ => true
    };

    // ----- Раскладка -----

    // Базовые ширины контролов: подгоняются масштабом под доступную ширину строки.
    private const int SearchW = 180;
    private const int CategoryW = 104;
    private const int LevelW = 80;
    private const int SortW = 50;
    private const int ResetW = 32;
    private const int Gap = 4;

    private void Layout(Rectangle bar)
    {
        int baseTotal = 0;
        int count = 0;
        if (EnableSearch) { baseTotal += SearchW; count++; }
        if (EnableCategory) { baseTotal += CategoryW; count++; }
        if (EnableLevel) { baseTotal += LevelW; count++; }
        if (EnablePriceSort) { baseTotal += SortW; count++; }
        if (EnableReset) { baseTotal += ResetW; count++; }
        baseTotal += Gap * Math.Max(0, count - 1);

        float scale = bar.Width >= baseTotal ? 1f : bar.Width / (float)Math.Max(1, baseTotal);
        int x = bar.X;

        if (EnableSearch)
        {
            _searchRect = new Rectangle(x, bar.Y, (int)(SearchW * scale), bar.Height);
            x += _searchRect.Width + Gap;
        }
        if (EnableCategory)
        {
            _catRect = new Rectangle(x, bar.Y, (int)(CategoryW * scale), bar.Height);
            x += _catRect.Width + Gap;
        }
        if (EnableLevel)
        {
            _levelRect = new Rectangle(x, bar.Y, (int)(LevelW * scale), bar.Height);
            x += _levelRect.Width + Gap;
        }
        if (EnablePriceSort)
        {
            _sortRect = new Rectangle(x, bar.Y, (int)(SortW * scale), bar.Height);
            x += _sortRect.Width + Gap;
        }
        if (EnableReset)
        {
            _resetRect = new Rectangle(x, bar.Y, (int)(ResetW * scale), bar.Height);
            x += _resetRect.Width + Gap;
        }

        _catOptionRects = new Rectangle[CategoryLabels.Length];
        for (int i = 0; i < CategoryLabels.Length; i++)
            _catOptionRects[i] = new Rectangle(_catRect.X, _catRect.Bottom + i * 22, _catRect.Width, 22);

        _levelOptionRects = new Rectangle[LevelLabels.Length];
        for (int i = 0; i < LevelLabels.Length; i++)
            _levelOptionRects[i] = new Rectangle(_levelRect.X, _levelRect.Bottom + i * 22, _levelRect.Width, 22);
    }

    // ----- Ввод -----

    /// <summary>
    /// Обрабатывает клики по контролам фильтра и ввод текста поиска.
    /// Возвращает true, если клик обработан фильтром (окно не должно использовать его дальше).
    /// </summary>
    public bool Update(MouseState mouse, KeyboardState keyboard, MouseState prevMouse, Rectangle barRect)
    {
        Layout(barRect);

        bool pressed = mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released;

        if (pressed)
        {
            // Клик мимо открытого списка — закрыть его
            if (_catDropdownOpen && !_catOptionRects.Any(r => r.Contains(mouse.X, mouse.Y)))
                _catDropdownOpen = false;
            if (_levelDropdownOpen && !_levelOptionRects.Any(r => r.Contains(mouse.X, mouse.Y)))
                _levelDropdownOpen = false;

            if (EnableSearch && _searchRect.Contains(mouse.X, mouse.Y))
            {
                _searchActive = true;
                return true;
            }
            if (EnableCategory && _catRect.Contains(mouse.X, mouse.Y))
            {
                _catDropdownOpen = !_catDropdownOpen;
                _levelDropdownOpen = false;
                return true;
            }
            if (EnableLevel && _levelRect.Contains(mouse.X, mouse.Y))
            {
                _levelDropdownOpen = !_levelDropdownOpen;
                _catDropdownOpen = false;
                return true;
            }
            if (EnableCategory && _catDropdownOpen)
            {
                for (int i = 0; i < _catOptionRects.Length; i++)
                {
                    if (_catOptionRects[i].Contains(mouse.X, mouse.Y))
                    {
                        _categoryFilter = i;
                        _catDropdownOpen = false;
                        return true;
                    }
                }
            }
            if (EnableLevel && _levelDropdownOpen)
            {
                for (int i = 0; i < _levelOptionRects.Length; i++)
                {
                    if (_levelOptionRects[i].Contains(mouse.X, mouse.Y))
                    {
                        _levelFilter = i;
                        _levelDropdownOpen = false;
                        return true;
                    }
                }
            }
            if (EnablePriceSort && _sortRect.Contains(mouse.X, mouse.Y))
            {
                _priceSort = (_priceSort + 1) % 3;
                return true;
            }
            if (EnableReset && _resetRect.Contains(mouse.X, mouse.Y))
            {
                Reset();
                return true;
            }
            _searchActive = false;
        }

        HandleSearchInput(keyboard);
        return false;
    }

    // Ввод русского текста как в чате: VK-коды через GetAsyncKeyState + таблица KeyCharMap
    private void HandleSearchInput(KeyboardState keyboard)
    {
        if (!_searchActive) return;

        bool russian = KeyboardLayoutHelper.IsRussianForeground();
        bool shiftDown = KeyboardLayoutHelper.IsShiftDown();
        var nowDown = new HashSet<uint>(KeyboardLayoutHelper.GetPressedVks());
        foreach (var vk in nowDown)
        {
            if (_prevSearchVks.Contains(vk)) continue;
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14 ||
                vk == 0x09 || vk == 0x0D || vk == 0x1B || vk == 0x08)
                continue;
            if (KeyCharMap.TryGetCharByVk(vk, russian, shiftDown, out char ch))
            {
                if (_searchText.Length < 30) _searchText += ch;
            }
        }
        _prevSearchVks = nowDown;

        if (keyboard.IsKeyDown(Keys.Back) && !_lastBackUp && _searchText.Length > 0)
            _searchText = _searchText[..^1];
        _lastBackUp = keyboard.IsKeyDown(Keys.Back);

        if (keyboard.IsKeyDown(Keys.Escape) && !_prevEscDown && _searchText.Length > 0)
            _searchText = "";
        else if (keyboard.IsKeyDown(Keys.Escape) && !_prevEscDown)
        {
            _searchActive = false;
            _catDropdownOpen = false;
            _levelDropdownOpen = false;
        }
        _prevEscDown = keyboard.IsKeyDown(Keys.Escape);

        if (keyboard.IsKeyDown(Keys.Enter) && !_prevEnterDown)
            _searchActive = false;
        _prevEnterDown = keyboard.IsKeyDown(Keys.Enter);
    }

    // ----- Отрисовка -----

    public void Draw(SpriteBatch sb, MouseState mouse, Rectangle barRect)
    {
        Layout(barRect);

        if (EnableSearch) DrawSearchBox(sb, mouse);
        if (EnableCategory) DrawDropdown(sb, mouse, _catRect, CategoryLabels[_categoryFilter], _catDropdownOpen);
        if (EnableLevel) DrawDropdown(sb, mouse, _levelRect, LevelLabels[_levelFilter], _levelDropdownOpen);
        if (EnablePriceSort) DrawPriceSort(sb, mouse);
        if (EnableReset) DrawReset(sb, mouse);

        // Выпадающие списки — поверх остального контента окна
        DrawDropdowns(sb, mouse);
    }

    private void DrawSearchBox(SpriteBatch sb, MouseState mouse)
    {
        var f = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (f == null) return;

        bool hover = _searchRect.Contains(mouse.X, mouse.Y);
        sb.Draw(SpriteCache.Pixel, _searchRect, _searchActive ? new Color(60, 55, 75) : hover ? new Color(55, 50, 68) : new Color(45, 40, 55));
        sb.Draw(SpriteCache.Pixel, new Rectangle(_searchRect.X, _searchRect.Y, _searchRect.Width, 2),
            _searchActive ? new Color(180, 150, 90) : new Color(90, 75, 50));

        if (_searchText.Length > 0)
        {
            DrawText(sb, _searchText, _searchRect.X + 6, _searchRect.Y + (_searchRect.Height - 14) / 2, Color.White);
            int tw = (int)f.MeasureString(_searchText).X;
            DrawText(sb, "|", _searchRect.X + 6 + tw + 1, _searchRect.Y + (_searchRect.Height - 14) / 2, new Color(220, 200, 140));
        }
        else
        {
            DrawText(sb, _searchActive ? "Введите название..." : "Поиск по названию...", _searchRect.X + 6,
                _searchRect.Y + (_searchRect.Height - 14) / 2, new Color(140, 130, 120));
        }
        DrawText(sb, KeyboardLayoutHelper.IsRussianForeground() ? "RU" : "EN",
            _searchRect.Right - 24, _searchRect.Y + (_searchRect.Height - 14) / 2, new Color(110, 150, 110));
    }

    private static void DrawDropdown(SpriteBatch sb, MouseState mouse, Rectangle rect, string label, bool open)
    {
        bool hover = rect.Contains(mouse.X, mouse.Y);
        sb.Draw(SpriteCache.Pixel, rect, hover || open ? new Color(60, 55, 75) : new Color(45, 40, 55));
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(90, 75, 50));
        DrawText(sb, (open ? "- " : "+ ") + label, rect.X + 6, rect.Y + (rect.Height - 14) / 2, Color.White);
    }

    private void DrawPriceSort(SpriteBatch sb, MouseState mouse)
    {
        bool hover = _sortRect.Contains(mouse.X, mouse.Y);
        sb.Draw(SpriteCache.Pixel, _sortRect, hover ? new Color(55, 50, 68) : new Color(45, 40, 55));
        sb.Draw(SpriteCache.Pixel, new Rectangle(_sortRect.X, _sortRect.Y, _sortRect.Width, 2), new Color(90, 75, 50));
        string sortLabel = _priceSort switch { 1 => "^", 2 => "v", _ => "-" };
        DrawText(sb, "Цена " + sortLabel, _sortRect.X + 4, _sortRect.Y + (_sortRect.Height - 14) / 2, Color.White);
    }

    private void DrawReset(SpriteBatch sb, MouseState mouse)
    {
        bool hover = _resetRect.Contains(mouse.X, mouse.Y);
        sb.Draw(SpriteCache.Pixel, _resetRect, hover ? new Color(70, 50, 50) : new Color(50, 42, 40));
        sb.Draw(SpriteCache.Pixel, new Rectangle(_resetRect.X, _resetRect.Y, _resetRect.Width, 2), new Color(110, 70, 60));
        DrawText(sb, "X", _resetRect.X + (_resetRect.Width - 10) / 2, _resetRect.Y + (_resetRect.Height - 14) / 2,
            new Color(230, 160, 140));
    }

    private void DrawDropdowns(SpriteBatch sb, MouseState mouse)
    {
        if (_catDropdownOpen)
        {
            sb.Draw(SpriteCache.Pixel, new Rectangle(_catRect.X - 2, _catRect.Y - 2, _catRect.Width + 4,
                CategoryLabels.Length * 22 + 4), new Color(160, 130, 80));
            for (int i = 0; i < _catOptionRects.Length; i++)
            {
                var r = _catOptionRects[i];
                bool hover = r.Contains(mouse.X, mouse.Y);
                sb.Draw(SpriteCache.Pixel, r, i == _categoryFilter ? new Color(80, 65, 45) : hover ? new Color(60, 55, 75) : new Color(40, 36, 50));
                DrawText(sb, CategoryLabels[i], r.X + 4, r.Y + 3, i == _categoryFilter ? new Color(230, 200, 130) : Color.White);
            }
        }

        if (_levelDropdownOpen)
        {
            sb.Draw(SpriteCache.Pixel, new Rectangle(_levelRect.X - 2, _levelRect.Y - 2, _levelRect.Width + 4,
                LevelLabels.Length * 22 + 4), new Color(160, 130, 80));
            for (int i = 0; i < _levelOptionRects.Length; i++)
            {
                var r = _levelOptionRects[i];
                bool hover = r.Contains(mouse.X, mouse.Y);
                sb.Draw(SpriteCache.Pixel, r, i == _levelFilter ? new Color(80, 65, 45) : hover ? new Color(60, 55, 75) : new Color(40, 36, 50));
                DrawText(sb, LevelLabels[i], r.X + 4, r.Y + 3, i == _levelFilter ? new Color(230, 200, 130) : Color.White);
            }
        }
    }

    private static void DrawText(SpriteBatch sb, string text, int x, int y, Color color)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;
        sb.DrawString(font, text, new Vector2(x, y), color);
    }
}