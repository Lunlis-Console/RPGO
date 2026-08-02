using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Screens;

namespace RPGGame.ClientMonoGame.Screens;

public class ScreenManager
{
    private IScreen? _current;
    private IScreen? _modal;

    public bool HasModal => _modal != null;

    /// <summary>Активен ли сейчас игровой экран (для обработки обрыва соединения).</summary>
    public bool IsGameActive => _current is GameScreen;

    public void ShowLogin(string? statusMessage = null)
    {
        _current?.Dispose();
        _current = new LoginScreen(statusMessage);
    }

    public void ShowGame()
    {
        _current?.Dispose();
        _current = new GameScreen();
    }

    public void ShowModal(IScreen modal)
    {
        _modal = modal;
    }

    public void CloseModal()
    {
        _modal?.Dispose();
        _modal = null;
    }

    public void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        _current?.Update(gameTime, keyboard, mouse);
        _modal?.Update(gameTime, keyboard, mouse);
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        _current?.Draw(gameTime, spriteBatch);
        _modal?.Draw(gameTime, spriteBatch);
    }
}

public interface IScreen : IDisposable
{
    void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse);
    void Draw(GameTime gameTime, SpriteBatch spriteBatch);
}
