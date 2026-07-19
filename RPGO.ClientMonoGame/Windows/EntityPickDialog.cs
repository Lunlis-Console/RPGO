using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Data;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Windows
{
    // Окно выбора сущности, когда в одной клетке несколько сущностей.
    public class EntityPickDialog : GameWindow
    {
        private List<EntityInfo> _entities = new();
        private int _mapX, _mapY;
        private Rectangle[] _rowRects = Array.Empty<Rectangle>();
        private MouseState _prevMouse;

        public Action<EntityInfo, int, int>? OnPick { get; set; }

        public EntityPickDialog()
        {
            Title = "Выберите цель";
            Width = 320;
            Height = 60;
            IsModal = true;
            Visible = false;
        }

        public void Setup(List<EntityInfo> entities, int mapX, int mapY)
        {
            _entities = entities;
            _mapX = mapX;
            _mapY = mapY;

            int rowH = 26;
            int rows = Math.Max(1, entities.Count);
            Height = 40 + rows * rowH + 40; // заголовок + строки + отступ
            if (Height > 420) Height = 420;

            Visible = true;
        }

        public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
        {
            if (!Visible) return;

            base.Update(gameTime, keyboard, mouse);

            bool pressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
            if (pressed)
            {
                for (int i = 0; i < _rowRects.Length; i++)
                {
                    if (_rowRects[i].Contains(mouse.X, mouse.Y) && i < _entities.Count)
                    {
                        var e = _entities[i];
                        Visible = false;
                        OnPick?.Invoke(e, _mapX, _mapY);
                        _prevMouse = mouse;
                        return;
                    }
                }
            }

            _prevMouse = mouse;
        }

        public override void Draw(SpriteBatch sb)
        {
            if (!Visible) return;
            base.Draw(sb, Mouse.GetState());

            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (font == null) return;

            var mouse = Mouse.GetState();
            int x = ContentX;
            int y = ContentY;
            int rowH = 26;
            int w = ContentW;

            DrawText(sb, "На этой клетке несколько сущностей:", x, y, Color.LightGray);
            y += 24;

            _rowRects = new Rectangle[_entities.Count];
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                var rect = new Rectangle(x, y, w, rowH);
                _rowRects[i] = rect;

                bool hover = rect.Contains(mouse.X, mouse.Y);
                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(60, 80, 120) : new Color(45, 48, 60));
                sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(80, 90, 110));

                string text = e.Type switch
                {
                    "monster" => $"⚔ {e.Name} [Ур.{e.Level}] HP {e.Hp}/{e.MaxHp}",
                    "player" => $"👤 {e.Name} [Ур.{e.Level}] HP {e.Hp}/{e.MaxHp}",
                    "merchant" => $"Магазин: {e.Name}",
                    "board" => "Доска заданий",
                    "collectible" => $"Сбор: {e.Name}",
                    "corpse" => $"Труп: {e.Name} [Ур.{e.Level}]",
                    _ => e.Name
                };
                DrawText(sb, text, rect.X + 8, rect.Y + (int)((rowH - font.MeasureString(text).Y) / 2), Color.White);
                y += rowH;
            }
        }
    }
}
