using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Networking;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.ClientMonoGame.Input;
using LostAndDivine.ClientMonoGame.Screens;
using LostAndDivine.ClientMonoGame.Windows;

namespace LostAndDivine.ClientMonoGame;

public class GameMain : Game
{
    public static GameMain? Instance { get; private set; }

    public GraphicsDeviceManager Graphics { get; }
    public SpriteBatch SpriteBatch { get; private set; } = null!;

    public GameClient Client { get; } = new();
    public NetworkManager Network { get; } = new();

    private ScreenManager _screens = null!;
    private KeyboardState _prevKb;

    // Обрыв соединения: пишется из сетевого потока, обрабатывается в Update (главный поток).
    private volatile string? _pendingDisconnectReason;

    // Идёт переподключение: поверх игры рисуется оверлей, ввод заморожен.
    private bool _reconnecting;

    // Неудачное переподключение: пишется из сетевого потока, обрабатывается в Update.
    private volatile bool _pendingReconnectFailed;

    public GameMain()
    {
        Instance = this;

        var settings = SettingsManager.Load();

        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = settings.Width,
            PreferredBackBufferHeight = settings.Height,
            SynchronizeWithVerticalRetrace = true
        };

        switch (settings.Mode)
        {
            case "fullscreen":
                Graphics.IsFullScreen = true;
                Window.IsBorderless = false;
                break;
            case "borderless":
                Graphics.IsFullScreen = true;
                Window.IsBorderless = true;
                break;
            default:
                Graphics.IsFullScreen = false;
                Window.IsBorderless = false;
                break;
        }
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        Window.Title = "LostAndDivine — MonoGame клиент";
        IsFixedTimeStep = true;
        TargetElapsedTime = System.TimeSpan.FromMilliseconds(16.67); // ~60fps

        // Обычный оконный режим (не на весь экран)
        Window.IsBorderless = false;
        Graphics.IsFullScreen = false;
        Window.AllowUserResizing = true;

