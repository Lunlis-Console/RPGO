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
    public IScreen? CurrentScreen => _current;
    public bool IsGameActive => _current is GameScreen;

    public void ShowLogin(string? statusMessage = null)
    {
        _current?.Dispose();
        _current = new LoginScreen(statusMessage);
    }

    public void ShowCharacterSelect(CharacterSlot[]? characters = null)
    {
        _current?.Dispose();
        var screen = new CharacterSelectScreen();
        if (characters != null)
            screen.SetCharacters(characters);
        _current = screen;
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
