using Microsoft.Xna.Framework.Input;

namespace RPGGame.ClientMonoGame.Rendering;

/// <summary>
/// Полная таблица перевода виртуальных клавиш в символы для EN и RU раскладок.
/// Используется вместо ToUnicode, чтобы ввод кириллицы и спецсимволов был
/// 100% предсказуемым и не зависел от состояния раскладки в ToUnicode.
/// </summary>
public static class KeyCharMap
{
    // [раскладка RU?][с Shift?][Keys] = char
    private static readonly Dictionary<Keys, char> EnNormal = new();
    private static readonly Dictionary<Keys, char> EnShift = new();
    private static readonly Dictionary<Keys, char> RuNormal = new();
    private static readonly Dictionary<Keys, char> RuShift = new();

    static KeyCharMap()
    {
        // Буквы (ЙЦУКЕН)
        Add(Keys.A, 'a', 'A', 'ф', 'Ф');
        Add(Keys.B, 'b', 'B', 'и', 'И');
        Add(Keys.C, 'c', 'C', 'с', 'С');
        Add(Keys.D, 'd', 'D', 'в', 'В');
        Add(Keys.E, 'e', 'E', 'у', 'У');
        Add(Keys.F, 'f', 'F', 'а', 'А');
        Add(Keys.G, 'g', 'G', 'п', 'П');
        Add(Keys.H, 'h', 'H', 'р', 'Р');
        Add(Keys.I, 'i', 'I', 'ш', 'Ш');
        Add(Keys.J, 'j', 'J', 'о', 'О');
        Add(Keys.K, 'k', 'K', 'л', 'Л');
        Add(Keys.L, 'l', 'L', 'д', 'Д');
        Add(Keys.M, 'm', 'M', 'ь', 'Ь');
        Add(Keys.N, 'n', 'N', 'т', 'Т');
        Add(Keys.O, 'o', 'O', 'щ', 'Щ');
        Add(Keys.P, 'p', 'P', 'з', 'З');
        Add(Keys.Q, 'q', 'Q', 'й', 'Й');
        Add(Keys.R, 'r', 'R', 'к', 'К');
        Add(Keys.S, 's', 'S', 'ы', 'Ы');
        Add(Keys.T, 't', 'T', 'е', 'Е');
        Add(Keys.U, 'u', 'U', 'г', 'Г');
        Add(Keys.V, 'v', 'V', 'м', 'М');
        Add(Keys.W, 'w', 'W', 'ц', 'Ц');
        Add(Keys.X, 'x', 'X', 'ч', 'Ч');
        Add(Keys.Y, 'y', 'Y', 'н', 'Н');
        Add(Keys.Z, 'z', 'Z', 'я', 'Я');

        // Цифры
        Add(Keys.D0, '0', ')', '0', ')');
        Add(Keys.D1, '1', '!', '1', '!');
        Add(Keys.D2, '2', '@', '2', '"');
        Add(Keys.D3, '3', '#', '3', '№');
        Add(Keys.D4, '4', '$', '4', ';');
        Add(Keys.D5, '5', '%', '5', '%');
        Add(Keys.D6, '6', '^', '6', ':');
        Add(Keys.D7, '7', '&', '7', '?');
        Add(Keys.D8, '8', '*', '8', '*');
        Add(Keys.D9, '9', '(', '9', '(');
    }

    private static void Add(Keys key, char enN, char enS, char ruN, char ruS)
    {
        EnNormal[key] = enN;
        EnShift[key] = enS;
        RuNormal[key] = ruN;
        RuShift[key] = ruS;
    }

    public static bool TryGetChar(Keys key, bool russian, bool shift, out char ch)
    {
        var map = (russian, shift) switch
        {
            (true, true) => RuShift,
            (true, false) => RuNormal,
            (false, true) => EnShift,
            (false, false) => EnNormal
        };
        return map.TryGetValue(key, out ch);
    }
}
