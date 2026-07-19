using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Windows;

public sealed class WindowManager
{
    private readonly List<GameWindow> _windows = new();

    public void Add(GameWindow window) => _windows.Add(window);

    public void BringToFront(GameWindow window)
    {
        if (_windows.Remove(window))
            _windows.Add(window);
    }

    private Rectangle GetRect(GameWindow w) => new Rectangle(w.X, w.Y, w.Width, w.Height);

    public bool IsMouseOverVisibleWindow(int x, int y)
    {
        foreach (var w in _windows)
        {
            if (w.Visible && GetRect(w).Contains(x, y))
                return true;
        }
        return false;
    }

    private bool IsCovered(GameWindow target, int x, int y)
    {
        int idx = _windows.IndexOf(target);
        for (int i = idx + 1; i < _windows.Count; i++)
        {
            var w = _windows[i];
            if (w.Visible && GetRect(w).Contains(x, y))
                return true;
        }
        return false;
    }

    public void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        bool clicked = mouse.LeftButton == ButtonState.Pressed;

        var modal = _windows.LastOrDefault(w => w.Visible && w.IsModal);

        // Если какое-либо окно выполняет внутренний drag-n-drop —
        // не меняем z-order и не гасим кнопки мыши у перетаскивающего окна,
        // иначе drag прервётся при переходе курсора на другое окно.
        bool anyDragging = false;
        foreach (var w in _windows)
            if (w.Visible && w.IsDragging) { anyDragging = true; break; }

        GameWindow? toFront = null;
        // Копируем список, т.к. Update окна может изменить _windows (BringToFront)
        var snapshot = _windows.ToList();
        foreach (var w in snapshot)
        {
            if (!w.Visible) continue;

            bool covered = IsCovered(w, mouse.X, mouse.Y);
            bool blockedByModal = modal != null && w != modal;

            if (!anyDragging && clicked && !covered && !blockedByModal && GetRect(w).Contains(mouse.X, mouse.Y))
                toFront = w;

            MouseState ms = mouse;
            // Перетаскивающее окно всегда получает реальную мышь
            if (!w.IsDragging && (covered || blockedByModal) && clicked)
            {
                ms = new MouseState(
                    mouse.X, mouse.Y, mouse.ScrollWheelValue,
                    ButtonState.Released, ButtonState.Released, ButtonState.Released,
                    ButtonState.Released, ButtonState.Released);
            }

            w.Update(gameTime, keyboard, ms);
        }

        // Не поднимаем окно поверх модального диалога. Важно: модальное окно
        // могло открыться внутри этого же кадра (например, QuantityDialog по
        // клику в TradeWindow), поэтому проверяем наличие в самый последний момент.
        bool hasModalNow = _windows.Any(w => w.Visible && w.IsModal);
        if (!anyDragging && toFront != null && !hasModalNow)
            BringToFront(toFront);
    }

    public void Draw(GameTime gameTime, SpriteBatch sb)
    {
        foreach (var w in _windows)
            w.Draw(sb);
    }
}
