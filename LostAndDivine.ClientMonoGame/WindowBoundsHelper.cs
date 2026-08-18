using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LostAndDivine.ClientMonoGame;

/// <summary>
/// Ограничивает оконный режим рабочей областью монитора (за вычетом панели задач),
/// чтобы при разрешении 1920x1080 окно не уходило под нижнюю панель Windows:
/// размер клиентской области уменьшается с сохранением пропорций, окно центрируется.
/// </summary>
public static class WindowBoundsHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, ref RECT pvParam, int fWinIni);

    private const int SPI_GETWORKAREA = 0x0030;

    // Запас под заголовок и рамки окна (вне клиентской области).
    private const int TitleBarH = 36;
    private const int BorderW = 6;

    /// <summary>Рабочая область главного монитора (без панели задач).</summary>
    public static Rectangle GetWorkArea()
    {
        var rc = new RECT();
        if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref rc, 0))
            return new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
        var dm = GraphicsAdapter.DefaultAdapter?.CurrentDisplayMode;
        return dm == null ? new Rectangle(0, 0, 1920, 1080) : new Rectangle(0, 0, dm.Width, dm.Height);
    }

    /// <summary>
    /// Уменьшает запрошенный размер клиентской области, если тот не влезает
    /// в рабочую область монитора (с запасом на заголовок и рамки окна).
    /// Пропорции сохраняются; если всё помещается — размер не меняется.
    /// </summary>
    public static (int W, int H) FitToWorkArea(int w, int h)
    {
        var wa = GetWorkArea();
        int availW = Math.Max(640, wa.Width - BorderW * 2);
        int availH = Math.Max(480, wa.Height - TitleBarH - BorderW * 2);
        float scale = Math.Min(availW / (float)Math.Max(1, w), availH / (float)Math.Max(1, h));
        if (scale >= 1f) return (w, h);
        return (Math.Max(640, (int)Math.Round(w * scale)), Math.Max(480, (int)Math.Round(h * scale)));
    }

    /// <summary>Позиционирует окно по центру рабочей области монитора.</summary>
    public static void PositionWindow(GameWindow window, int clientW, int clientH)
    {
        var wa = GetWorkArea();
        int x = wa.X + Math.Max(BorderW, (wa.Width - clientW) / 2);
        int y = wa.Y + Math.Max(BorderW, (wa.Height - clientH - TitleBarH) / 2);
        try { window.Position = new Point(x, y); } catch { }
    }
}