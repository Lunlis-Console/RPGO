using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Screens;

public class SettingsScreen : IScreen
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
    private KeyboardState _prevKeyboard;

    private readonly SettingsManager _settings;

    private static readonly Color CPanel = new(30, 32, 44);
    private static readonly Color CField = new(40, 42, 54);
    private static readonly Color CFieldBorder = new(80, 90, 110);
    private static readonly Color CGreen = new(0, 150, 90);
    private static readonly Color CGreenHover = new(0, 180, 110);
    private static readonly Color CDanger = new(150, 60, 60);
    private static readonly Color CDangerHover = new(190, 80, 80);
    private static readonly Color CDropBg = new(35, 37, 48);
    private static readonly Color CDropHover = new(55, 60, 80);
    private static readonly Color CLight = new(210, 210, 220);
    private static readonly Color CGold = new(220, 200, 120);
    private static readonly Color CArrow = new(140, 150, 170);

    public SettingsScreen()
    {
        _settings = SettingsManager.Load();
        _modeIndex = Array.IndexOf(ModeValues, _settings.Mode);
        if (_modeIndex < 0) _modeIndex = 0;
        _resIndex = IndexForResolution(_settings.Width, _settings.Height);
    }

    private int IndexForResolution(int w, int h)
    {
        for (int i = 0; i < Resolutions.Length; i++)
            if (Resolutions[i].w == w && Resolutions[i].h == h) return i;
        return 2;
    }

    public void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (keyboard.IsKeyDown(Keys.Escape) && _prevKeyboard.IsKeyUp(Keys.Escape))
        {
            Close();
            return;
        }

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

        var g = GameMain.Instance!.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;
        int pw = 360;
        int ph = 280;
        int px = (sw - pw) / 2;
        int py = (sh - ph) / 2;
        int cx = px + 20;
        int cw = pw - 40;

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
                    _openCombo.IsOpen = false;
                    _openCombo = null;
                }
                else if (dropRect.Contains(mouse.X, mouse.Y))
                {
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
                    _openCombo.IsOpen = false;
                    _openCombo = null;
                }
            }
            _prevMouse = mouse;
            _prevKeyboard = keyboard;
            return;
        }

        // --- 2. Обычный режим ---
        if (clicked)
        {
            int y = py + 44;
            y += 16;

            // Комбобокс режима
            var modeRect = new Rectangle(cx, y, cw, 26);
            if (modeRect.Contains(mouse.X, mouse.Y))
            {
                _openCombo = new ComboWidget(modeRect, ModeLabels, _modeIndex, idx => _modeIndex = idx);
                _openCombo.IsOpen = true;
                _prevMouse = mouse;
                _prevKeyboard = keyboard;
                return;
            }

            // Комбобокс разрешения
            y += 34;
            y += 16;
            var resRect = new Rectangle(cx, y, cw, 26);
            if (resRect.Contains(mouse.X, mouse.Y))
            {
                var resLabels = Resolutions.Select(r => $"{r.w} x {r.h}").ToArray();
                _openCombo = new ComboWidget(resRect, resLabels, _resIndex, idx => _resIndex = idx);
                _openCombo.IsOpen = true;
                _prevMouse = mouse;
                _prevKeyboard = keyboard;
                return;
            }

            // Кнопки
            y += 34;
            y += 16;
            int btnH = 30;
            int btnW = (cw - 10) / 2;

            var applyRect = new Rectangle(cx, y, btnW, btnH);
            if (applyRect.Contains(mouse.X, mouse.Y))
            {
                Apply();
                _prevMouse = mouse;
                _prevKeyboard = keyboard;
                return;
            }

            var closeRect = new Rectangle(cx + btnW + 10, y, btnW, btnH);
            if (closeRect.Contains(mouse.X, mouse.Y))
            {
                Close();
                _prevMouse = mouse;
                _prevKeyboard = keyboard;
                return;
            }

            // Кнопка Выход из игры
            y += 36;
            var quitRect = new Rectangle(cx, y, cw, btnH);
            if (quitRect.Contains(mouse.X, mouse.Y))
            {
                GameMain.Instance!.Exit();
                return;
            }
        }

        _prevMouse = mouse;
        _prevKeyboard = keyboard;
    }

    private void Apply()
    {
        var g = GameMain.Instance!.Graphics;
        var (rw, rh) = Resolutions[_resIndex];

        g.PreferredBackBufferWidth = rw;
        g.PreferredBackBufferHeight = rh;

        switch (ModeValues[_modeIndex])
        {
            case "fullscreen":
                g.IsFullScreen = true;
                GameMain.Instance.Window.IsBorderless = false;
                break;
            case "borderless":
                g.IsFullScreen = true;
                GameMain.Instance.Window.IsBorderless = true;
                break;
            default:
                g.IsFullScreen = false;
                GameMain.Instance.Window.IsBorderless = false;
                break;
        }

        g.ApplyChanges();

        _settings.Width = rw;
        _settings.Height = rh;
        _settings.Mode = ModeValues[_modeIndex];
        _settings.Save();
    }

    private void Close()
    {
        GameMain.Instance!.CloseSettings();
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        spriteBatch.Begin();

        var font = SpriteCache.Font;
        if (font == null) { spriteBatch.End(); return; }

        var g = GameMain.Instance!.Graphics;
        int sw = g.PreferredBackBufferWidth;
        int sh = g.PreferredBackBufferHeight;
        int pw = 360;
        int ph = 280;
        int px = (sw - pw) / 2;
        int py = (sh - ph) / 2;
        int cx = px + 20;
        int cw = pw - 40;
        var ms = Mouse.GetState();

        // Затемнение фона
        spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(0, 0, sw, sh), new Color(0, 0, 0, 150));

        // Панель
        spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(px, py, pw, ph), CPanel);
        DrawBorder(spriteBatch, new Rectangle(px, py, pw, ph), new Color(90, 95, 115));

        spriteBatch.DrawString(font, "Настройки", new Vector2(px + 20, py + 14), CGold);

        // --- Комбобокс режима ---
        int y = py + 44;
        spriteBatch.DrawString(font, "Режим:", new Vector2(cx, y - 2), CLight);
        y += 16;
        var modeRect = new Rectangle(cx, y, cw, 26);
        DrawComboField(spriteBatch, font, modeRect, ModeLabels, _modeIndex, ms);
        y += 34;

        // --- Комбобокс разрешения ---
        spriteBatch.DrawString(font, "Разрешение:", new Vector2(cx, y - 2), CLight);
        y += 16;
        var resRect = new Rectangle(cx, y, cw, 26);
        var resItems = Resolutions.Select(r => $"{r.w} x {r.h}").ToArray();
        DrawComboField(spriteBatch, font, resRect, resItems, _resIndex, ms);
        y += 34;

        // Разделитель
        spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(cx, y + 4, cw, 1), CFieldBorder);
        y += 16;

        // Кнопки
        int btnH = 30;
        int btnW = (cw - 10) / 2;

        var applyRect2 = new Rectangle(cx, y, btnW, btnH);
        bool applyHover = applyRect2.Contains(ms.X, ms.Y);
        spriteBatch.Draw(SpriteCache.Pixel, applyRect2, applyHover ? CGreenHover : CGreen);
        DrawText(spriteBatch, "Применить",
            applyRect2.X + (btnW - (int)font.MeasureString("Применить").X) / 2,
            applyRect2.Y + (btnH - (int)font.MeasureString("Применить").Y) / 2,
            Color.White, font);

        var closeRect2 = new Rectangle(cx + btnW + 10, y, btnW, btnH);
        bool closeHover = closeRect2.Contains(ms.X, ms.Y);
        spriteBatch.Draw(SpriteCache.Pixel, closeRect2, closeHover ? CDangerHover : CDanger);
        DrawText(spriteBatch, "Закрыть",
            closeRect2.X + (btnW - (int)font.MeasureString("Закрыть").X) / 2,
            closeRect2.Y + (btnH - (int)font.MeasureString("Закрыть").Y) / 2,
            Color.White, font);

        // Кнопка Выход из игры
        y += 36;
        var quitRect = new Rectangle(cx, y, cw, btnH);
        bool quitHover = quitRect.Contains(ms.X, ms.Y);
        spriteBatch.Draw(SpriteCache.Pixel, quitRect, quitHover ? new Color(180, 50, 50) : new Color(130, 40, 40));
        DrawText(spriteBatch, "Выход из игры",
            quitRect.X + (cw - (int)font.MeasureString("Выход из игры").X) / 2,
            quitRect.Y + (btnH - (int)font.MeasureString("Выход из игры").Y) / 2,
            Color.White, font);

        // --- Dropdown: рисуется ПОСЛЕ ВСЕГО, обрезается по панели ---
        if (_openCombo != null && _openCombo.IsOpen)
        {
            int itemH = 22;
            int dropH = _openCombo.Items.Length * itemH;
            var dropRect = new Rectangle(_openCombo.Bounds.X, _openCombo.Bounds.Bottom,
                _openCombo.Bounds.Width, dropH);

            spriteBatch.End();
            var oldScissor = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
                new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
            spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(px, py, pw, ph);

            spriteBatch.Draw(SpriteCache.Pixel, dropRect, CDropBg);
            spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(dropRect.X, dropRect.Y, dropRect.Width, 1), CFieldBorder);
            spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(dropRect.X, dropRect.Bottom - 1, dropRect.Width, 1), CFieldBorder);
            spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(dropRect.X, dropRect.Y, 1, dropRect.Height), CFieldBorder);
            spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(dropRect.Right - 1, dropRect.Y, 1, dropRect.Height), CFieldBorder);

            for (int i = 0; i < _openCombo.Items.Length; i++)
            {
                var itemRect = new Rectangle(dropRect.X, dropRect.Y + i * itemH, dropRect.Width, itemH);
                bool itemHover = itemRect.Contains(ms.X, ms.Y);
                bool isSelected = i == _openCombo.SelectedIndex;

                if (isSelected)
                    spriteBatch.Draw(SpriteCache.Pixel, itemRect, new Color(40, 60, 100));
                else if (itemHover)
                    spriteBatch.Draw(SpriteCache.Pixel, itemRect, CDropHover);

                Color textColor = isSelected ? Color.White : CLight;
                DrawText(spriteBatch, _openCombo.Items[i], itemRect.X + 8,
                    itemRect.Y + (itemH - (int)font.MeasureString(_openCombo.Items[i]).Y) / 2,
                    textColor, font);
            }

            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = oldScissor;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        spriteBatch.End();
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

    private static void DrawBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
        => UIHelper.DrawRectOutline(sb, rect, color, thickness);

    private static void DrawText(SpriteBatch sb, string text, int x, int y, Color color, SpriteFont? font)
    {
        if (font != null && !string.IsNullOrEmpty(text))
            sb.DrawString(font, text, new Vector2(x, y), color);
    }

    public void Dispose() { }

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
