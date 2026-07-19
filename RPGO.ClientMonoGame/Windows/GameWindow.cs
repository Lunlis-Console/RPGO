using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Windows;

public class GameWindow
{
    public string Title { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 400;
    public int Height { get; set; } = 500;
    public bool Visible { get; set; }
    public bool IsModal { get; set; }

    // true, если окно в данный момент выполняет внутренний drag-n-drop
    public virtual bool IsDragging => false;

    // Вызывается при закрытии окна (клик по крестику)
    protected virtual void OnClose() { }

    public bool Contains(Point p) => new Rectangle(X, Y, Width, Height).Contains(p);

    protected const int TitleH = 28;
    private const int CloseBtnW = 20;
    private const int Border = 2;

    private bool _dragging;
    private int _dragOffX, _dragOffY;

    private static readonly Color TitleColor = new Color(45, 55, 75);
    private static readonly Color BgColor = new Color(30, 32, 40);
    private static readonly Color BorderColor = new Color(80, 90, 110);
    private static readonly Color CloseHover = new Color(200, 60, 60);
    private static readonly Color CloseNormal = new Color(140, 40, 40);

    private MouseState _prevMouse;

    public virtual void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

        var titleRect = new Rectangle(X, Y, Width, TitleH);
        var closeRect = new Rectangle(X + Width - CloseBtnW - 4, Y + 4, CloseBtnW, CloseBtnW);

        if (clicked)
        {
            if (closeRect.Contains(mouse.X, mouse.Y))
            {
                Visible = false;
                OnClose();
                _prevMouse = mouse;
                return;
            }
            if (titleRect.Contains(mouse.X, mouse.Y))
            {
                _dragging = true;
                _dragOffX = mouse.X - X;
                _dragOffY = mouse.Y - Y;
            }
        }

        if (_dragging && mouse.LeftButton == ButtonState.Pressed)
        {
            X = mouse.X - _dragOffX;
            Y = mouse.Y - _dragOffY;
        }

        if (released) _dragging = false;

        _prevMouse = mouse;
    }

    public virtual void Draw(SpriteBatch sb)
    {
        Draw(sb, Mouse.GetState());
    }

    public void Draw(SpriteBatch sb, MouseState mouse)
    {
        if (!Visible) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        var windowRect = new Rectangle(X, Y, Width, Height);

        sb.Draw(SpriteCache.Pixel, windowRect, BgColor);
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, TitleH), TitleColor);

        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, Border), BorderColor);
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y + Height - Border, Width, Border), BorderColor);
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Border, Height), BorderColor);
        sb.Draw(SpriteCache.Pixel, new Rectangle(X + Width - Border, Y, Border, Height), BorderColor);

        var titleSize = font.MeasureString(Title);
        sb.DrawString(font, Title, new Vector2(X + 8, Y + (TitleH - titleSize.Y) / 2), Color.White);

        var closeRect = new Rectangle(X + Width - CloseBtnW - 4, Y + 4, CloseBtnW, CloseBtnW);
        Color closeColor = closeRect.Contains(mouse.X, mouse.Y) ? CloseHover : CloseNormal;
        sb.Draw(SpriteCache.Pixel, closeRect, closeColor);
        var xSize = font.MeasureString("X");
        sb.DrawString(font, "X", new Vector2(closeRect.X + (closeRect.Width - xSize.X) / 2, closeRect.Y + (closeRect.Height - xSize.Y) / 2), Color.White);
    }

    protected int ContentX => X + 8;
    protected int ContentY => Y + TitleH + 4;
    protected int ContentW => Width - 16;
    protected int ContentH => Height - TitleH - 8;

    protected void DrawText(SpriteBatch sb, string text, int x, int y, Color color, SpriteFont? font = null)
    {
        var f = font ?? SpriteCache.FontSmall ?? SpriteCache.Font;
        if (f != null) sb.DrawString(f, text, new Vector2(x, y), color);
    }

    protected int DrawButton(SpriteBatch sb, string text, int x, int y, int w, int h, Color bg, MouseState mouse, MouseState prevMouse)
    {
        var rect = new Rectangle(x, y, w, h);
        sb.Draw(SpriteCache.Pixel, rect, bg);
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font != null)
        {
            var size = font.MeasureString(text);
            sb.DrawString(font, text, new Vector2(x + (w - size.X) / 2, y + (h - size.Y) / 2), Color.White);
        }
        return mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released && rect.Contains(mouse.X, mouse.Y) ? 1 : 0;
    }

    protected static int DrawButton(SpriteBatch sb, string text, Rectangle rect, Color bg, MouseState mouse, MouseState prevMouse, SpriteFont? font = null)
    {
        sb.Draw(SpriteCache.Pixel, rect, bg);
        var f = font ?? SpriteCache.FontSmall ?? SpriteCache.Font;
        if (f != null)
        {
            var size = f.MeasureString(text);
            sb.DrawString(f, text, new Vector2(rect.X + (rect.Width - size.X) / 2, rect.Y + (rect.Height - size.Y) / 2), Color.White);
        }
        return mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released && rect.Contains(mouse.X, mouse.Y) ? 1 : 0;
    }

    protected void DrawBar(SpriteBatch sb, int x, int y, int w, int h, int value, int max, Color fill, string label)
    {
        float pct = max > 0 ? Math.Clamp((float)value / max, 0, 1) : 0;
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, h), new Color(40, 40, 50));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, (int)(w * pct), h), fill);
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font != null)
        {
            var text = $"{label} {value}/{max}";
            var size = font.MeasureString(text);
            sb.DrawString(font, text, new Vector2(x + (w - size.X) / 2, y + (h - size.Y) / 2), Color.White);
        }
    }
}
