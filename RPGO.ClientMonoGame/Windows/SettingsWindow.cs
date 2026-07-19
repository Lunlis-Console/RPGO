using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Windows;

public class SettingsWindow : GameWindow
{
    private static readonly (int w, int h)[] Resolutions =
    {
        (800, 600), (1024, 768), (1280, 720), (1366, 768),
        (1600, 900), (1920, 1080), (2560, 1440)
    };
    private static readonly string[] ModeLabels = { "Оконный", "Полноэкранный", "Без рамок" };
    private static readonly string[] ModeValues = { "windowed", "fullscreen", "borderless" };

    private int _modeIndex;
    private int _resIndex;

    private ComboWidget? _openCombo;
    private MouseState _prevMouse;

    public Action? ApplyRequested { get; set; }
    public Action? LogoutRequested { get; set; }

    public string SelectedMode => ModeValues[MathHelper.Clamp(_modeIndex, 0, ModeValues.Length - 1)];
    public (int w, int h) SelectedResolution => Resolutions[MathHelper.Clamp(_resIndex, 0, Resolutions.Length - 1)];

    private static readonly Color CField = new(40, 42, 54);
    private static readonly Color CFieldBorder = new(80, 90, 110);
    private static readonly Color CDanger = new(150, 60, 60);
    private static readonly Color CDangerHover = new(190, 80, 80);
    private static readonly Color CGreen = new(0, 150, 90);
    private static readonly Color CGreenHover = new(0, 180, 110);
    private static readonly Color CDropBg = new(35, 37, 48);
    private static readonly Color CDropHover = new(55, 60, 80);
    private static readonly Color CLight = new(210, 210, 220);
    private static readonly Color CGold = new(220, 200, 120);
    private static readonly Color CArrow = new(140, 150, 170);

    public SettingsWindow()
    {
        Title = "Настройки";
        Width = 380;
        Height = 400;
        Visible = false;

        var s = SettingsManager.Load();
        _modeIndex = Array.IndexOf(ModeValues, s.Mode);
        if (_modeIndex < 0) _modeIndex = 0;
        _resIndex = IndexForResolution(s.Width, s.Height);
    }

    private int IndexForResolution(int w, int h)
    {
        for (int i = 0; i < Resolutions.Length; i++)
            if (Resolutions[i].w == w && Resolutions[i].h == h) return i;
        return 2;
    }

