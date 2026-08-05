using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RPGGame.ClientMonoGame.Rendering;

public sealed class FloatingText
{
    public float X, Y;
    public string Text = "";
    public Color Color;
    public DateTime StartTime;
    public int DurationMs = 1000;
    public float Scale = 1f;
}

public class FloatingTextRenderer
{
    private readonly List<FloatingText> _texts = new();
    private static readonly Random _rng = new();
    private readonly object _lock = new();

    public void Spawn(float mapX, float mapY, string text, Color color, bool isCrit = false)
    {
        lock (_lock)
        {
            float jitterX = (float)(_rng.NextDouble() - 0.5) * 0.6f;
            _texts.Add(new FloatingText
            {
                X = mapX + jitterX,
                Y = mapY,
                Text = text,
                Color = color,
                StartTime = DateTime.UtcNow,
                Scale = isCrit ? 1.6f : 1.2f
            });
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, int startX, int startY,
        float gridOX, float gridOY, float cellW, float cellH)
    {
        lock (_lock)
        {
            for (int i = _texts.Count - 1; i >= 0; i--)
            {
                var ft = _texts[i];
                float elapsed = (float)(DateTime.UtcNow - ft.StartTime).TotalMilliseconds;
                if (elapsed >= ft.DurationMs) { _texts.RemoveAt(i); continue; }
                float t = elapsed / ft.DurationMs;
                int alpha = 255 - (int)(t * 200); if (alpha < 0) alpha = 0;
                float rise = t * 1.2f;
                float fpx = gridOX + (ft.X - startX) * cellW + cellW / 2;
                float fpy = gridOY + (ft.Y - startY - rise) * cellH - 4;
                var c = new Color(ft.Color.R, ft.Color.G, ft.Color.B, (byte)alpha);
                Vector2 origin = font.MeasureString(ft.Text) / 2f;
                float scale = ft.Scale;
                var outline = new Color((byte)0, (byte)0, (byte)0, (byte)(alpha * 0.9f));
                float o = 1.2f * scale;
                sb.DrawString(font, ft.Text, new Vector2(fpx - o, fpy), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx + o, fpy), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy - o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy + o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx - o, fpy - o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx + o, fpy - o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx - o, fpy + o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx + o, fpy + o), outline, 0f, origin, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, ft.Text, new Vector2(fpx, fpy), c, 0f, origin, scale, SpriteEffects.None, 0f);
            }
        }
    }

    public void Clear()
    {
        lock (_lock) _texts.Clear();
    }
}
