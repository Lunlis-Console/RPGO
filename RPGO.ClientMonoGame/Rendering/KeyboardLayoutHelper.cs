using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework.Input;

namespace RPGGame.ClientMonoGame.Rendering;

/// <summary>
/// Помогает корректно переводить нажатия клавиш в символы с учётом
/// текущей раскладки Windows и Shift/CapsLock через нативный ToUnicode,
/// а также переключает раскладку ОС (RU/EN) по клику в чате.
/// Работает только на Windows (целевая платформа игры).
/// </summary>
public static class KeyboardLayoutHelper
{
    [DllImport("user32.dll")]
    private static extern int ToUnicode(
        uint virtualKey, uint scanCode, byte[] keyState,
        [Out] StringBuilder pwszBuff, int cchBuff, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint KLF_ACTIVATE = 0x00000001;
    private const uint MAPVK_VK_TO_VSC = 0;

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

    /// <summary>
    /// Возвращает символ для виртуальной клавиши с учётом раскладки ОС
    /// и модификаторов из keyState. null — непечатный символ.
    /// </summary>
    public static char? TranslateKey(Keys key, byte[] keyState)
    {
        uint vk = (uint)key;
        uint scan = MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        // ToUnicode требует, чтобы сама клавиша была помечена как нажатая
        keyState[vk] |= 0x80;
        var sb = new StringBuilder(8);
        int result = ToUnicode(vk, scan, keyState, sb, sb.Capacity, 0);
        if (result > 0 && sb.Length > 0)
        {
            char c = sb[0];
            if (c == '\r' || c == '\n' || c == '\t') return null;
            if (char.IsControl(c)) return null;
            return c;
        }
        return null;
    }

    public static byte[] GetCurrentKeyState(KeyboardState state)
    {
        // Берём реальное состояние клавиш из ОС (включает Shift/Ctrl/Alt/CapsLock
        // и текущую раскладку). Дополнительно синхронизируем модификаторы из
        // MonoGame-состояния, чтобы моментальные нажатия учитывались корректно.
        var keyState = new byte[256];
        GetKeyboardState(keyState);
        ApplyMod(keyState, VK_SHIFT, state.IsKeyDown(Keys.LeftShift) || state.IsKeyDown(Keys.RightShift));
        ApplyMod(keyState, VK_CONTROL, state.IsKeyDown(Keys.LeftControl) || state.IsKeyDown(Keys.RightControl));
        ApplyMod(keyState, VK_MENU, state.IsKeyDown(Keys.LeftAlt) || state.IsKeyDown(Keys.RightAlt));
        ApplyMod(keyState, VK_CAPITAL, state.IsKeyDown(Keys.CapsLock));
        return keyState;
    }

    private static void ApplyMod(byte[] keyState, int vk, bool down)
    {
        keyState[vk] = down ? (byte)0x80 : (byte)0x00;
    }

    // VK-коды для модификаторов (используются в массиве состояния GetKeyboardState)
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_CAPITAL = 0x14;
}