    public bool IsComboOpen => _openCombo != null && _openCombo.IsOpen;

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) { base.Update(gameTime, keyboard, mouse); return; }

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

        // --- 1. Если dropdown открыт — ТОЛЬКО он обрабатывает клики ---
        if (_openCombo != null && _openCombo.IsOpen)
        {
            if (clicked)
            {
                int itemH = 22;
                var comboBounds = _openCombo.Bounds;
                var dropRect = new Rectangle(comboBounds.X, comboBounds.Bottom,
                    comboBounds.Width, _openCombo.Items.Length * itemH);

                if (comboBounds.Contains(mouse.X, mouse.Y))
                {
                    // Клик по полю — закрыть
                    _openCombo.IsOpen = false;
                    _openCombo = null;
                }
                else if (dropRect.Contains(mouse.X, mouse.Y))
                {
                    // Клик по элементу — выбрать
                    int idx = (mouse.Y - dropRect.Y) / itemH;
                    if (idx >= 0 && idx < _openCombo.Items.Length)
                    {
                        _openCombo.SelectedIndex = idx;
                        _openCombo.OnSelect?.Invoke(idx);
                    }
                    _openCombo.IsOpen = false;
                    _openCombo = null;
                }
                else
                {
                    // Клик за пределами — закрыть
                    _openCombo.IsOpen = false;
                    _openCombo = null;
                }
            }
            _prevMouse = mouse;
            return; // Блокируем ВСЁ: base.Update, кнопки, и т.д.
        }

        // --- 2. Обычный режим: кнопки/элементы окна ---
        if (clicked)
        {
            int cx = ContentX;
            int cy = ContentY;
            int cw = ContentW;

            // Комбобокс режима
            int y = cy + 22 + 16;
            var modeRect = new Rectangle(cx, y, cw, 26);
            if (modeRect.Contains(mouse.X, mouse.Y))
            {
                _openCombo = new ComboWidget(modeRect, ModeLabels, _modeIndex, idx => _modeIndex = idx);
                _openCombo.IsOpen = true;
                _prevMouse = mouse;
                return;
            }

            // Комбобокс разрешения
            y += 34 + 16;
            var resRect = new Rectangle(cx, y, cw, 26);
            if (resRect.Contains(mouse.X, mouse.Y))
            {
                var resLabels = Resolutions.Select(r => $"{r.w} x {r.h}").ToArray();
                _openCombo = new ComboWidget(resRect, resLabels, _resIndex, idx => _resIndex = idx);
                _openCombo.IsOpen = true;
                _prevMouse = mouse;
                return;
            }

            // Кнопки — позиция считается ТАК ЖЕ как в Draw
            y += 34;
            y += 16; // разделитель + отступ
            int btnH = 30;
            int btnW = (cw - 10) / 2;

            // Кнопка Применить
            var applyRect = new Rectangle(cx, y, btnW, btnH);
            if (applyRect.Contains(mouse.X, mouse.Y))
            {
                ApplyRequested?.Invoke();
                _prevMouse = mouse;
                return;
            }

            // Кнопка Выход в меню
            var exitRect = new Rectangle(cx + btnW + 10, y, btnW, btnH);
            if (exitRect.Contains(mouse.X, mouse.Y))
            {
                Visible = false;
                LogoutRequested?.Invoke();
                _prevMouse = mouse;
                return;
            }
        }

        // --- 3. drag заголовка и т.д. ---
        base.Update(gameTime, keyboard, mouse);
        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;

        base.Draw(sb);
        if (!Visible) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int cx = ContentX;
        int cy = ContentY;
        int cw = ContentW;
        var ms = Mouse.GetState();

        int y = cy;
        DrawText(sb, "ЭКРАН", cx, y, CGold, font);
        y += 22;

        DrawText(sb, "Режим:", cx, y, CLight, font);
        y += 16;
        var modeRect = new Rectangle(cx, y, cw, 26);
        DrawComboField(sb, font, modeRect, ModeLabels, _modeIndex, ms);
        y += 34;

        DrawText(sb, "Разрешение:", cx, y, CLight, font);
        y += 16;
        var resRect = new Rectangle(cx, y, cw, 26);
        var resItems = Resolutions.Select(r => $"{r.w} x {r.h}").ToArray();
        DrawComboField(sb, font, resRect, resItems, _resIndex, ms);
        y += 34;

        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, y + 4, cw, 1), CFieldBorder);
        y += 16;

        int btnH = 30;
        int btnW = (cw - 10) / 2;

        var applyRect2 = new Rectangle(cx, y, btnW, btnH);
        bool applyHover = applyRect2.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, applyRect2, applyHover ? CGreenHover : CGreen);
        DrawText(sb, "Применить",
            applyRect2.X + (btnW - (int)font.MeasureString("Применить").X) / 2,
            applyRect2.Y + (btnH - (int)font.MeasureString("Применить").Y) / 2,
            Color.White, font);

        var exitRect2 = new Rectangle(cx + btnW + 10, y, btnW, btnH);
        bool exitHover = exitRect2.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, exitRect2, exitHover ? CDangerHover : CDanger);
        DrawText(sb, "Выход в меню",
            exitRect2.X + (btnW - (int)font.MeasureString("Выход в меню").X) / 2,
            exitRect2.Y + (btnH - (int)font.MeasureString("Выход в меню").Y) / 2,
            Color.White, font);

        // --- Dropdown: рисуется ПОСЛЕ ВСЕГО, обрезается по окну ---
        if (_openCombo != null && _openCombo.IsOpen)
        {
            int itemH = 22;
            int dropH = _openCombo.Items.Length * itemH;
            var dropRect = new Rectangle(_openCombo.Bounds.X, _openCombo.Bounds.Bottom,
                _openCombo.Bounds.Width, dropH);

            var windowRect = new Rectangle(X, Y + TitleH, Width, Height - TitleH);
            sb.End();
            var oldScissor = sb.GraphicsDevice.ScissorRectangle;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
                new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
            sb.GraphicsDevice.ScissorRectangle = windowRect;

            sb.Draw(SpriteCache.Pixel, dropRect, CDropBg);
            sb.Draw(SpriteCache.Pixel, new Rectangle(dropRect.X, dropRect.Y, dropRect.Width, 1), CFieldBorder);
            sb.Draw(SpriteCache.Pixel, new Rectangle(dropRect.X, dropRect.Bottom - 1, dropRect.Width, 1), CFieldBorder);
            sb.Draw(SpriteCache.Pixel, new Rectangle(dropRect.X, dropRect.Y, 1, dropRect.Height), CFieldBorder);
            sb.Draw(SpriteCache.Pixel, new Rectangle(dropRect.Right - 1, dropRect.Y, 1, dropRect.Height), CFieldBorder);

            for (int i = 0; i < _openCombo.Items.Length; i++)
            {
                var itemRect = new Rectangle(dropRect.X, dropRect.Y + i * itemH, dropRect.Width, itemH);
                bool itemHover = itemRect.Contains(ms.X, ms.Y);
                bool isSelected = i == _openCombo.SelectedIndex;

                if (isSelected)
                    sb.Draw(SpriteCache.Pixel, itemRect, new Color(40, 60, 100));
                else if (itemHover)
                    sb.Draw(SpriteCache.Pixel, itemRect, CDropHover);

                Color textColor = isSelected ? Color.White : CLight;
                DrawText(sb, _openCombo.Items[i], itemRect.X + 8,
                    itemRect.Y + (itemH - (int)font.MeasureString(_openCombo.Items[i]).Y) / 2,
                    textColor, font);
            }

            sb.End();
            sb.GraphicsDevice.ScissorRectangle = oldScissor;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }
    }

    private void DrawComboField(SpriteBatch sb, SpriteFont font, Rectangle bounds, string[] items, int selectedIndex, MouseState mouse)
    {
        bool hovered = bounds.Contains(mouse.X, mouse.Y);
        bool isOpen = _openCombo != null && _openCombo.IsOpen && _openCombo.Bounds == bounds;

        sb.Draw(SpriteCache.Pixel, bounds, hovered || isOpen ? new Color(50, 52, 65) : CField);
        sb.Draw(SpriteCache.Pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), CFieldBorder);
        sb.Draw(SpriteCache.Pixel, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), CFieldBorder);
        sb.Draw(SpriteCache.Pixel, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), CFieldBorder);
        sb.Draw(SpriteCache.Pixel, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), CFieldBorder);

        string text = selectedIndex >= 0 && selectedIndex < items.Length ? items[selectedIndex] : "";
        DrawText(sb, text, bounds.X + 8, bounds.Y + (bounds.Height - (int)font.MeasureString(text).Y) / 2, Color.White, font);

        int arrowSize = 8;
        int arrowX = bounds.Right - arrowSize - 10;
        int arrowY = bounds.Y + (bounds.Height - arrowSize) / 2;
        DrawArrowDown(sb, arrowX, arrowY, arrowSize, isOpen ? Color.White : CArrow);
    }

    private static void DrawArrowDown(SpriteBatch sb, int x, int y, int size, Color color)
    {
        for (int i = 0; i < size; i++)
        {
            int startX = x + i;
            int endX = x + size - 1 - i;
            int drawY = y + i;
            sb.Draw(SpriteCache.Pixel, new Rectangle(startX, drawY, Math.Max(1, endX - startX + 1), 1), color);
        }
    }

    private class ComboWidget
    {
        public Rectangle Bounds;
        public string[] Items;
        public int SelectedIndex;
        public bool IsOpen;
        public Action<int> OnSelect;

        public ComboWidget(Rectangle bounds, string[] items, int selected, Action<int> onSelect)
        {
            Bounds = bounds;
            Items = items;
            SelectedIndex = selected;
            OnSelect = onSelect;
        }
    }
}
