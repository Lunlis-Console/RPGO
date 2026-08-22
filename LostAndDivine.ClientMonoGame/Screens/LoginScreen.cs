using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using System.Linq;

namespace LostAndDivine.ClientMonoGame.Screens;

public class LoginScreen : IScreen
{
    private readonly string[] _labels = { "Логин:", "Пароль:" };
    private readonly string[] _defaults = { "", "" };
    private readonly string[] _values = new string[2];
    private string _serverIp = "127.0.0.1";

    private int _selectedField = -1;
    private string _statusMessage = "Не подключено";
    private Color _statusColor = Color.Red;
    private DateTime _lastActionTime = DateTime.MinValue;

    private readonly string[] _buttonLabels = { "Вход", "Регистрация", "Тест. аккаунт" };
    private         Rectangle[] _buttonRects = new Rectangle[3];
    private         Rectangle[] _fieldRects = new Rectangle[3];

    private KeyboardState _prevKeyboard;
    private MouseState _prevMouse;

    private Rectangle _settingsIconRect = Rectangle.Empty;

    public LoginScreen(string? statusMessage = null)
    {
        Array.Copy(_defaults, _values, 2);
        _serverIp = SettingsManager.Load().ServerIp;
        RebuildLayout();
        _ = AutoConnectAsync();

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            _statusMessage = statusMessage;
            _statusColor = Color.Red;
        }

