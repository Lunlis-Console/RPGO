using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace RPGGame.ClientMonoGame.Rendering;

public class ChatRenderer
{
    private readonly List<(string name, string text, DateTime time)> _messages = new();
    private const int MaxMessages = 200;

    public bool IsTyping { get; set; }
    public string TypedText { get; set; } = "";

    public enum Layout { En, Ru }
    public Layout CurrentLayout { get; set; } = Layout.En;
    public bool IsLangMenuOpen { get; set; }

    // Соответствие латинской клавише -> русская буква (ЙЦУКЕН)
    private static readonly Dictionary<Keys, char> RuMap = new()
    {
        { Keys.A, 'ф' }, { Keys.B, 'и' }, { Keys.C, 'с' }, { Keys.D, 'в' }, { Keys.E, 'у' },
        { Keys.F, 'а' }, { Keys.G, 'п' }, { Keys.H, 'р' }, { Keys.I, 'ш' }, { Keys.J, 'о' },
        { Keys.K, 'л' }, { Keys.L, 'д' }, { Keys.M, 'ь' }, { Keys.N, 'т' }, { Keys.O, 'щ' },
        { Keys.P, 'з' }, { Keys.Q, 'й' }, { Keys.R, 'к' }, { Keys.S, 'ы' }, { Keys.T, 'е' },
        { Keys.U, 'г' }, { Keys.V, 'м' }, { Keys.W, 'ц' }, { Keys.X, 'ч' }, { Keys.Y, 'н' },
        { Keys.Z, 'я' },
    };

    public void AddMessage(string name, string text)
    {
        _messages.Add((name, text, DateTime.UtcNow));
        if (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);
    }

    public void HandleInput(KeyboardState keyboard, KeyboardState prevKeyboard)
    {
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        bool ru = CurrentLayout == Layout.Ru;

        for (int k = (int)Keys.A; k <= (int)Keys.Z; k++)
        {
            if (keyboard.IsKeyDown((Keys)k) && prevKeyboard.IsKeyUp((Keys)k))
            {
                Keys key = (Keys)k;
                char c;
                if (ru && RuMap.TryGetValue(key, out var ruc))
                    c = ruc;
                else
                    c = (char)('a' + k - (int)Keys.A);
                if (shift)
                    c = char.ToUpper(c);
                TypedText += c;
            }
        }

        // Цифры и их Shift-варианты (верхний ряд), индекс = сама цифра
        string enShift = ")!@#$%^&*(";
        string ruShift = ")!№;%:?*(\"";
        for (int k = (int)Keys.D0; k <= (int)Keys.D9; k++)
        {
            if (keyboard.IsKeyDown((Keys)k) && prevKeyboard.IsKeyUp((Keys)k))
            {
                int d = k - (int)Keys.D0;
                if (shift)
                    TypedText += ru ? ruShift[d] : enShift[d];
                else
                    TypedText += (char)('0' + d);
            }
        }

        // Пунктуация / спецсимволы
        if (keyboard.IsKeyDown(Keys.Space) && prevKeyboard.IsKeyUp(Keys.Space))
            TypedText += ' ';
        AddOem(keyboard, prevKeyboard, Keys.OemPeriod, shift ? '?' : '.', ru ? (shift ? '/' : '.') : (shift ? '?' : '.'));
        AddOem(keyboard, prevKeyboard, Keys.OemComma, shift ? '<' : ',', ru ? (shift ? 'б' : 'б') : (shift ? '<' : ','));
        AddOem(keyboard, prevKeyboard, Keys.OemMinus, shift ? '_' : '-', ru ? (shift ? '-' : '-') : (shift ? '_' : '-'));
        AddOem(keyboard, prevKeyboard, Keys.OemQuestion, shift ? '/' : '/', ru ? (shift ? '.' : ',') : (shift ? '?' : '/'));
        AddOem(keyboard, prevKeyboard, Keys.OemQuotes, shift ? '"' : '\'', ru ? (shift ? 'э' : 'ь') : (shift ? '"' : '\''));
        AddOem(keyboard, prevKeyboard, Keys.OemOpenBrackets, shift ? '{' : '[', ru ? (shift ? 'х' : 'х') : (shift ? '{' : '['));
        AddOem(keyboard, prevKeyboard, Keys.OemCloseBrackets, shift ? '}' : ']', ru ? (shift ? 'ъ' : 'ъ') : (shift ? '}' : ']'));
        AddOem(keyboard, prevKeyboard, Keys.OemTilde, shift ? '~' : '`', ru ? (shift ? 'ё' : 'ё') : (shift ? '~' : '`'));

        if (keyboard.IsKeyDown(Keys.Back) && prevKeyboard.IsKeyUp(Keys.Back) && TypedText.Length > 0)
            TypedText = TypedText[..^1];
        if (keyboard.IsKeyDown(Keys.Escape) && prevKeyboard.IsKeyUp(Keys.Escape))
        {
            IsTyping = false;
            TypedText = "";
            IsLangMenuOpen = false;
        }
    }

    private void AddOem(KeyboardState keyboard, KeyboardState prevKeyboard, Keys key, char enChar, char ruChar)
    {
        if (keyboard.IsKeyDown(key) && prevKeyboard.IsKeyUp(key))
            TypedText += CurrentLayout == Layout.Ru ? ruChar : enChar;
    }

