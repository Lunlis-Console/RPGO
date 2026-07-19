using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Input;
using RPGGame.ClientMonoGame.Screens;
using RPGGame.ClientMonoGame.Windows;

namespace RPGGame.ClientMonoGame;

public class GameMain : Game
{
    public static GameMain? Instance { get; private set; }

    public GraphicsDeviceManager Graphics { get; }
    public SpriteBatch SpriteBatch { get; private set; } = null!;

    public GameClient Client { get; } = new();
    public NetworkManager Network { get; } = new();

    private ScreenManager _screens = null!;
    private KeyboardState _prevKb;

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
        IsMouseVisible = true;
        Window.Title = "RPGO — MonoGame клиент";
        IsFixedTimeStep = true;
        TargetElapsedTime = System.TimeSpan.FromMilliseconds(16.67); // ~60fps

        // Обычный оконный режим (не на весь экран)
        Window.IsBorderless = false;
        Graphics.IsFullScreen = false;
        Window.AllowUserResizing = true;
        Window.Position = new Microsoft.Xna.Framework.Point(100, 100);
    }

    protected override void Initialize()
    {
        // Инициализация GameClient с колбэком для UI-потока
        Client.Initialize(() => { });

        // Привязка сетевых событий
        Network.MessageReceived += msg => Client.HandleMessage(msg);
        Network.Connected += () => Client.OnConnected();
        Network.Disconnected += () => Client.OnDisconnected("Соединение закрыто");
        Network.ConnectionLost += reason => Client.OnDisconnected(reason);
        Network.ReconnectStateReceived += state => Client.OnReconnectState(state);

        _screens = new ScreenManager();
        _screens.ShowLogin();

        Client.WelcomeReceived += () => _screens.ShowGame();
        Client.SystemMessage += msg => Logger.Info($"System: {msg}");

        base.Initialize();
    }

    protected override void LoadContent()
    {
        Content.RootDirectory = "Content";
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        SpriteCache.Load(GraphicsDevice, Content);
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

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();

        // Переключение полноэкранный/оконный режим
        if ((keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt)) && keyboard.IsKeyDown(Keys.Enter))
        {
            Graphics.IsFullScreen = !Graphics.IsFullScreen;
            Window.IsBorderless = false;
            Graphics.ApplyChanges();
        }
        if (keyboard.IsKeyDown(Keys.F11) && _prevKb.IsKeyUp(Keys.F11))
        {
            Graphics.IsFullScreen = !Graphics.IsFullScreen;
            Window.IsBorderless = false;
            Graphics.ApplyChanges();
        }

        if (keyboard.IsKeyDown(Keys.Escape))
        {
            if (_screens.HasModal)
                _screens.CloseModal();
            // Esc больше не закрывает клиент (например, при вводе текста в чат)
        }

        _screens.Update(gameTime, keyboard, mouse);

        _prevKb = keyboard;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(24, 24, 32));
        _screens.Draw(gameTime, SpriteBatch);
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        SpriteCache.Unload();
        base.UnloadContent();
    }
}
