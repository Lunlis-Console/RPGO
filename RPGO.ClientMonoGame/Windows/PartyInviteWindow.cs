using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Windows;

public class PartyInviteWindow : GameWindow
{
    private float _remaining = 15f;
    private string _inviterName = "";
    private Rectangle _acceptRect = Rectangle.Empty;
    private Rectangle _declineRect = Rectangle.Empty;
    private MouseState _prevMouse;

    public Action<string>? Accepted { get; set; }
    public Action<string>? Declined { get; set; }

    private static readonly Color CGreen = new(50, 160, 80);
    private static readonly Color CGreenHover = new(70, 190, 100);
    private static readonly Color CDanger = new(150, 60, 60);
    private static readonly Color CDangerHover = new(190, 80, 80);
    private static readonly Color CGold = new(220, 200, 120);

    public PartyInviteWindow()
    {
        Title = "Приглашение в пати";
        Width = 340;
        Height = 180;
        Visible = false;
        IsModal = true;
    }

    public void Show(string inviterName)
    {
        _inviterName = inviterName;
        _remaining = 15f;
        var g = GameMain.Instance!.Graphics;
        X = (g.PreferredBackBufferWidth - Width) / 2;
        Y = (g.PreferredBackBufferHeight - Height) / 2;
        Visible = true;
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        _remaining -= (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_remaining <= 0)
        {
            _remaining = 0;
            Declined?.Invoke(_inviterName);
            Visible = false;
            return;
        }

        int cx = ContentX;
        int cw = ContentW;
        int btnY = ContentY + 64;
        int btnH = 30;
        int btnW = (cw - 10) / 2;

        _acceptRect = new Rectangle(cx, btnY, btnW, btnH);
        _declineRect = new Rectangle(cx + btnW + 10, btnY, btnW, btnH);

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        if (clicked)
        {
            if (_acceptRect.Contains(mouse.X, mouse.Y))
            {
                Accepted?.Invoke(_inviterName);
                Visible = false;
            }
            else if (_declineRect.Contains(mouse.X, mouse.Y))
            {
                Declined?.Invoke(_inviterName);
                Visible = false;
            }
        }

        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;

        var font = SpriteCache.Font ?? SpriteCache.FontSmall;
        var fontSmall = SpriteCache.FontSmall ?? font;
        if (font == null) return;

        // Фон окна + заголовок (без крестика)
        var windowRect = new Rectangle(X, Y, Width, Height);
        sb.Draw(SpriteCache.Pixel, windowRect, new Color(30, 32, 40));
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, TitleH), new Color(45, 55, 75));
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, 2), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y + Height - 2, Width, 2), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, 2, Height), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(X + Width - 2, Y, 2, Height), new Color(80, 90, 110));

        var titleSize = font.MeasureString(Title);
        sb.DrawString(font, Title, new Vector2(X + 8, Y + (TitleH - titleSize.Y) / 2), Color.White);

        int cx = ContentX;
        int cy = ContentY;
        int cw = ContentW;
        var ms = Mouse.GetState();

        // Текст приглашения
        string line1 = $"{_inviterName}";
        var s1 = font.MeasureString(line1);
        sb.DrawString(font, line1, new Vector2(X + (Width - s1.X) / 2, cy), Color.White);

        string line2 = "приглашает вас в группу";
        var s2 = fontSmall.MeasureString(line2);
        sb.DrawString(fontSmall, line2, new Vector2(X + (Width - s2.X) / 2, cy + 22), CLight);

        // Таймер
        int secs = (int)Math.Ceiling(_remaining);
        string timer = $"Осталось {secs} сек.";
        var ts = fontSmall.MeasureString(timer);
        sb.DrawString(fontSmall, timer, new Vector2(X + (Width - ts.X) / 2, cy + 42), CGold);

        // Кнопки
        int btnY = cy + 64;
        int btnH = 30;
        int btnW = (cw - 10) / 2;

        var acceptRect = new Rectangle(cx, btnY, btnW, btnH);
        bool acceptHover = acceptRect.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, acceptRect, acceptHover ? CGreenHover : CGreen);
        DrawCenteredText(sb, font, "Принять", acceptRect, Color.White);

        var declineRect = new Rectangle(cx + btnW + 10, btnY, btnW, btnH);
        bool declineHover = declineRect.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, declineRect, declineHover ? CDangerHover : CDanger);
        DrawCenteredText(sb, font, "Отказаться", declineRect, Color.White);
    }

    private void DrawCenteredText(SpriteBatch sb, SpriteFont font, string text, Rectangle rect, Color color)
    {
        var size = font.MeasureString(text);
        sb.DrawString(font, text,
            new Vector2(rect.X + (rect.Width - size.X) / 2, rect.Y + (rect.Height - size.Y) / 2),
            color);
    }

    private static readonly Color CLight = new(210, 210, 220);
}