    // Возвращает true, если клик обработан чатом (чтобы GameScreen не трогал лишнее)
    // pressed — событие нажатия ЛКМ в этом кадре (just-pressed)
    public bool HandleClick(int mx, int my, float x, float y, float w, float h, bool pressed)
    {
        float inputY = y + h - 26;
        int indW = 34, indH = 22;
        float indX = x + w - 8 - indW;
        var indRect = new Rectangle((int)indX, (int)inputY, indW, indH);

        // Пункты меню выше индикатора
        int itemH = 22;
        var ruRect = new Rectangle((int)indX, (int)inputY - itemH * 2, indW, itemH);
        var enRect = new Rectangle((int)indX, (int)inputY - itemH, indW, itemH);

        if (IsLangMenuOpen)
        {
            if (pressed)
            {
                if (ruRect.Contains(mx, my)) { CurrentLayout = Layout.Ru; IsLangMenuOpen = false; return true; }
                if (enRect.Contains(mx, my)) { CurrentLayout = Layout.En; IsLangMenuOpen = false; return true; }
                if (indRect.Contains(mx, my)) { IsLangMenuOpen = false; return true; } // повторный клик — закрыть
            }
            // Клик вне меню не закрывает его (ждём выбора языка)
            return true;
        }

        // Закрыто: открываем только по клику (не при простом наведении)
        if (pressed && indRect.Contains(mx, my))
        {
            IsLangMenuOpen = true;
            return true;
        }
        return false;
    }

    public void Draw(SpriteBatch sb, float x, float y, float w, float h)
    {
        var font = SpriteCache.Font;
        var fontSmall = SpriteCache.FontSmall ?? font;
        if (font == null) return;

        // Фон чата (полупрозрачный)
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(26, 26, 34, 150));

        // Сообщения (с переносом строк по ширине)
        int lineH = 14;
        float maxTextW = w - 16;
        var wrapped = new List<(string text, Color color)>();
        int startIdx = Math.Max(0, _messages.Count - 200);
        for (int i = startIdx; i < _messages.Count; i++)
        {
            var msg = _messages[i];
            string prefix = $"{msg.name}: ";
            Color nameColor = msg.name switch
            {
                "Система" => Color.Yellow,
                "Бой" => Color.Red,
                "Пати" => new Color(100, 200, 100),
                "Трейд" => new Color(200, 200, 100),
                _ => Color.White
            };

            string full = prefix + msg.text;
            var words = full.Split(' ');
            var cur = "";
            foreach (var word in words)
            {
                string test = cur.Length == 0 ? word : cur + " " + word;
                if (fontSmall.MeasureString(test).X > maxTextW && cur.Length > 0)
                {
                    wrapped.Add((cur, nameColor));
                    cur = word;
                }
                else
                {
                    cur = test;
                }
            }
            if (cur.Length > 0) wrapped.Add((cur, nameColor));
        }

        int visibleLines = Math.Max(1, (int)((h - 30) / lineH));
        int from = Math.Max(0, wrapped.Count - visibleLines);
        float msgY = y + 4;
        for (int i = from; i < wrapped.Count; i++)
        {
            sb.DrawString(fontSmall, wrapped[i].text, new Vector2(x + 8, msgY), wrapped[i].color);
            msgY += lineH;
        }

        // Поле ввода
        float inputY = y + h - 26;
        int indW = 34, indH = 22;
        float indX = x + w - 8 - indW;
        // Текстовая область сдвигается, чтобы не перекрывать индикатор
        float textMaxX = indX - 6;
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)(x + 4), (int)inputY, (int)(w - 8), 22), new Color(40, 40, 50, 180));

        // Индикатор раскладки
        var msNow = Microsoft.Xna.Framework.Input.Mouse.GetState();
        bool hover = new Rectangle((int)indX, (int)inputY, indW, indH).Contains(msNow.X, msNow.Y);
        Color indColor = IsLangMenuOpen ? new Color(80, 110, 160)
            : (hover ? new Color(90, 96, 120) : new Color(60, 64, 80));
        sb.Draw(SpriteCache.Pixel, new Rectangle((int)indX, (int)inputY, indW, indH), indColor);
        sb.DrawString(fontSmall, CurrentLayout == Layout.Ru ? "RU" : "EN",
            new Vector2(indX + (indW - fontSmall.MeasureString(CurrentLayout == Layout.Ru ? "RU" : "EN").X) / 2, inputY + 3),
            Color.White);

        // Выпадающее меню языков
        if (IsLangMenuOpen)
        {
            int itemH = 22;
            var ruRect = new Rectangle((int)indX, (int)inputY - itemH * 2, indW, itemH);
            var enRect = new Rectangle((int)indX, (int)inputY - itemH, indW, itemH);
            sb.Draw(SpriteCache.Pixel, ruRect, CurrentLayout == Layout.Ru ? new Color(80, 110, 160) : new Color(50, 54, 70));
            sb.Draw(SpriteCache.Pixel, enRect, CurrentLayout == Layout.En ? new Color(80, 110, 160) : new Color(50, 54, 70));
            sb.DrawString(fontSmall, "RU", new Vector2(indX + (indW - fontSmall.MeasureString("RU").X) / 2, ruRect.Y + 3), Color.White);
            sb.DrawString(fontSmall, "EN", new Vector2(indX + (indW - fontSmall.MeasureString("EN").X) / 2, enRect.Y + 3), Color.White);
        }

        if (IsTyping)
        {
            string displayText = TypedText + "_";
            // Ограничим ширину текста, чтобы не заезжал под индикатор
            var measured = fontSmall.MeasureString(displayText);
            float drawX = x + 8;
            if (drawX + measured.X > textMaxX)
                drawX = textMaxX - measured.X;
            if (drawX < x + 8) drawX = x + 8;
            sb.DrawString(fontSmall, displayText, new Vector2(drawX, inputY + 3), Color.White);
        }
        else
        {
            sb.DrawString(fontSmall, "Нажмите Enter для ввода сообщения...", new Vector2(x + 8, inputY + 3), new Color(120, 120, 130));
        }
    }
}
