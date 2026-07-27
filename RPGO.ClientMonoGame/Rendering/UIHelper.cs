using System.Collections.Generic;
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

    /// <summary>Разбивает текст на строки по слову, не превышающие maxWidth.</summary>
    public static List<string> WrapText(SpriteFont font, string text, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add("");
                continue;
            }
            var words = paragraph.Split(' ');
            var currentLine = "";
            foreach (var word in words)
            {
                var testLine = currentLine.Length == 0 ? word : currentLine + " " + word;
                if (font.MeasureString(testLine).X > maxWidth)
                {
                    if (currentLine.Length > 0)
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        lines.Add(word);
                        currentLine = "";
                    }
                }
                else
                {
                    currentLine = testLine;
                }
            }
            if (currentLine.Length > 0)
                lines.Add(currentLine);
        }
        return lines;
    }

    /// <summary>Измеряет высоту текста, разбитого по строкам.</summary>
    public static int MeasureWrappedHeight(SpriteFont font, string text, float maxWidth)
    {
        var lines = WrapText(font, text, maxWidth);
        return lines.Count * (int)(font.MeasureString("Wg").Y) + Math.Max(0, lines.Count - 1) * 2;
    }
}
