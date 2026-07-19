using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Windows;

public class LogoutConfirmWindow : GameWindow
{
    private float _remaining = 5f;
    private Rectangle _confirmRect = Rectangle.Empty;
    private Rectangle _cancelRect = Rectangle.Empty;
    private MouseState _prevMouse;

    public Action? Confirmed { get; set; }
    public Action? Cancelled { get; set; }

    public LogoutConfirmWindow()
    {
        Title = "Выход из игры";
        Width = 360;
        Height = 180;
        Visible = false;
        IsModal = true;
    }

    public void ResetTimer() => _remaining = 5f;

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        _remaining -= (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_remaining <= 0)
        {
            _remaining = 0;
            Confirmed?.Invoke();
            Visible = false;
            return;
        }

        RebuildLayout();
        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

        if (clicked)
        {
            if (_cancelRect.Contains(mouse.X, mouse.Y))
            {
                Cancelled?.Invoke();
                Visible = false;
            }
            else if (_confirmRect.Contains(mouse.X, mouse.Y))
            {
                Confirmed?.Invoke();
                Visible = false;
            }
        }

        _prevMouse = mouse;
    }

    private void RebuildLayout()
    {
        int innerX = ContentX + 4;
        int y = ContentY + 70;
        int btnW = (ContentW - 16) / 2;
        _cancelRect = new Rectangle(innerX, y, btnW, 32);
        _confirmRect = new Rectangle(innerX + btnW + 8, y, btnW, 32);
    }

    public override void Draw(SpriteBatch sb)
    {
        base.Draw(sb);
        if (!Visible) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        RebuildLayout();
        var ms = Mouse.GetState();

        string line1 = "Сохранение прогресса...";
        var s1 = font.MeasureString(line1);
        sb.DrawString(font, line1, new Vector2(X + (Width - s1.X) / 2, ContentY), Color.LightGray);

        string line2 = $"Выход через {Math.Ceiling(_remaining)} сек.";
        var s2 = font.MeasureString(line2);
        sb.DrawString(font, line2, new Vector2(X + (Width - s2.X) / 2, ContentY + 30), Color.Gold);

        DrawButton(sb, "Отмена", _cancelRect, new Color(120, 120, 130), ms, _prevMouse);
        DrawButton(sb, "Выйти сейчас", _confirmRect, new Color(150, 60, 60), ms, _prevMouse);
    }
}