        var client = GameMain.Instance?.Client;
        if (client != null)
        {
            client.SystemMessage += OnSystemMessage;
            client.ErrorReceived += OnError;
        }
    }

    private (int w, int h) GetSize()
    {
        var g = GameMain.Instance?.Graphics;
        int w = g?.PreferredBackBufferWidth ?? 1000;
        int h = g?.PreferredBackBufferHeight ?? 720;
        return (w, h);
    }

    private void RebuildLayout()
    {
        var (w, h) = GetSize();
        int centerX = w / 2;
        int startY = h / 2 - 120;

        for (int i = 0; i < 3; i++)
            _fieldRects[i] = new Rectangle(centerX - 60, startY + i * 45, 200, 30);

        _buttonRects[0] = new Rectangle(centerX + 160, startY, 130, 30);
        _buttonRects[1] = new Rectangle(centerX + 160, startY + 38, 130, 30);
        _buttonRects[2] = new Rectangle(centerX + 160, startY + 76, 130, 30);

        int iconSize = 36;
        _settingsIconRect = new Rectangle(w - iconSize - 12, 12, iconSize, iconSize);
    }

    public void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        var client = GameMain.Instance?.Client;
        if (client == null) return;

        RebuildLayout();

        // Текстовый ввод
        if (_selectedField >= 0)
        {
            for (int k = (int)Keys.A; k <= (int)Keys.Z; k++)
            {
                if (keyboard.IsKeyDown((Keys)k) && _prevKeyboard.IsKeyUp((Keys)k))
                {
                    char c = (char)('a' + k - (int)Keys.A);
                    if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
                        c = char.ToUpper(c);
                    _values[_selectedField] += c;
                }
            }
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            for (int k = (int)Keys.D0; k <= (int)Keys.D9; k++)
            {
                if (keyboard.IsKeyDown((Keys)k) && _prevKeyboard.IsKeyUp((Keys)k))
                {
                    if (shift)
                    {
                        _values[_selectedField] += (k - (int)Keys.D0) switch
                        {
                            0 => ')', 1 => '!', 2 => '@', 3 => '#', 4 => '$',
                            5 => '%', 6 => '^', 7 => '&', 8 => '*', 9 => '(',
                            _ => (char)('0' + k - (int)Keys.D0)
                        };
                    }
                    else
                        _values[_selectedField] += (char)('0' + k - (int)Keys.D0);
                }
            }
            void TryKey(Keys key, char normal, char shifted)
            {
                if (keyboard.IsKeyDown(key) && _prevKeyboard.IsKeyUp(key))
                    _values[_selectedField] += shift ? shifted : normal;
            }
            TryKey(Keys.OemMinus, '-', '_');
            TryKey(Keys.OemPlus, '=', '+');
            TryKey(Keys.OemOpenBrackets, '[', '{');
            TryKey(Keys.OemCloseBrackets, ']', '}');
            TryKey(Keys.OemPipe, '\\', '|');
            TryKey(Keys.OemSemicolon, ';', ':');
            TryKey(Keys.OemQuotes, '\'', '"');
            TryKey(Keys.OemTilde, '`', '~');
            TryKey(Keys.OemComma, ',', '<');
            TryKey(Keys.OemPeriod, '.', '>');
            TryKey(Keys.OemQuestion, '/', '?');
            if (keyboard.IsKeyDown(Keys.Back) && _prevKeyboard.IsKeyUp(Keys.Back) && _values[_selectedField].Length > 0)
                _values[_selectedField] = _values[_selectedField][..^1];
        }

        // Клик по полю ввода
        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
        {
            _selectedField = -1;
            for (int i = 0; i < 2; i++)
            {
                if (_fieldRects[i].Contains(mouse.X, mouse.Y))
                    _selectedField = i;
            }

            // Клик по кнопкам
            if ((DateTime.Now - _lastActionTime).TotalMilliseconds > 1500)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (_buttonRects[i].Contains(mouse.X, mouse.Y))
                    {
                        _lastActionTime = DateTime.Now;
                        HandleButton(i);
                    }
                }
            }

            // Клик по иконке настроек
            if (_settingsIconRect.Contains(mouse.X, mouse.Y))
                GameMain.Instance!.ShowSettings();
        }

        // Enter для быстрого входа
        if (keyboard.IsKeyDown(Keys.Enter) && _prevKeyboard.IsKeyUp(Keys.Enter))
            HandleButton(0);

        _prevKeyboard = keyboard;
        _prevMouse = mouse;
    }

    private async void HandleButton(int index)
    {
        var client = GameMain.Instance?.Client;
        var network = GameMain.Instance?.Network;
        if (client == null || network == null) return;

        string ip = string.IsNullOrWhiteSpace(_serverIp) ? "127.0.0.1" : _serverIp.Trim();

        switch (index)
        {
            case 0: // Вход
                if (string.IsNullOrWhiteSpace(_values[0]) || _values[0].Length < 3)
                {
                    _statusMessage = "Логин должен быть не менее 3 символов";
                    _statusColor = Color.OrangeRed;
                    return;
                }
                if (string.IsNullOrWhiteSpace(_values[1]))
                {
                    _statusMessage = "Пароль не может быть пустым";
                    _statusColor = Color.OrangeRed;
                    return;
                }
                if (!network.IsConnected)
                {
                    bool connected = await network.ConnectAsync(ip, 7777);
                    if (!connected)
                    {
                        _statusMessage = "Ошибка подключения";
                        _statusColor = Color.Red;
                        return;
                    }
                    SaveServerIp(ip);
                }
                client.Authenticate(_values[0], _values[1]);
                _statusMessage = "Авторизация...";
                _statusColor = Color.Yellow;
                break;

            case 1: // Регистрация
            {
                string? valError = ValidateRegistration(_values[0], _values[1]);
                if (valError != null)
                {
                    _statusMessage = valError;
                    _statusColor = Color.OrangeRed;
                    return;
                }
                if (!network.IsConnected)
                {
                    bool connected2 = await network.ConnectAsync(ip, 7777);
                    if (!connected2)
                    {
                        _statusMessage = "Ошибка подключения";
                        _statusColor = Color.Red;
                        return;
                    }
                    SaveServerIp(ip);
                }
                await client.SendAsync("register", new
                {
                    Login = _values[0],
                    Password = _values[1],
                    PlayerName = _values[0]
                });
                _statusMessage = "Регистрация...";
                _statusColor = Color.Yellow;
            }
                break;

            case 2: // Тестовый аккаунт (test / 123)
                _values[0] = "test";
                _values[1] = "123";
                if (!network.IsConnected)
                {
                    bool connected3 = await network.ConnectAsync(ip, 7777);
                    if (!connected3)
                    {
                        _statusMessage = "Ошибка подключения";
                        _statusColor = Color.Red;
                        return;
                    }
                    SaveServerIp(ip);
                }
                client.Authenticate(_values[0], _values[1]);
                _statusMessage = "Авторизация (test)...";
                _statusColor = Color.Yellow;
                break;
        }
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        spriteBatch.Begin();

        var font = SpriteCache.Font;
        if (font == null)
        {
            spriteBatch.End();
            return;
        }

        RebuildLayout();
        var (w, h) = GetSize();
        int centerX = w / 2;
        int startY = h / 2 - 120;

        // Заголовок
        var title = "LostAndDivine — MonoGame клиент";
        var titleSize = font.MeasureString(title);
        spriteBatch.DrawString(font, title, new Vector2(centerX - titleSize.X / 2, startY - 60), Color.Gold);

        var subtitle = "Авторизация";
        var subSize = font.MeasureString(subtitle);
        spriteBatch.DrawString(font, subtitle, new Vector2(centerX - subSize.X / 2, startY - 30), Color.White);

        // Поля ввода
        for (int i = 0; i < 3; i++)
        {
            // Label
            spriteBatch.DrawString(font, _labels[i], new Vector2(_fieldRects[i].X - 70, _fieldRects[i].Y + 5), Color.LightGray);

            // Field background
            var bgColor = _selectedField == i ? new Color(60, 60, 80) : new Color(40, 40, 55);
            spriteBatch.Draw(SpriteCache.Pixel, _fieldRects[i], bgColor);
            DrawBorder(spriteBatch, _fieldRects[i], _selectedField == i ? Color.DodgerBlue : new Color(80, 80, 100));

            // Field text
            var displayText = i == 1 ? new string('*', _values[i].Length) : _values[i];
            spriteBatch.DrawString(font, displayText, new Vector2(_fieldRects[i].X + 5, _fieldRects[i].Y + 5), Color.White);
        }

        // Кнопки
        var btnBgColors = new[] { new Color(0, 180, 100), new Color(255, 170, 0), new Color(150, 80, 200) };

        for (int i = 0; i < 5; i++)
        {
            var r = _buttonRects[i];
            bool hover = r.Contains(_prevMouse.X, _prevMouse.Y);
            bool press = hover && _prevMouse.LeftButton == ButtonState.Pressed;

            var color = btnBgColors[i];
            if (press)
                color = LerpColor(color, Color.Black, 0.3f);
            else if (hover)
                color = Brighten(color, 40);

            var drawRect = press ? new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2) : r;
            spriteBatch.Draw(SpriteCache.Pixel, drawRect, color);
            DrawBorder(spriteBatch, drawRect, hover ? Color.White : new Color(180, 180, 200));
            var btnSize = font.MeasureString(_buttonLabels[i]);
            spriteBatch.DrawString(font, _buttonLabels[i],
                new Vector2(drawRect.X + (drawRect.Width - btnSize.X) / 2,
                            drawRect.Y + (drawRect.Height - btnSize.Y) / 2),
                Color.White);
        }

        // Статус-бар
        {
            int sbarH = 40;
            var sbarRect = new Rectangle(0, h - sbarH, w, sbarH);
            spriteBatch.Draw(SpriteCache.Pixel, sbarRect, new Color(16, 16, 28, 230));
            DrawBorder(spriteBatch, sbarRect, new Color(60, 60, 80));
            var stSize = font.MeasureString(_statusMessage);
            spriteBatch.DrawString(font, _statusMessage, new Vector2((w - stSize.X) / 2, h - sbarH + (sbarH - stSize.Y) / 2), _statusColor);
        }

        // Подсказка
        spriteBatch.DrawString(font, "Enter — быстрый вход  |  Tab — переключение полей  |  «Тестовый аккаунт» — test/123",
            new Vector2(centerX - 320, startY + 285), Color.Gray);

        // Версия клиента
        var version = UpdateManager.LocalVersion;
        if (!string.IsNullOrEmpty(version))
            spriteBatch.DrawString(font, $"v{version}", new Vector2(12, h - 24), Color.Gray);

        // Иконка настроек (правый верхний угол)
        var settingsIcon = SpriteCache.GetIconSettings();
        spriteBatch.Draw(SpriteCache.Pixel, _settingsIconRect, new Color(40, 42, 56));
        DrawBorder(spriteBatch, _settingsIconRect, new Color(90, 95, 115));
        if (settingsIcon != null)
        {
            int pad = 6;
            spriteBatch.Draw(settingsIcon, new Rectangle(_settingsIconRect.X + pad, _settingsIconRect.Y + pad, _settingsIconRect.Width - pad * 2, _settingsIconRect.Height - pad * 2), Color.White);
        }
        else
        {
            var sSize = font.MeasureString("*");
            spriteBatch.DrawString(font, "*", new Vector2(_settingsIconRect.X + (_settingsIconRect.Width - sSize.X) / 2, _settingsIconRect.Y + (_settingsIconRect.Height - sSize.Y) / 2), Color.White);
        }

        spriteBatch.End();
    }

    private static void DrawBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
        => UIHelper.DrawRectOutline(sb, rect, color, thickness);

    private async Task AutoConnectAsync()
    {
        try
        {
            var network = GameMain.Instance?.Network;
            if (network == null || network.IsConnected) return;
            _statusMessage = "Подключение...";
            _statusColor = Color.Yellow;
            bool ok = await network.ConnectAsync(_serverIp, 7777);
            if (ok)
            {
                SaveServerIp(_serverIp);
                _statusMessage = "Подключено";
                _statusColor = Color.LimeGreen;
            }
            else
            {
                _statusMessage = "Сервер недоступен (адрес — в настройках)";
                _statusColor = Color.Red;
            }
        }
        catch { }
    }

    private static void SaveServerIp(string ip)
    {
        try
        {
            var settings = SettingsManager.Load();
            settings.ServerIp = ip;
            settings.Save();
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        var client = GameMain.Instance?.Client;
        if (client != null)
        {
            client.SystemMessage -= OnSystemMessage;
            client.ErrorReceived -= OnError;
        }
    }

    private static Color Brighten(Color c, int amount)
    {
        int R(int v) => Math.Min(255, v + amount);
        return new Color(R(c.R), R(c.G), R(c.B), c.A);
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        int L(int va, int vb) => (int)(va + (vb - va) * t);
        return new Color(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B), a.A);
    }

    private static string? ValidateRegistration(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Length < 3 || login.Length > 20)
            return "Логин: от 3 до 20 символов";
        if (login.Any(c => char.IsWhiteSpace(c)))
            return "Логин не должен содержать пробелы";
        if (!login.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            return "Логин: только латинские буквы и цифры";

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6 || password.Length > 50)
            return "Пароль: от 6 до 50 символов";
        if (password.Any(c => char.IsWhiteSpace(c)))
            return "Пароль не должен содержать пробелы";
        if (!password.Any(char.IsUpper))
            return "Пароль: минимум одна заглавная буква";
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            return "Пароль: минимум один спецсимвол (!№;% и т.д.)";

        return null;
    }

    private void OnSystemMessage(string msg)
    {
        _statusMessage = msg;
        _statusColor = Color.Yellow;
    }

    private void OnError(string msg)
    {
        _statusMessage = $"Ошибка: {msg}";
        _statusColor = Color.Red;
    }
}
