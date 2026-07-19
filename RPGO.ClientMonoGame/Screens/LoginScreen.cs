using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Screens;

public class LoginScreen : IScreen
{
    private readonly string[] _labels = { "IP:", "Логин:", "Пароль:", "Имя:" };
    private readonly string[] _defaults = { "127.0.0.1", "", "", "" };
    private readonly string[] _values = new string[4];

    private int _selectedField = -1;
    private string _statusMessage = "Не подключено";
    private Color _statusColor = Color.Red;
    private string _systemMessage = "";

    private readonly string[] _buttonLabels = { "Подключиться", "Вход", "Регистрация", "Тестовый аккаунт" };
    private Rectangle[] _buttonRects = new Rectangle[4];
    private Rectangle[] _fieldRects = new Rectangle[4];

    private KeyboardState _prevKeyboard;
    private MouseState _prevMouse;

    private Rectangle _settingsIconRect = Rectangle.Empty;

    public LoginScreen()
    {
        Array.Copy(_defaults, _values, 4);
        RebuildLayout();

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

        for (int i = 0; i < 4; i++)
            _fieldRects[i] = new Rectangle(centerX - 60, startY + i * 45, 200, 30);

        _buttonRects[0] = new Rectangle(centerX + 160, startY, 130, 30);
        _buttonRects[1] = new Rectangle(centerX + 160, startY + 38, 130, 30);
        _buttonRects[2] = new Rectangle(centerX + 160, startY + 76, 130, 30);
        _buttonRects[3] = new Rectangle(centerX + 160, startY + 116, 130, 30);

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
            for (int k = (int)Keys.D0; k <= (int)Keys.D9; k++)
            {
                if (keyboard.IsKeyDown((Keys)k) && _prevKeyboard.IsKeyUp((Keys)k))
                    _values[_selectedField] += (char)('0' + k - (int)Keys.D0);
            }
            if (keyboard.IsKeyDown(Keys.OemPeriod) && _prevKeyboard.IsKeyUp(Keys.OemPeriod))
                _values[_selectedField] += '.';
            if (keyboard.IsKeyDown(Keys.Back) && _prevKeyboard.IsKeyUp(Keys.Back) && _values[_selectedField].Length > 0)
                _values[_selectedField] = _values[_selectedField][..^1];
        }

        // Клик по полю ввода
        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
        {
            _selectedField = -1;
            for (int i = 0; i < 4; i++)
            {
                if (_fieldRects[i].Contains(mouse.X, mouse.Y))
                    _selectedField = i;
            }

            // Клик по кнопкам
            for (int i = 0; i < 4; i++)
            {
                if (_buttonRects[i].Contains(mouse.X, mouse.Y))
                    HandleButton(i);
            }

            // Клик по иконке настроек
            if (_settingsIconRect.Contains(mouse.X, mouse.Y))
                GameMain.Instance!.ShowSettings();
        }

        // Enter для быстрого входа
        if (keyboard.IsKeyDown(Keys.Enter) && _prevKeyboard.IsKeyUp(Keys.Enter))
            HandleButton(1);

