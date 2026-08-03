using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace RPGGame.ClientMonoGame.Rendering;

public readonly struct TooltipLine
{
    public readonly string Text;
    public readonly Color? Color;

    public TooltipLine(string text, Color? color = null)
    {
        Text = text; Color = color;
    }

    public static implicit operator TooltipLine(string text) => new(text);
}

public static class TooltipRenderer
{
    private static readonly Color BgColor = new Color(20, 22, 30, 240);
    private static readonly Color BorderColor = new Color(90, 95, 115);
    private static readonly Color TitleColor = new Color(230, 220, 140);
    private static readonly Color TextColor = new Color(200, 200, 210);

    public static void Draw(SpriteBatch sb, List<string> lines, MouseState mouse, int windowRight = int.MaxValue, int windowBottom = int.MaxValue)
    {
        Draw(sb, lines.ConvertAll(l => new TooltipLine(l)), mouse, windowRight, windowBottom);
    }

    public static void Draw(SpriteBatch sb, List<TooltipLine> lines, MouseState mouse, int windowRight = int.MaxValue, int windowBottom = int.MaxValue)
    {
        if (lines.Count == 0) return;
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int pad = 8;
        int lh = 18;
        float maxW = 0;
        foreach (var l in lines)
            maxW = Math.Max(maxW, font.MeasureString(l.Text).X);

        int tw = (int)maxW + pad * 2;
        int th = lines.Count * lh + pad * 2;
        int tx = mouse.X + 16;
        int ty = mouse.Y + 16;

        if (tx + tw > windowRight) tx = windowRight - tw - 4;
        if (ty + th > windowBottom) ty = windowBottom - th - 4;
        if (tx < 0) tx = 4;
        if (ty < 0) ty = 4;

        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, tw, th), BgColor);
        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, tw, 2), BorderColor);

        for (int i = 0; i < lines.Count; i++)
        {
            var color = lines[i].Color ?? (i == 0 ? TitleColor : TextColor);
            sb.DrawString(font, lines[i].Text, new Vector2(tx + pad, ty + pad + i * lh), color);
        }
    }
}
