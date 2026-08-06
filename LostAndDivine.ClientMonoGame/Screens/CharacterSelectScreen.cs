using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Networking;
using LostAndDivine.ClientMonoGame.Rendering;

namespace LostAndDivine.ClientMonoGame.Screens;

public class CharacterSelectScreen : IScreen
{
    private readonly CharacterSlot[] _slots;
    private int _selected = -1;
    private string _status = "";
    private Color _statusColor = Color.White;

    private bool _creating;
    private string _createName = "";
    private int _createClass;
    private static readonly string[] ClassNames =
    {
        "Воин", "Разбойник", "Некромаг", "Маг стихий", "Потрошитель", "Колдун"
    };

    private bool _confirmDelete;
    private string _pendingDeleteName = "";

    private Rectangle _btnEnter;
    private Rectangle _btnCreate;
    private Rectangle _btnDelete;
    private Rectangle _btnBack;
    private Rectangle _btnLeftClass;
    private Rectangle _btnRightClass;
    private Rectangle _nameField;
    private Rectangle _btnCreateConfirm;
    private Rectangle _btnCreateCancel;
    private Rectangle _btnDeleteConfirm;
    private Rectangle _btnDeleteCancel;

    private KeyboardState _prevKb;
    private MouseState _prevMouse;

    public CharacterSelectScreen()
    {
        _slots = new CharacterSlot[5];
        for (int i = 0; i < _slots.Length; i++)
            _slots[i] = new CharacterSlot();
        SubHooks();
        RebuildLayout();
    }

    private void SubHooks()
    {
        var c = GameMain.Instance?.Client;
        if (c == null) return;
        c.CharacterListUpdated += OnCharacterList;
        c.SystemMessage += OnSystemMessage;
    }

    public void SetCharacters(CharacterSlot[] chars)
    {
        for (int i = 0; i < _slots.Length; i++)
            _slots[i] = i < chars.Length ? chars[i] : new CharacterSlot();
    }

    public void SetStatus(string msg, Color color)
    {
        _status = msg;
        _statusColor = color;
    }

    private void OnSystemMessage(string msg)
    {
        _status = msg;
        _statusColor = Color.OrangeRed;
        _creating = false;
    }

    private void OnCharacterList(CharacterSlot[] chars)
    {
        for (int i = 0; i < _slots.Length; i++)
            _slots[i] = i < chars.Length ? chars[i] : new CharacterSlot();
        _creating = false;
        _confirmDelete = false;
        _status = "";
        _statusColor = Color.White;
    }

    private (int w, int h) Size()
    {
        var g = GameMain.Instance?.Graphics;
        if (g == null) return (1000, 720);
        return (g.PreferredBackBufferWidth, g.PreferredBackBufferHeight);
    }

    private void RebuildLayout()
    {
        var (w, h) = Size();
        int slotW = 200;
        int slotH = 120;
        int startX = (w - slotW * 5 - 80) / 2;
        int startY = 200;

        for (int i = 0; i < 5; i++)
            _slots[i].Rect = new Rectangle(startX + i * (slotW + 20), startY, slotW, slotH);

        int btnY = startY + slotH + 40;
        int centerX = w / 2;

        _btnEnter = new Rectangle(centerX - 200, btnY, 120, 36);
        _btnCreate = new Rectangle(centerX - 60, btnY, 120, 36);
        _btnDelete = new Rectangle(centerX + 80, btnY, 120, 36);
        _btnBack = new Rectangle(centerX - 60, btnY + 50, 120, 36);

        _nameField = new Rectangle(centerX - 120, 280, 240, 32);
        _btnLeftClass = new Rectangle(centerX - 160, 330, 36, 36);
        _btnRightClass = new Rectangle(centerX + 124, 330, 36, 36);
        _btnCreateConfirm = new Rectangle(centerX - 120, 390, 110, 36);
        _btnCreateCancel = new Rectangle(centerX + 10, 390, 110, 36);

        _btnDeleteConfirm = new Rectangle(centerX - 120, h / 2 + 50, 110, 36);
        _btnDeleteCancel = new Rectangle(centerX + 10, h / 2 + 50, 110, 36);
    }

