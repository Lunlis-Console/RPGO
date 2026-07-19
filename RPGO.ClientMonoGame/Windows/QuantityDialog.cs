using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;

namespace RPGGame.ClientMonoGame.Windows
{
    public class QuantityDialog : GameWindow
    {
        private string _itemName = "";
        private int _maxQuantity;
        private int _pricePerUnit;
        private KeyboardState _prevKeyboard;
        private MouseState _prevMouse;
        private bool _sliderDrag;

        private Rectangle _trackRect;
        private int _knobSize = 18;

        public int SelectedQuantity { get; private set; }
        public Action<int>? OnConfirm { get; set; }
        public bool ShowPrice { get; set; } = true;

        public QuantityDialog()
        {
            Title = "Количество";
            Width = 320;
            Height = 180;
            Visible = false;
        }

        public void Setup(string itemName, int maxQuantity, int pricePerUnit)
        {
            _itemName = itemName;
            _maxQuantity = Math.Max(1, maxQuantity);
            _pricePerUnit = pricePerUnit;
            SelectedQuantity = 1;
            _sliderDrag = false;
            Visible = true;
        }

        private int QuantityFromX(int x)
        {
            if (_maxQuantity <= 1) return 1;
            int usable = _trackRect.Width - _knobSize;
            float t = (x - (_trackRect.X + _knobSize / 2)) / (float)usable;
            t = Math.Clamp(t, 0, 1);
            return 1 + (int)Math.Round(t * (_maxQuantity - 1));
        }

        public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
        {
            if (!Visible) return;

            if (keyboard.IsKeyDown(Keys.Left) && !_prevKeyboard.IsKeyDown(Keys.Left) && SelectedQuantity > 1)
                SelectedQuantity--;
            else if (keyboard.IsKeyDown(Keys.Right) && !_prevKeyboard.IsKeyDown(Keys.Right) && SelectedQuantity < _maxQuantity)
                SelectedQuantity++;

            _prevKeyboard = keyboard;

            int cx = ContentX;
            int cy = ContentY;
            _trackRect = new Rectangle(cx, cy + 60, Width - 32, 10);

            bool pressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
            bool down = mouse.LeftButton == ButtonState.Pressed;

            int knobX = _trackRect.X + KnobOffset();
            var knobRect = new Rectangle(knobX, _trackRect.Y - (_knobSize - _trackRect.Height) / 2, _knobSize, _knobSize);

            if (pressed && (knobRect.Contains(mouse.X, mouse.Y) || _trackRect.Contains(mouse.X, mouse.Y)))
            {
                _sliderDrag = true;
                SelectedQuantity = QuantityFromX(mouse.X);
            }
            else if (_sliderDrag && down)
            {
                SelectedQuantity = QuantityFromX(mouse.X);
            }
            else if (!down)
            {
                _sliderDrag = false;
            }

            int btnAreaY = cy + 120;
            var okBtn = new Rectangle(cx + 30, btnAreaY, 80, 28);
            var cancelBtn = new Rectangle(cx + 130, btnAreaY, 80, 28);

            if (pressed)
            {
                if (okBtn.Contains(mouse.X, mouse.Y))
                {
                    OnConfirm?.Invoke(SelectedQuantity);
                    Visible = false;
                    return;
                }
                if (cancelBtn.Contains(mouse.X, mouse.Y))
                {
                    SelectedQuantity = 0;
                    Visible = false;
                    return;
                }
            }

            base.Update(gameTime, keyboard, mouse);
            _prevMouse = mouse;
        }

        private int KnobOffset()
        {
            if (_maxQuantity <= 1) return 0;
            int usable = _trackRect.Width - _knobSize;
            float t = (SelectedQuantity - 1) / (float)(_maxQuantity - 1);
            return (int)Math.Round(t * usable);
        }

        public override void Draw(SpriteBatch sb)
        {
            if (!Visible) return;

            base.Draw(sb, Mouse.GetState());

            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (font == null) return;

            var mouse = Mouse.GetState();
            int cx = ContentX;
            int cy = ContentY;
            int total = SelectedQuantity * _pricePerUnit;

            DrawText(sb, _itemName, cx, cy, Color.White);
            DrawText(sb, $"Кол-во: {SelectedQuantity} / {_maxQuantity}", cx, cy + 30, Color.LightGray);

            // Ползунок
            sb.Draw(SpriteCache.Pixel, _trackRect, new Color(60, 65, 85));
            int knobX = _trackRect.X + KnobOffset();
            var knobRect = new Rectangle(knobX, _trackRect.Y - (_knobSize - _trackRect.Height) / 2, _knobSize, _knobSize);
            bool knobHover = knobRect.Contains(mouse.X, mouse.Y) || _trackRect.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, knobRect, knobHover ? new Color(120, 170, 230) : new Color(90, 130, 190));

            if (ShowPrice)
                DrawText(sb, $"Итого: {total} gold", cx, cy + 92, Color.Gold);

            int btnAreaY = cy + 120;
            var okColor = new Color(50, 120, 50);
            var cancelColor = new Color(120, 50, 50);
            DrawButton(sb, "ОК", cx + 30, btnAreaY, 80, 28, okColor, mouse, _prevMouse);
            DrawButton(sb, "ОТМЕНА", cx + 130, btnAreaY, 80, 28, cancelColor, mouse, _prevMouse);
        }
    }
}
