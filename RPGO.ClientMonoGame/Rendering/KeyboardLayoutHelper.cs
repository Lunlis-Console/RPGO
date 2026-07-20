using System.Runtime.InteropServices;

namespace RPGGame.ClientMonoGame.Rendering;

/// <summary>
/// Определяет раскладку активного окна (RU/EN) и переключает раскладку ОС
/// по клику в чате. Сам ввод символов идёт через детерминированную таблицу
/// VK->char (KeyCharMap), без ненадёжного ToUnicode. Работает на Windows.
/// </summary>
public static class KeyboardLayoutHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const uint KLF_ACTIVATE = 0x00000001;

    // Языковые идентификаторы
    private const int LANG_RU = 0x0419;
    private const int LANG_EN = 0x0409;

    public static bool IsRussian()
    {
        try
        {
            IntPtr hkl = GetKeyboardLayout(0);
            int langId = (int)(hkl.ToInt64() & 0xFFFF);
            return langId == LANG_RU;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Раскладка именно АКТИВНОГО (foreground) окна, а не потока игры.
    /// Для оконной игры GetKeyboardLayout(0) часто возвращает не ту раскладку,
    /// которую видит пользователь, из-за чего ввод кириллицы ломался.
    /// </summary>
    public static bool IsRussianForeground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return IsRussian();
            uint threadId = GetWindowThreadProcessId(hwnd, out _);
            IntPtr hkl = GetKeyboardLayout(threadId);
            int langId = (int)(hkl.ToInt64() & 0xFFFF);
            return langId == LANG_RU;
        }
        catch
        {
            return IsRussian();
        }
    }

    public static void SetRussian(bool russian)
    {
        try
        {
            int targetLang = russian ? LANG_RU : LANG_EN;
            // Ищем уже загруженную раскладку с нужным языком
            int count = GetKeyboardLayoutList(0, null!);
            if (count > 0)
            {
                var list = new IntPtr[count];
                GetKeyboardLayoutList(count, list);
                foreach (var hkl in list)
                {
                    int langId = (int)(hkl.ToInt64() & 0xFFFF);
                    if (langId == targetLang)
                    {
                        ActivateKeyboardLayout(hkl, KLF_ACTIVATE);
                        return;
                    }
                }
            }
            // Если нет в списке — грузим по KLID (hex-строка языка)
            string klid = russian ? "00000419" : "00000409";
            IntPtr loaded = LoadKeyboardLayout(klid, KLF_ACTIVATE);
            if (loaded != IntPtr.Zero)
                ActivateKeyboardLayout(loaded, KLF_ACTIVATE);
        }
        catch
        {
            // игнорируем
        }
    }

    public static void ToggleLayout()
    {
        SetRussian(!IsRussian());
    }

    /// <summary>Нажат ли Shift прямо сейчас (по состоянию ОС).</summary>
    public static bool IsShiftDown()
    {
        return (GetAsyncKeyState(0x10) & 0x8000) != 0;
    }

    /// <summary>
    /// Возвращает VK всех клавиш, нажатых прямо сейчас (независимо от MonoGame).
    /// Используется вместо GetPressedKeys(), который ломается на русской раскладке.
    /// </summary>
    public static IEnumerable<uint> GetPressedVks()
    {
        for (uint vk = 1; vk < 256; vk++)
        {
            // 0x8000 — клавиша нажата в данный момент
            if ((GetAsyncKeyState((int)vk) & 0x8000) != 0)
                yield return vk;
        }
    }
}
