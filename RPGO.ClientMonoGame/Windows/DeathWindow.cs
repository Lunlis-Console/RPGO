using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Windows;

public class DeathWindow : GameWindow
{
    private float _remaining = 5f;
    private Rectangle _reviveRect = Rectangle.Empty;
    private MouseState _prevMouse;
    private string _lostGoldText = "";

    public Action? ReviveRequested { get; set; }

    public DeathWindow()
    {
        Title = "Вы погибли";
        Width = 280;
        Height = 140;
        Visible = false;
        IsModal = true;
    }

    public void Activate(int lostGold)
    {
        _remaining = 5f;
        _lostGoldText = lostGold > 0 ? $"Потеряно {lostGold} золота." : "";
        Visible = true;
    }

    public void Deactivate()
    {
        Visible = false;
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        _remaining -= (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_remaining <= 0)
        {
            _remaining = 0;
            ReviveRequested?.Invoke();
            Visible = false;
            return;
        }

        RebuildLayout();
        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

        if (clicked && _reviveRect.Contains(mouse.X, mouse.Y))
        {
            ReviveRequested?.Invoke();
            Visible = false;
        }

        _prevMouse = mouse;
    }

    private void RebuildLayout()
    {
        int innerX = ContentX + 4;
        int y = ContentY + ContentH - 44;
        int btnW = ContentW - 8;
        _reviveRect = new Rectangle(innerX, y, btnW, 32);
    }

    public override void Draw(SpriteBatch sb)
    {
        base.Draw(sb);
        if (!Visible) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        RebuildLayout();
        var ms = Mouse.GetState();

        int cx = X + Width / 2;
        int y = ContentY + 12;

        string line1 = "Вы погибли";
        var s1 = font.MeasureString(line1);
        sb.DrawString(font, line1, new Vector2(cx - s1.X / 2, y), new Color(200, 50, 50));
        y += (int)s1.Y + 10;

        if (!string.IsNullOrEmpty(_lostGoldText))
        {
            var sg = font.MeasureString(_lostGoldText);
            sb.DrawString(font, _lostGoldText, new Vector2(cx - sg.X / 2, y), new Color(200, 180, 80));
            y += (int)sg.Y + 8;
        }

        string timer = $"Возрождение через {Math.Ceiling(_remaining)} сек.";
        var st = font.MeasureString(timer);
        sb.DrawString(font, timer, new Vector2(cx - st.X / 2, y), Color.LightGray);

        DrawButton(sb, "Возродиться", _reviveRect, new Color(60, 130, 60), ms, _prevMouse);
    }
}