        _prevKeyboard = keyboard;
        _prevMouse = mouse;
    }

    private async void HandleButton(int index)
    {
        var client = GameMain.Instance?.Client;
        var network = GameMain.Instance?.Network;
        if (client == null || network == null) return;

        string ip = string.IsNullOrWhiteSpace(_values[0]) ? "127.0.0.1" : _values[0].Trim();

        switch (index)
        {
            case 0: // Подключиться
                _statusMessage = "Подключение...";
                _statusColor = Color.Yellow;
                bool ok = await network.ConnectAsync(ip, 7777);
                if (ok)
                {
                    _statusMessage = "Подключено";
                    _statusColor = Color.LimeGreen;
                }
                else
                {
                    _statusMessage = "Ошибка подключения";
                    _statusColor = Color.Red;
                }
                break;

            case 1: // Вход
                if (!network.IsConnected)
                {
                    bool connected = await network.ConnectAsync(ip, 7777);
                    if (!connected)
                    {
                        _statusMessage = "Ошибка подключения";
                        _statusColor = Color.Red;
                        return;
                    }
                }
                client.Authenticate(_values[1], _values[2]);
                _statusMessage = "Авторизация...";
                _statusColor = Color.Yellow;
                break;

            case 2: // Регистрация
                if (!network.IsConnected)
                {
                    bool connected2 = await network.ConnectAsync(ip, 7777);
                    if (!connected2)
                    {
                        _statusMessage = "Ошибка подключения";
                        _statusColor = Color.Red;
                        return;
                    }
                }
                await client.SendAsync("register", new
                {
                    Login = _values[1],
                    Password = _values[2],
                    PlayerName = string.IsNullOrWhiteSpace(_values[3]) ? _values[1] : _values[3]
                });
                _statusMessage = "Регистрация...";
                _statusColor = Color.Yellow;
                break;

            case 3: // Тестовый аккаунт (test / 123)
                _values[0] = ip;
                _values[1] = "test";
                _values[2] = "123";
                _values[3] = "test";
                if (!network.IsConnected)
                {
                    bool connected3 = await network.ConnectAsync(ip, 7777);
                    if (!connected3)
                    {
                        _statusMessage = "Ошибка подключения";
                        _statusColor = Color.Red;
                        return;
                    }
                }
                client.Authenticate(_values[1], _values[2]);
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
        var title = "RPGO — MonoGame клиент";
        var titleSize = font.MeasureString(title);
        spriteBatch.DrawString(font, title, new Vector2(centerX - titleSize.X / 2, startY - 60), Color.Gold);

        var subtitle = "Авторизация";
        var subSize = font.MeasureString(subtitle);
        spriteBatch.DrawString(font, subtitle, new Vector2(centerX - subSize.X / 2, startY - 30), Color.White);

        // Поля ввода
        for (int i = 0; i < 4; i++)
        {
            // Label
            spriteBatch.DrawString(font, _labels[i], new Vector2(_fieldRects[i].X - 70, _fieldRects[i].Y + 5), Color.LightGray);

            // Field background
            var bgColor = _selectedField == i ? new Color(60, 60, 80) : new Color(40, 40, 55);
            spriteBatch.Draw(SpriteCache.Pixel, _fieldRects[i], bgColor);
            DrawBorder(spriteBatch, _fieldRects[i], _selectedField == i ? Color.DodgerBlue : new Color(80, 80, 100));

            // Field text
            var displayText = i == 2 ? new string('*', _values[i].Length) : _values[i];
            spriteBatch.DrawString(font, displayText, new Vector2(_fieldRects[i].X + 5, _fieldRects[i].Y + 5), Color.White);
        }

        // Кнопки
        var btnBgColors = new[] { new Color(0, 120, 215), new Color(0, 180, 100), new Color(255, 170, 0), new Color(150, 80, 200) };

        for (int i = 0; i < 4; i++)
        {
            spriteBatch.Draw(SpriteCache.Pixel, _buttonRects[i], btnBgColors[i]);
            DrawBorder(spriteBatch, _buttonRects[i], Color.White);
            var btnSize = font.MeasureString(_buttonLabels[i]);
            spriteBatch.DrawString(font, _buttonLabels[i],
                new Vector2(_buttonRects[i].X + (_buttonRects[i].Width - btnSize.X) / 2,
                            _buttonRects[i].Y + (_buttonRects[i].Height - btnSize.Y) / 2),
                Color.White);
        }

        // Статус
        spriteBatch.DrawString(font, _statusMessage, new Vector2(centerX - 100, startY + 210), _statusColor);

        // Системное сообщение
        if (!string.IsNullOrEmpty(_systemMessage))
            spriteBatch.DrawString(font, _systemMessage, new Vector2(centerX - 150, startY + 240), Color.Yellow);

        // Подсказка
        spriteBatch.DrawString(font, "Enter — быстрый вход  |  Tab — переключение полей  |  «Тестовый аккаунт» — test/123",
            new Vector2(centerX - 320, startY + 285), Color.Gray);

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
            var sSize = font.MeasureString("⚙");
            spriteBatch.DrawString(font, "⚙", new Vector2(_settingsIconRect.X + (_settingsIconRect.Width - sSize.X) / 2, _settingsIconRect.Y + (_settingsIconRect.Height - sSize.Y) / 2), Color.White);
        }

        spriteBatch.End();
    }

    private static void DrawBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
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

    private void OnSystemMessage(string msg)
    {
        _systemMessage = msg;
    }

    private void OnError(string msg)
    {
        _statusMessage = $"Ошибка: {msg}";
        _statusColor = Color.Red;
    }
}
