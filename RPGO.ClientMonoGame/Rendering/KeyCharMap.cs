using System.Collections.Generic;

namespace RPGGame.ClientMonoGame.Rendering;

/// <summary>
/// Таблица перевода виртуальных кодов клавиш (VK, winuser.h) в символы для
/// EN и RU раскладок. Используем VK напрямую (а не MonoGame Keys), потому что
/// MonoGame DesktopGL на русской раскладке возвращает Keys.None для OEM-клавиш
/// (х, ъ, ж, э, б, ю). VK же стабилен вне зависимости от раскладки.
/// Русские буквы заданы через \uXXXX, чтобы файл был чистым ASCII и кириллица
/// никогда не билась при перекодировке.
/// </summary>
public static class KeyCharMap
{
    private static readonly Dictionary<uint, char> EnNormal = new();
    private static readonly Dictionary<uint, char> EnShift = new();
    private static readonly Dictionary<uint, char> RuNormal = new();
    private static readonly Dictionary<uint, char> RuShift = new();

    // VK-коды (winuser.h)
    private const uint VK_A = 0x41, VK_B = 0x42, VK_C = 0x43, VK_D = 0x44, VK_E = 0x45,
                     VK_F = 0x46, VK_G = 0x47, VK_H = 0x48, VK_I = 0x49, VK_J = 0x4A,
                     VK_K = 0x4B, VK_L = 0x4C, VK_M = 0x4D, VK_N = 0x4E, VK_O = 0x4F,
                     VK_P = 0x50, VK_Q = 0x51, VK_R = 0x52, VK_S = 0x53, VK_T = 0x54,
                     VK_U = 0x55, VK_V = 0x56, VK_W = 0x57, VK_X = 0x58, VK_Y = 0x59,
                     VK_Z = 0x5A;
    private const uint VK_0 = 0x30, VK_1 = 0x31, VK_2 = 0x32, VK_3 = 0x33, VK_4 = 0x34,
                     VK_5 = 0x35, VK_6 = 0x36, VK_7 = 0x37, VK_8 = 0x38, VK_9 = 0x39;
    private const uint VK_SPACE = 0x20;
    private const uint VK_OEM_3 = 0xC0;   // ` ~  /  ё Ё
    private const uint VK_OEM_4 = 0xDB;   // [ {  /  х Х
    private const uint VK_OEM_6 = 0xDD;   // ] }  /  ъ Ъ
    private const uint VK_OEM_1 = 0xBA;   // ; :  /  ж Ж
    private const uint VK_OEM_7 = 0xDE;   // ' "  /  э Э
    private const uint VK_OEM_COMMA = 0xBC;   // , <  /  б Б
    private const uint VK_OEM_PERIOD = 0xBE;  // . >  /  ю Ю
    private const uint VK_OEM_MINUS = 0xBD;   // - _  /  - _
    private const uint VK_OEM_PLUS = 0xBB;    // = +  /  = +
    private const uint VK_OEM_5 = 0xDC;   // \ |  /  ё Ё
    private const uint VK_OEM_2 = 0xBF;   // / ?  /  . ,

    static KeyCharMap()
    {
        Add(VK_SPACE, ' ', ' ', ' ', ' ');

        // Буквы (ЙЦУКЕН)
        Add(VK_A, 'a', 'A', '\u0444', '\u0424');
        Add(VK_B, 'b', 'B', '\u0438', '\u0418');
        Add(VK_C, 'c', 'C', '\u0441', '\u0421');
        Add(VK_D, 'd', 'D', '\u0432', '\u0412');
        Add(VK_E, 'e', 'E', '\u0443', '\u0423');
        Add(VK_F, 'f', 'F', '\u0430', '\u0410');
        Add(VK_G, 'g', 'G', '\u043F', '\u041F');
        Add(VK_H, 'h', 'H', '\u0440', '\u0420');
        Add(VK_I, 'i', 'I', '\u0448', '\u0428');
        Add(VK_J, 'j', 'J', '\u043E', '\u041E');
        Add(VK_K, 'k', 'K', '\u043B', '\u041B');
        Add(VK_L, 'l', 'L', '\u0434', '\u0414');
        Add(VK_M, 'm', 'M', '\u044C', '\u042C');
        Add(VK_N, 'n', 'N', '\u0442', '\u0422');
        Add(VK_O, 'o', 'O', '\u0449', '\u0429');
        Add(VK_P, 'p', 'P', '\u0437', '\u0427');
        Add(VK_Q, 'q', 'Q', '\u0439', '\u0419');
        Add(VK_R, 'r', 'R', '\u043A', '\u041A');
        Add(VK_S, 's', 'S', '\u044B', '\u042B');
        Add(VK_T, 't', 'T', '\u0435', '\u0415');
        Add(VK_U, 'u', 'U', '\u0433', '\u0413');
        Add(VK_V, 'v', 'V', '\u043C', '\u041C');
        Add(VK_W, 'w', 'W', '\u0446', '\u0426');
        Add(VK_X, 'x', 'X', '\u0447', '\u0427');
        Add(VK_Y, 'y', 'Y', '\u043D', '\u041D');
        Add(VK_Z, 'z', 'Z', '\u044F', '\u042F');

        // Цифры
        Add(VK_0, '0', ')', '0', ')');
        Add(VK_1, '1', '!', '1', '!');
        Add(VK_2, '2', '@', '2', '"');
        Add(VK_3, '3', '#', '3', '\u2116');
        Add(VK_4, '4', '$', '4', ';');
        Add(VK_5, '5', '%', '5', '%');
        Add(VK_6, '6', '^', '6', ':');
        Add(VK_7, '7', '&', '7', '?');
        Add(VK_8, '8', '*', '8', '*');
        Add(VK_9, '9', '(', '9', '(');

        // OEM (ЙЦУКЕН: х ъ ж э б ю ё)
        Add(VK_OEM_PERIOD, '.', '>', '\u044E', '\u042E');
        Add(VK_OEM_COMMA, ',', '<', '\u0431', '\u0411');
        Add(VK_OEM_MINUS, '-', '_', '-', '_');
        Add(VK_OEM_PLUS, '=', '+', '=', '+');
        Add(VK_OEM_1, ';', ':', '\u0436', '\u0416');
        Add(VK_OEM_7, '\'', '"', '\u044D', '\u042D');
        Add(VK_OEM_4, '[', '{', '\u0445', '\u0425');
        Add(VK_OEM_6, ']', '}', '\u044A', '\u042A');
        Add(VK_OEM_5, '\\', '|', '\u0451', '\u0401');
        Add(VK_OEM_3, '`', '~', '\u0451', '\u0401');
        Add(VK_OEM_2, '/', '?', '.', ',');
    }

    private static void Add(uint vk, char enN, char enS, char ruN, char ruS)
    {
        EnNormal[vk] = enN;
        EnShift[vk] = enS;
        RuNormal[vk] = ruN;
        RuShift[vk] = ruS;
    }

    public static bool TryGetCharByVk(uint vk, bool russian, bool shift, out char ch)
    {
        var map = (russian, shift) switch
        {
            (true, true) => RuShift,
            (true, false) => RuNormal,
            (false, true) => EnShift,
            (false, false) => EnNormal
        };
        return map.TryGetValue(vk, out ch);
    }
}
