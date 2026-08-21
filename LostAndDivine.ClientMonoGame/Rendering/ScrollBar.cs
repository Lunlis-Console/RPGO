using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LostAndDivine.ClientMonoGame.Rendering;

namespace LostAndDivine.ClientMonoGame.Rendering;

/// <summary>
/// Единый стиль вертикального скроллбара для всех окон игры:
/// одинаковая ширина, цвета дорожки и ползунка, минимальная высота ползунка.
/// </summary>
public static class ScrollBar
{
    public const int DefaultWidth = 10;
    private static readonly Color TrackColor = new Color(50, 52, 62);
    private static readonly Color ThumbColor = new Color(120, 130, 150);
    private const int MinThumbH = 28;

    /// <summary>Рисует дорожку и ползунок. Возвращает прямоугольник ползунка (для hit-test).</summary>
    public static Rectangle Draw(SpriteBatch sb, int x, int y, int height, int scrollY, int viewport, int contentHeight, int width = DefaultWidth)
    {
        var track = new Rectangle(x, y, width, height);
        sb.Draw(SpriteCache.Pixel, track, TrackColor);
        int thumbH = ComputeThumbHeight(height, viewport, contentHeight);
        int thumbY = ComputeThumbY(y, height, thumbH, scrollY, viewport, contentHeight);
        var thumb = new Rectangle(x, thumbY, width, thumbH);
        sb.Draw(SpriteCache.Pixel, thumb, ThumbColor);
        return thumb;
    }

    public static int ComputeThumbHeight(int trackH, int viewport, int contentHeight)
    {
        int max = Math.Max(0, contentHeight - viewport);
        if (max <= 0) return trackH;
        return Math.Max(MinThumbH, (int)((float)trackH * viewport / Math.Max(1, contentHeight)));
    }

    public static int ComputeThumbY(int trackY, int trackH, int thumbH, int scrollY, int viewport, int contentHeight)
    {
        int max = Math.Max(0, contentHeight - viewport);
        if (max <= 0) return trackY;
        float t = Math.Clamp((float)scrollY / max, 0, 1f);
        return trackY + (int)(t * (trackH - thumbH));
    }

    /// <summary>Переводит позицию мыши по дорожке в значение прокрутки.</summary>
    public static int ScrollFromMouse(int trackY, int trackH, int thumbH, int viewport, int contentHeight, int mouseY)
    {
        int max = Math.Max(0, contentHeight - viewport);
        if (max <= 0 || trackH <= thumbH) return 0;
        float ratio = (mouseY - trackY - thumbH / 2) / (float)(trackH - thumbH);
        return (int)(Math.Clamp(ratio, 0, 1f) * max);
    }
}