        // Оконный режим: если разрешение не влезает в рабочую область монитора
        // (например, 1920x1080 при панели задач), уменьшаем окно под панель
        // и центрируем его в рабочей области.
        var (fitW, fitH) = WindowBoundsHelper.FitToWorkArea(settings.Width, settings.Height);
        Graphics.PreferredBackBufferWidth = fitW;
        Graphics.PreferredBackBufferHeight = fitH;
        WindowBoundsHelper.PositionWindow(Window, fitW, fitH);
    }

    protected override void Initialize()
    {
        // Стартовая проверка обновлений (до показа логина): сравнение версии с сервером,
        // при наличии обновления — скачивание и перезапуск.
        Window.Title = "Проверка обновлений...";
        bool updateApplied = UpdateManager.RunStartupCheck();
        if (updateApplied)
        {
            UpdateManager.RestartToApply();
            Environment.Exit(0);
            return;
        }
        Window.Title = "LostAndDivine — MonoGame клиент";

        // Инициализация GameClient с колбэком для UI-потока
        Client.Initialize(() => { });

        // Привязка сетевых событий
        Network.MessageReceived += msg => Client.HandleMessage(msg);
        Network.Connected += () => Client.OnConnected();
        Network.Disconnected += () => Client.OnDisconnected("Соединение закрыто");
        Network.ConnectionLost += reason => Client.OnDisconnected(reason);
        Network.ReconnectStateReceived += state => Client.OnReconnectState(state);
        Network.ReconnectStateReceived += state => { _reconnecting = false; };
        Network.ReconnectFailed += OnReconnectFailed;

        _screens = new ScreenManager();
        _screens.ShowLogin();

        Client.CharacterListUpdated += chars =>
        {
            if (_screens.CurrentScreen is CharacterSelectScreen cs)
                cs.SetCharacters(chars);
            else if (!_screens.IsGameActive)
                _screens.ShowCharacterSelect(chars);
        };
        Client.WelcomeReceived += () => _screens.ShowGame();
        Client.SystemMessage += msg => Logger.Info($"System: {msg}");
        Client.Disconnected += reason => _pendingDisconnectReason = reason;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        Content.RootDirectory = "Content";
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        SpriteCache.Load(GraphicsDevice, Content);
        SpriteCache.LoadAnimations(Path.Combine(AppContext.BaseDirectory, "Content"));
        SpriteCache.LoadCursors(Path.Combine(AppContext.BaseDirectory, "Content"));
    }

    public void ShowSettings()
    {
        _screens.ShowModal(new SettingsScreen());
    }

    public void CloseSettings()
    {
        _screens.CloseModal();
    }

    public void ShowLogin()
    {
        _screens.ShowLogin();
    }

    /// <summary>
    /// Обработка обрыва соединения во время игры: если есть сессия — показываем
    /// оверлей «Переподключение...» и даём авто-reconnect шанс вернуть игрока в бой.
    /// Без сессии (выход в меню) — сразу возвращаем к экрану входа.
    /// </summary>
    private void HandleConnectionLost(string reason)
    {
        if (!_screens.IsGameActive) return;
        if (Network.IsConnected) return; // уже успели переподключиться

        string msg = string.IsNullOrWhiteSpace(reason)
            ? "Соединение с сервером потеряно"
            : $"Соединение с сервером потеряно: {reason}";

        if (Network.HasSession)
        {
            Logger.Warn($"{msg} — показываем оверлей переподключения");
            _reconnecting = true;
            return;
        }

        Logger.Warn(msg);
        _screens.ShowLogin(msg);
    }

    /// <summary>
    /// Авто-reconnect исчерпан (лимит времени или сервер отклонил сессию):
    /// немедленно останавливаем попытки и сбрасываем сессию. Возврат к экрану
    /// входа выполняется на главном потоке (Update), т.к. событие приходит
    /// из сетевого потока.
    /// </summary>
    private void OnReconnectFailed()
    {
        _reconnecting = false;
        try { Network.StopReconnect(); } catch { }
        _pendingReconnectFailed = true;
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();

        // Переключение полноэкранный/оконный режим
        if ((keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt)) && keyboard.IsKeyDown(Keys.Enter))
        {
            ToggleFullscreen();
        }
        if (keyboard.IsKeyDown(Keys.F11) && _prevKb.IsKeyUp(Keys.F11))
        {
            ToggleFullscreen();
        }

        if (keyboard.IsKeyDown(Keys.Escape))
        {
            if (_screens.HasModal)
                _screens.CloseModal();
            // Esc больше не закрывает клиент (например, при вводе текста в чат)
        }

        // Обрыв соединения: показываем оверлей переподключения (или возвращаем
        // к экрану входа, если сессии нет). Обработка на главном потоке,
        // чтобы не гонять смену экранов/оверлея из сетевого потока.
        var pendingReason = _pendingDisconnectReason;
        if (pendingReason != null)
        {
            _pendingDisconnectReason = null;
            HandleConnectionLost(pendingReason);
        }

        // Неудачное переподключение: возврат к экрану входа (на главном потоке).
        if (_pendingReconnectFailed)
        {
            _pendingReconnectFailed = false;
            if (_screens.IsGameActive)
            {
                Logger.Warn("Переподключение не удалось, возврат к экрану входа");
                _screens.ShowLogin("Соединение с сервером потеряно. Переподключение не удалось.");
            }
        }

        // Пока идёт переподключение — игра заморожена под оверлеем:
        // ввод не обрабатываем (нет случайных SendAsync на мёртвом соединении).
        if (!_reconnecting)
            _screens.Update(gameTime, keyboard, mouse);

        _prevKb = keyboard;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(24, 24, 32));
        _screens.Draw(gameTime, SpriteBatch);

        // Оверлей «Переподключение...» поверх замороженной игры
        if (_reconnecting)
        {
            int w = Graphics.PreferredBackBufferWidth;
            int h = Graphics.PreferredBackBufferHeight;
            SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            SpriteBatch.Draw(SpriteCache.Pixel, new Rectangle(0, 0, w, h), new Color(0, 0, 0, 170));
            var font = SpriteCache.Font;
            if (font != null)
            {
                var title = "Переподключение...";
                var hint = "Потеряно соединение с сервером. Попытка восстановить сеанс...";
                var titleSize = font.MeasureString(title);
                var hintSize = font.MeasureString(hint);
                SpriteBatch.DrawString(font, title, new Vector2((w - titleSize.X) / 2, h / 2 - 40), Color.Gold);
                SpriteBatch.DrawString(font, hint, new Vector2((w - hintSize.X) / 2, h / 2), Color.LightGray);
            }
            SpriteBatch.End();
        }

        // Custom cursor поверх всех экранов
        {
            var ms = Mouse.GetState();
            string ct = Screens.GameScreen.CurrentCursorType ?? "main";
            var tex = SpriteCache.GetCursor(ct) ?? SpriteCache.GetCursor("main");
            if (tex != null)
            {
                var hs = SpriteCache.GetCursorHotspot(ct);
                const float scale = 0.75f;
                int scaledHs = (int)Math.Round(hs.X * scale);
                int scaledHsY = (int)Math.Round(hs.Y * scale);
                float drawW = tex.Width * scale;
                float drawH = tex.Height * scale;
                SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
                SpriteBatch.Draw(tex, new Rectangle(ms.X - scaledHs, ms.Y - scaledHsY, (int)drawW, (int)drawH), Color.White);
                SpriteBatch.End();
            }
        }

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        SpriteCache.Unload();
        base.UnloadContent();
    }

    /// <summary>
    /// Alt+Enter / F11: переключение полноэкранный ↔ оконный. При возврате
    /// в оконный режим окно подгоняется под рабочую область монитора (панель
    /// задач не перекрывает игру), при переходе в полный экран восстанавливается
    /// разрешение из настроек.
    /// </summary>
    private void ToggleFullscreen()
    {
        bool goWindowed = Graphics.IsFullScreen;
        var s = SettingsManager.Load();
        Graphics.IsFullScreen = !goWindowed;
        Window.IsBorderless = false;
        if (goWindowed)
        {
            var (fitW, fitH) = WindowBoundsHelper.FitToWorkArea(s.Width, s.Height);
            Graphics.PreferredBackBufferWidth = fitW;
            Graphics.PreferredBackBufferHeight = fitH;
            Graphics.ApplyChanges();
            WindowBoundsHelper.PositionWindow(Window, fitW, fitH);
        }
        else
        {
            Graphics.PreferredBackBufferWidth = s.Width;
            Graphics.PreferredBackBufferHeight = s.Height;
            Graphics.ApplyChanges();
        }
    }
}