    public void Update(GameTime gameTime, KeyboardState kb, MouseState mouse)
    {
        var c = GameMain.Instance?.Client;
        if (c == null) return;
        RebuildLayout();

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

        if (_creating)
        {
            HandleTextInput(kb);

            if (clicked)
            {
                if (_btnLeftClass.Contains(mouse.X, mouse.Y))
                    _createClass = (_createClass + 5) % 6;
                if (_btnRightClass.Contains(mouse.X, mouse.Y))
                    _createClass = (_createClass + 1) % 6;

                if (_btnCreateConfirm.Contains(mouse.X, mouse.Y) && _createName.Length >= 3 && _createName.Length <= 20)
                {
                    c.CreateCharacter(_createName, _createClass);
                    _status = "Создание персонажа...";
                    _statusColor = Color.Yellow;
                }
                if (_btnCreateCancel.Contains(mouse.X, mouse.Y))
                {
                    _creating = false;
                    _createName = "";
                    _status = "Создание отменено";
                    _statusColor = Color.Gray;
                }
            }
        }
        else if (_confirmDelete)
        {
            if (clicked)
            {
                if (_btnDeleteConfirm.Contains(mouse.X, mouse.Y))
                {
                    c.DeleteCharacter(_pendingDeleteName);
                    _confirmDelete = false;
                    _pendingDeleteName = "";
                    _status = "Удаление...";
                    _statusColor = Color.Yellow;
                }
                if (_btnDeleteCancel.Contains(mouse.X, mouse.Y))
                {
                    _confirmDelete = false;
                    _status = "Удаление отменено";
                    _statusColor = Color.Gray;
                }
            }
        }
        else
        {
            if (clicked)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (_slots[i].Rect.Contains(mouse.X, mouse.Y) && _slots[i].HasCharacter)
                        _selected = i;
                }

                if (_btnEnter.Contains(mouse.X, mouse.Y) && _selected >= 0 && _slots[_selected].HasCharacter)
                {
                    c.SelectCharacter(_slots[_selected].Name);
                    _status = $"Вход в мир за {_slots[_selected].Name}...";
                    _statusColor = Color.Yellow;
                }
                else if (_btnCreate.Contains(mouse.X, mouse.Y))
                {
                    _creating = true;
                    _createName = "";
                    _status = "Создание нового персонажа";
                    _statusColor = new Color(100, 180, 255);
                }
                else if (_btnDelete.Contains(mouse.X, mouse.Y) && _selected >= 0 && _slots[_selected].HasCharacter)
                {
                    _confirmDelete = true;
                    _pendingDeleteName = _slots[_selected].Name;
                    _status = $"Удаление персонажа «{_pendingDeleteName}»";
                    _statusColor = new Color(255, 120, 100);
                }
                else if (_btnBack.Contains(mouse.X, mouse.Y))
                {
                    UnsubHooks();
                    GameMain.Instance?.ShowLogin();
                }
                else if (_selected >= 0 && _slots[_selected].HasCharacter)
                {
                    _status = $"Выбран персонаж: {_slots[_selected].Name} (ур. {_slots[_selected].Level})";
                    _statusColor = new Color(180, 220, 180);
                }
            }
        }

        _prevKb = kb;
        _prevMouse = mouse;
    }

    private void HandleTextInput(KeyboardState kb)
    {
        for (int k = (int)Keys.A; k <= (int)Keys.Z; k++)
        {
            if (kb.IsKeyDown((Keys)k) && _prevKb.IsKeyUp((Keys)k))
            {
                char ch = (char)('a' + k - (int)Keys.A);
                if (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift))
                    ch = char.ToUpper(ch);
                _createName += ch;
            }
        }
        for (int k = (int)Keys.D0; k <= (int)Keys.D9; k++)
        {
            if (kb.IsKeyDown((Keys)k) && _prevKb.IsKeyUp((Keys)k))
                _createName += (char)('0' + k - (int)Keys.D0);
        }
        if (kb.IsKeyDown(Keys.Back) && _prevKb.IsKeyUp(Keys.Back) && _createName.Length > 0)
            _createName = _createName[..^1];
    }

    public void Draw(GameTime gameTime, SpriteBatch sb)
    {
        sb.Begin();
        var font = SpriteCache.Font;
        if (font == null) { sb.End(); return; }

        var (w, h) = Size();
        int centerX = w / 2;

        var title = "Выбор персонажа";
        var ts = font.MeasureString(title);
        sb.DrawString(font, title, new Vector2(centerX - ts.X / 2, 50), Color.Gold);

        var sub = "Выберите персонажа или создайте нового";
        var ss = font.MeasureString(sub);
        sb.DrawString(font, sub, new Vector2(centerX - ss.X / 2, 100), Color.LightGray);

        // Slots
        for (int i = 0; i < 5; i++)
        {
            var slot = _slots[i];
            var r = slot.Rect;
            bool sel = _selected == i;
            bool hover = r.Contains(_prevMouse.X, _prevMouse.Y) && slot.HasCharacter;
            var bg = sel ? new Color(50, 50, 80) : (hover ? new Color(40, 40, 65) : new Color(30, 30, 50));
            var border = sel ? Color.Gold : (hover ? Color.White : new Color(80, 80, 110));
            sb.Draw(SpriteCache.Pixel, r, bg);
            UIHelper.DrawRectOutline(sb, r, border);

            if (slot.HasCharacter)
            {
                float y = r.Y + 10;
                var nSize = font.MeasureString(slot.Name);
                sb.DrawString(font, slot.Name, new Vector2(r.X + (r.Width - nSize.X) / 2, y), Color.White);
                y += 30;
                var lvl = $"Ур. {slot.Level}";
                var lSize = font.MeasureString(lvl);
                sb.DrawString(font, lvl, new Vector2(r.X + (r.Width - lSize.X) / 2, y), Color.LimeGreen);
                y += 24;
                var cls = font.MeasureString(slot.ClassName);
                sb.DrawString(font, slot.ClassName, new Vector2(r.X + (r.Width - cls.X) / 2, y), Color.Cyan);
            }
            else
            {
                string empty = "Пусто";
                var es = font.MeasureString(empty);
                sb.DrawString(font, empty, new Vector2(r.X + (r.Width - es.X) / 2, r.Y + r.Height / 2 - es.Y / 2), Color.DarkGray);
            }
        }

        // Buttons
        DrawButton(sb, font, _btnEnter, "Войти", new Color(0, 180, 100), _prevMouse);
        DrawButton(sb, font, _btnCreate, "Создать", new Color(0, 120, 215), _prevMouse);
        DrawButton(sb, font, _btnDelete, "Удалить", new Color(200, 50, 50), _prevMouse);
        DrawButton(sb, font, _btnBack, "Выход", new Color(100, 100, 120), _prevMouse);

        // Create panel
        if (_creating)
        {
            int py = 260;
            var panelRect = new Rectangle(centerX - 200, py - 10, 400, 200);
            sb.Draw(SpriteCache.Pixel, panelRect, new Color(20, 20, 40));
            UIHelper.DrawRectOutline(sb, panelRect, new Color(80, 80, 130));

            var label = "Имя персонажа:";
            sb.DrawString(font, label, new Vector2(centerX - 120, pc(py) - 10), Color.LightGray);

            sb.Draw(SpriteCache.Pixel, _nameField, new Color(40, 40, 60));
            UIHelper.DrawRectOutline(sb, _nameField, Color.DodgerBlue);
            sb.DrawString(font, _createName, new Vector2(_nameField.X + 5, pc(_nameField.Y) + 1), Color.White);

            var classLabel = "Класс:";
            sb.DrawString(font, classLabel, new Vector2(centerX - 120, pc(340)), Color.LightGray);

            var cn = ClassNames[_createClass];
            var cnSize = font.MeasureString(cn);
            sb.DrawString(font, cn, new Vector2(centerX - cnSize.X / 2, pc(340)), Color.Cyan);

            DrawButton(sb, font, _btnLeftClass, "<", new Color(80, 80, 140), _prevMouse);
            DrawButton(sb, font, _btnRightClass, ">", new Color(80, 80, 140), _prevMouse);

            DrawButton(sb, font, _btnCreateConfirm, "Создать", new Color(0, 180, 100), _prevMouse);
            DrawButton(sb, font, _btnCreateCancel, "Отмена", new Color(150, 50, 50), _prevMouse);
        }

        // Delete confirm modal
        if (_confirmDelete)
        {
            sb.Draw(SpriteCache.Pixel, new Rectangle(0, 0, w, h), new Color(0, 0, 0, 180));

            int dw = 400, dh = 180;
            var dlgRect = new Rectangle(centerX - dw / 2, h / 2 - dh / 2, dw, dh);
            sb.Draw(SpriteCache.Pixel, dlgRect, new Color(35, 18, 18));
            UIHelper.DrawRectOutline(sb, dlgRect, new Color(200, 60, 60));

            var titleTxt = "Удаление персонажа";
            var ts2 = font.MeasureString(titleTxt);
            sb.DrawString(font, titleTxt, new Vector2(centerX - ts2.X / 2, dlgRect.Y + 18), Color.OrangeRed);

            var msg = $"Вы уверены, что хотите удалить персонажа";
            var msg2 = $"«{_pendingDeleteName}»?";
            var ms1 = font.MeasureString(msg);
            var ms2 = font.MeasureString(msg2);
            sb.DrawString(font, msg, new Vector2(centerX - ms1.X / 2, dlgRect.Y + 55), Color.LightGray);
            sb.DrawString(font, msg2, new Vector2(centerX - ms2.X / 2, dlgRect.Y + 80), Color.White);

            var warn = "Это действие нельзя отменить!";
            var ws = font.MeasureString(warn);
            sb.DrawString(font, warn, new Vector2(centerX - ws.X / 2, dlgRect.Y + 108), new Color(255, 120, 100));

            DrawButton(sb, font, _btnDeleteConfirm, "Удалить", new Color(200, 50, 50), _prevMouse);
            DrawButton(sb, font, _btnDeleteCancel, "Отмена", new Color(100, 100, 120), _prevMouse);
        }

        // Status bar
        if (!string.IsNullOrEmpty(_status))
        {
            int sbarH = 40;
            var sbarRect = new Rectangle(0, h - sbarH, w, sbarH);
            sb.Draw(SpriteCache.Pixel, sbarRect, new Color(16, 16, 28, 230));
            UIHelper.DrawRectOutline(sb, sbarRect, new Color(60, 60, 80));
            var stSize = font.MeasureString(_status);
            sb.DrawString(font, _status, new Vector2((w - stSize.X) / 2, h - sbarH + (sbarH - stSize.Y) / 2), _statusColor);
        }

        sb.End();
    }

    private static void DrawButton(SpriteBatch sb, SpriteFont font, Rectangle r, string text, Color color, MouseState mouse)
    {
        bool hover = r.Contains(mouse.X, mouse.Y);
        bool press = hover && mouse.LeftButton == ButtonState.Pressed;

        var c = color;
        if (press)
            c = new Color((int)(c.R * 0.7f), (int)(c.G * 0.7f), (int)(c.B * 0.7f), c.A);
        else if (hover)
            c = new Color(Math.Min(255, c.R + 40), Math.Min(255, c.G + 40), Math.Min(255, c.B + 40), c.A);

        var dr = press ? new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2) : r;
        sb.Draw(SpriteCache.Pixel, dr, c);
        UIHelper.DrawRectOutline(sb, dr, hover ? Color.White : new Color(180, 180, 200));
        var size = font.MeasureString(text);
        sb.DrawString(font, text, new Vector2(dr.X + (dr.Width - size.X) / 2, pc(dr.Y) + (dr.Height - size.Y) / 2), Color.White);
    }

    private static float pc(int y) => y;

    public void Dispose()
    {
        UnsubHooks();
    }

    private void UnsubHooks()
    {
        var c = GameMain.Instance?.Client;
        if (c != null)
        {
            c.CharacterListUpdated -= OnCharacterList;
            c.SystemMessage -= OnSystemMessage;
        }
    }
}

public class CharacterSlot
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string ClassName { get; set; } = "";
    public string Zone { get; set; } = "";
    public Rectangle Rect { get; set; }
    public bool HasCharacter => !string.IsNullOrEmpty(Name);
}
