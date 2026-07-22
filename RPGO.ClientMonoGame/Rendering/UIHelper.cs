using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RPGGame.ClientMonoGame.Rendering;

/// <summary>
/// Общие методы отрисовки UI, используемые несколькими окнами/экранами.
/// </summary>
public static class UIHelper
{
    /// <summary>Рисует прямоугольную обводку (4 линии) вокруг rect.</summary>
    public static void DrawRectOutline(SpriteBatch sb, Rectangle rect, Color color, int t = 1)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, t), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Bottom - t, rect.Width, t), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, t, rect.Height), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.Right - t, rect.Y, t, rect.Height), color);
    }

    /// <summary>Рисует текст, если он не null/пустой.</summary>
    public static void DrawText(SpriteBatch sb, string text, int x, int y, Color color, SpriteFont font)
    {
        if (!string.IsNullOrEmpty(text))
            sb.DrawString(font, text, new Vector2(x, y), color);
    }
}
