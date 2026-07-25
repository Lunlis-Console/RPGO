using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;
using System.Text;

namespace RPGGame.ClientMonoGame.Windows;

public class MailWindow : GameWindow
{
    public event Action? InboxRequested;
    public event Action? OutboxRequested;
    public event Action<int>? ReadRequested;
    public event Action<int>? TakeAttachmentRequested;
    public event Action<int>? DeleteRequested;
    public event Action<string, string, string, int, string, int>? SendRequested;
    public event Action? InventoryRequested;

    private int _selectedTab;
    private List<MailEntry> _inbox = new();
    private List<MailEntry> _outbox = new();
    private int _selectedMailId = -1;
    private MailEntry? _selectedMail;
    private int _scrollOffset;
    private int _listVisibleRows = 12;
    private int _lastRequestedFolder = 0;

    private string _composeRecipient = "";
    private string _composeSubject = "";
    private string _composeBody = "";
    private int _composeGold;
    private string _composeItemId = "";
    private string _composeItemName = "";
    private int _composeItemQty;
    private int _activeField = -1;
    private StringBuilder _fieldBuffer = new();

    private bool _showItemPicker;
    private List<(string Id, string Name, int Qty)> _inventory = new();
    private int _itemPickerScroll;

    public bool IsInputActive => _activeField >= 0;

    private new MouseState _prevMouse;
    private int _prevScroll;
    private HashSet<uint> _prevDownVks = new();

    private static readonly Color CTabActive = new(60, 90, 140);
    private static readonly Color CTabInactive = new(40, 44, 56);
    private static readonly Color CFieldBg = new(35, 38, 48);
    private static readonly Color CFieldActive = new(50, 55, 70);
    private static readonly Color CFieldBorder = new(60, 65, 80);
    private static readonly Color CBtnSend = new(50, 120, 70);
    private static readonly Color CBtnSendHover = new(70, 150, 90);
    private static readonly Color CBtnDelete = new(130, 50, 50);
    private static readonly Color CBtnDeleteHover = new(170, 70, 70);
    private static readonly Color CBtnAction = new(60, 80, 120);
    private static readonly Color CBtnActionHover = new(80, 100, 150);
    private static readonly Color CUnread = new(220, 200, 100);
    private static readonly Color CLight = new(200, 200, 210);
    private static readonly Color CGold = new(220, 200, 120);

    private const int TabH = 26;
    private const int RowH = 24;
    private const int FieldH = 22;
    private const int BtnH = 24;

    public MailWindow()
    {
        Title = "Почта";
        Width = 520;
        Height = 560;
        Visible = false;
    }

    public void Open()
    {
        var g = GameMain.Instance!.Graphics;
        X = (g.PreferredBackBufferWidth - Width) / 2;
        Y = (g.PreferredBackBufferHeight - Height) / 2;
        Visible = true;
        _selectedTab = 0;
        _selectedMailId = -1;
        _selectedMail = null;
        _activeField = -1;
        _showItemPicker = false;
        InboxRequested?.Invoke();
    }

    public void SetInbox(List<MailEntry> mails) { _inbox = mails; if (_lastRequestedFolder == 0) _selectedTab = 0; }
    public void SetOutbox(List<MailEntry> mails) { _outbox = mails; if (_lastRequestedFolder == 1) _selectedTab = 1; }

    public void SetInventory(List<(string Id, string Name, int Qty)> items) { _inventory = items; _showItemPicker = true; }

    public void SelectMail(int id)
    {
        _selectedMailId = id;
        _selectedMail = _selectedTab == 0
            ? _inbox.FirstOrDefault(m => m.Id == id)
            : _outbox.FirstOrDefault(m => m.Id == id);
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) { _prevMouse = mouse; _prevScroll = mouse.ScrollWheelValue; return; }

        if (_activeField >= 0)
        {
            HandleFieldInputVkb();
            if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            {
                var r = GetFieldRect(_activeField);
                if (!r.Contains(mouse.X, mouse.Y))
                {
                    CommitField();
                    _activeField = -1;
                }
            }
            _prevMouse = mouse;
            _prevScroll = mouse.ScrollWheelValue;
            return;
        }

        bool justClicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

        if (justClicked)
        {
            for (int t = 0; t < 3; t++)
            {
                var tr = GetTabRect(t);
                if (tr.Contains(mouse.X, mouse.Y))
                {
                    _selectedTab = t;
                    _selectedMailId = -1;
                    _selectedMail = null;
                    _scrollOffset = 0;
                    _lastRequestedFolder = t;
                    if (t == 0) InboxRequested?.Invoke();
                    else if (t == 1) OutboxRequested?.Invoke();
                    _prevMouse = mouse;
                    return;
                }
            }

            if (_selectedMail != null)
            {
                var takeBtn = new Rectangle(ContentX, ContentY + TabH + 260, 150, BtnH);
                if (takeBtn.Contains(mouse.X, mouse.Y) && _selectedMail.GoldAmount > 0 && _selectedMail.TakenAt == "" && _selectedTab == 0)
                    TakeAttachmentRequested?.Invoke(_selectedMailId);

                var replyBtn = new Rectangle(ContentX + 160, ContentY + TabH + 260, 100, BtnH);
                if (replyBtn.Contains(mouse.X, mouse.Y))
                {
                    _selectedTab = 2;
                    _composeRecipient = _selectedMail.SenderName;
                    _composeSubject = $"RE: {_selectedMail.Subject}";
                }

                var delBtn = new Rectangle(ContentX + ContentW - 100, ContentY + TabH + 260, 100, BtnH);
                if (delBtn.Contains(mouse.X, mouse.Y))
                    DeleteRequested?.Invoke(_selectedMailId);
            }

            if (_selectedTab == 2)
            {
                if (_showItemPicker)
                {
                    var pickerRect = new Rectangle(ContentX, ContentY + TabH + 240, ContentW, 150);
                    if (pickerRect.Contains(mouse.X, mouse.Y))
                    {
                        int pickerStartY = ContentY + TabH + 240 + 24;
                        for (int i = 0; i < _inventory.Count && i < 6; i++)
                        {
                            int idx = i + _itemPickerScroll;
                            if (idx >= _inventory.Count) break;
                            var row = new Rectangle(ContentX + 4, pickerStartY + i * RowH, ContentW - 8, RowH);
                            if (row.Contains(mouse.X, mouse.Y))
                            {
                                var item = _inventory[idx];
                                _composeItemId = item.Id;
                                _composeItemName = item.Name;
                                _composeItemQty = 1;
                                _showItemPicker = false;
                                break;
                            }
                        }
                        _prevMouse = mouse;
                        return;
                    }
                    else
                    {
                        _showItemPicker = false;
                    }
                }

                for (int f = 0; f < 4; f++)
                {
                    var fr = GetFieldRect(f);
                    if (fr.Contains(mouse.X, mouse.Y))
                    {
                        _activeField = f;
                        _fieldBuffer.Clear();
                        _fieldBuffer.Append(f switch { 0 => _composeRecipient, 1 => _composeSubject, 2 => _composeBody, 3 => _composeGold > 0 ? _composeGold.ToString() : "", _ => "" });
                        _prevMouse = mouse;
                        _prevDownVks.Clear();
                        return;
                    }
                }

                var addItemBtn = new Rectangle(ContentX, ContentY + TabH + 200, ContentW, BtnH);
                if (addItemBtn.Contains(mouse.X, mouse.Y))
                {
                    InventoryRequested?.Invoke();
                    _prevMouse = mouse;
                    return;
                }

                if (!string.IsNullOrEmpty(_composeItemId))
                {
                    var removeItemBtn = new Rectangle(ContentX + ContentW - 120, ContentY + TabH + 220, 120, BtnH);
                    if (removeItemBtn.Contains(mouse.X, mouse.Y))
                    {
                        _composeItemId = "";
                        _composeItemName = "";
                        _composeItemQty = 0;
                    }
                }

                var sendBtn = new Rectangle(ContentX, ContentY + TabH + 395, 120, BtnH);
                if (sendBtn.Contains(mouse.X, mouse.Y))
                {
                    SendRequested?.Invoke(_composeRecipient, _composeSubject, _composeBody, _composeGold, _composeItemId, _composeItemQty);
                    _selectedTab = 0;
                    _composeRecipient = ""; _composeSubject = ""; _composeBody = ""; _composeGold = 0; _composeItemId = ""; _composeItemQty = 0;
                    InboxRequested?.Invoke();
                }
            }

            if (_selectedTab < 2 && _selectedMail == null)
            {
                var list = _selectedTab == 0 ? _inbox : _outbox;
                for (int r = 0; r < _listVisibleRows; r++)
                {
                    int idx = r + _scrollOffset;
                    if (idx >= list.Count) break;
                    var rowRect = new Rectangle(ContentX, ContentY + TabH + 4 + r * RowH, ContentW, RowH);
                    if (rowRect.Contains(mouse.X, mouse.Y))
                    {
                        _selectedMailId = list[idx].Id;
                        _selectedMail = list[idx];
                        ReadRequested?.Invoke(list[idx].Id);
                        break;
                    }
                }
            }

            if (_selectedMail != null)
            {
                var backBtn = new Rectangle(ContentX + ContentW - 80, ContentY + TabH + 260 + BtnH + 8, 80, BtnH);
                if (backBtn.Contains(mouse.X, mouse.Y))
                {
                    _selectedMail = null;
                    _selectedMailId = -1;
                }
            }
        }

        int delta = mouse.ScrollWheelValue - _prevScroll;
        if (delta != 0 && _selectedMail == null && _selectedTab < 2)
        {
            var list = _selectedTab == 0 ? _inbox : _outbox;
            int maxScroll = Math.Max(0, list.Count - _listVisibleRows);
            if (delta < 0) _scrollOffset = Math.Min(maxScroll, _scrollOffset + 2);
            else _scrollOffset = Math.Max(0, _scrollOffset - 2);
        }

        _prevMouse = mouse;
        _prevScroll = mouse.ScrollWheelValue;
        base.Update(gameTime, keyboard, mouse);
    }

    private void HandleFieldInputVkb()
    {
        bool russian = KeyboardLayoutHelper.IsRussianForeground();
        bool shiftDown = KeyboardLayoutHelper.IsShiftDown();
        var nowDown = new HashSet<uint>(KeyboardLayoutHelper.GetPressedVks());

        foreach (var vk in nowDown)
        {
            if (_prevDownVks.Contains(vk)) continue;

            if (vk == 0x08)
            {
                if (_fieldBuffer.Length > 0) _fieldBuffer.Remove(_fieldBuffer.Length - 1, 1);
            }
            else if (vk == 0x0D)
            {
                CommitField();
                _activeField = -1;
                _prevDownVks = nowDown;
                return;
            }
            else if (vk == 0x1B)
            {
                _activeField = -1;
                _prevDownVks = nowDown;
                return;
            }
            else if (vk == 0x20)
            {
                if (_activeField < 3 && _fieldBuffer.Length < 200)
                    _fieldBuffer.Append(' ');
            }
            else if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14 || vk == 0x09)
            {
                continue;
            }
            else if (_activeField == 3)
            {
                if (KeyCharMap.TryGetCharByVk(vk, false, shiftDown, out char digit) && digit >= '0' && digit <= '9' && _fieldBuffer.Length < 8)
                    _fieldBuffer.Append(digit);
            }
            else
            {
                int maxLen = _activeField == 2 ? 200 : 50;
                if (_fieldBuffer.Length < maxLen && KeyCharMap.TryGetCharByVk(vk, russian, shiftDown, out char ch))
                    _fieldBuffer.Append(ch);
            }
        }
        _prevDownVks = nowDown;
    }

    private void CommitField()
    {
        string val = _fieldBuffer.ToString();
        switch (_activeField)
        {
            case 0: _composeRecipient = val; break;
            case 1: _composeSubject = val; break;
            case 2: _composeBody = val; break;
            case 3: int.TryParse(val, out int g); _composeGold = Math.Max(0, g); break;
        }
    }

    private Rectangle GetTabRect(int index)
    {
        int tabW = ContentW / 3;
        return new Rectangle(ContentX + index * tabW, ContentY, tabW, TabH);
    }

    private Rectangle GetFieldRect(int fieldIndex)
    {
        int y = ContentY + TabH + 4 + fieldIndex * (FieldH + 6);
        return new Rectangle(ContentX + 80, y, ContentW - 80, FieldH);
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        var hoverMouse = Mouse.GetState();

        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, Height), new Color(30, 32, 40));
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, TitleH), new Color(45, 55, 75));
        UIHelper.DrawRectOutline(sb, new Rectangle(X, Y, Width, Height), new Color(80, 90, 110));
        var tSize = font.MeasureString(Title);
        sb.DrawString(font, Title, new Vector2(X + 8, Y + (TitleH - tSize.Y) / 2), Color.White);

        string[] tabs = { "Входящие", "Исходящие", "Написать" };
        for (int t = 0; t < 3; t++)
        {
            var tr = GetTabRect(t);
            sb.Draw(SpriteCache.Pixel, tr, t == _selectedTab ? CTabActive : CTabInactive);
            UIHelper.DrawRectOutline(sb, tr, CFieldBorder);
            var ts = font.MeasureString(tabs[t]);
            sb.DrawString(font, tabs[t], new Vector2(tr.X + (tr.Width - ts.X) / 2, tr.Y + (TabH - ts.Y) / 2), Color.White);
        }

        if (_selectedTab < 2 && _selectedMail == null)
            DrawMailList(sb, font, hoverMouse);
        else if (_selectedMail != null)
            DrawMailDetail(sb, font, hoverMouse);
        else if (_selectedTab == 2)
            DrawCompose(sb, font, hoverMouse);
    }

    private void DrawMailList(SpriteBatch sb, SpriteFont font, MouseState hMouse)
    {
        var list = _selectedTab == 0 ? _inbox : _outbox;
        int startY = ContentY + TabH + 4;
        int maxScroll = Math.Max(0, list.Count - _listVisibleRows);

        if (list.Count == 0)
        {
            DrawText(sb, "Нет писем.", ContentX + 10, startY + 20, new Color(120, 120, 130), font);
            return;
        }

        for (int r = 0; r < _listVisibleRows; r++)
        {
            int idx = r + _scrollOffset;
            if (idx >= list.Count) break;
            var m = list[idx];
            var rowRect = new Rectangle(ContentX, startY + r * RowH, ContentW, RowH);
            bool hover = rowRect.Contains(hMouse.X, hMouse.Y);
            bool unread = string.IsNullOrEmpty(m.ReadAt);

            sb.Draw(SpriteCache.Pixel, rowRect, hover ? new Color(50, 55, 70) : new Color(35, 38, 48));

            string who = _selectedTab == 0 ? $"От: {m.SenderName}" : $"Кому: {m.RecipientName}";
            string subject = string.IsNullOrEmpty(m.Subject) ? "(без темы)" : m.Subject;
            bool hasAttach = (m.GoldAmount > 0 || (!string.IsNullOrEmpty(m.ItemId) && m.ItemQuantity > 0));

            DrawText(sb, who, ContentX + 4, startY + r * RowH + 4, unread ? CUnread : CLight, font);
            DrawText(sb, subject, ContentX + 150, startY + r * RowH + 4, unread ? Color.White : CLight, font);

            if (hasAttach)
                DrawText(sb, "+", ContentX + ContentW - 30, startY + r * RowH + 4, CGold, font);

            sb.Draw(SpriteCache.Pixel, new Rectangle(ContentX + 148, startY + r * RowH + 2, 1, RowH - 4), new Color(60, 65, 80));
        }

        if (maxScroll > 0)
        {
            string scrollInfo = $"{_scrollOffset + 1}-{Math.Min(_scrollOffset + _listVisibleRows, list.Count)} / {list.Count}";
            DrawText(sb, scrollInfo, ContentX + ContentW - 100, ContentY + TabH + 4 + _listVisibleRows * RowH + 4, new Color(100, 100, 110), font);
        }
    }

    private void DrawMailDetail(SpriteBatch sb, SpriteFont font, MouseState hMouse)
    {
        var m = _selectedMail!;
        int y = ContentY + TabH + 8;
        int lx = ContentX;

        string header = _selectedTab == 0 ? $"От: {m.SenderName}" : $"Кому: {m.RecipientName}";
        DrawText(sb, header, lx, y, CLight, font); y += 18;
        DrawText(sb, $"Тема: {m.Subject}", lx, y, Color.White, font); y += 18;
        DrawText(sb, $"Дата: {FormatDate(m.SentAt)}", lx, y, new Color(130, 130, 140), font); y += 22;

        sb.Draw(SpriteCache.Pixel, new Rectangle(lx, y, ContentW, 120), CFieldBg);
        UIHelper.DrawRectOutline(sb, new Rectangle(lx, y, ContentW, 120), CFieldBorder);
        DrawText(sb, m.Body ?? "", lx + 6, y + 6, CLight, font);
        y += 130;

        if (m.GoldAmount > 0 || (!string.IsNullOrEmpty(m.ItemId) && m.ItemQuantity > 0))
        {
            DrawText(sb, "Вложение:", lx, y, CGold, font); y += 18;
            if (m.GoldAmount > 0) { DrawText(sb, $"  Золото: {m.GoldAmount}", lx, y, CGold, font); y += 16; }
            if (!string.IsNullOrEmpty(m.ItemId) && m.ItemQuantity > 0)
            { DrawText(sb, $"  {m.ItemName} x{m.ItemQuantity}", lx, y, CLight, font); y += 18; }
        }

        y += 8;

        if (_selectedTab == 0 && (m.GoldAmount > 0 || (!string.IsNullOrEmpty(m.ItemId) && m.ItemQuantity > 0)) && m.TakenAt == "")
        {
            var takeBtn = new Rectangle(lx, y, 150, BtnH);
            bool hov = takeBtn.Contains(hMouse.X, hMouse.Y);
            sb.Draw(SpriteCache.Pixel, takeBtn, hov ? CBtnActionHover : CBtnAction);
            var ts = font.MeasureString("Забрать вложение");
            sb.DrawString(font, "Забрать вложение", new Vector2(takeBtn.X + (takeBtn.Width - ts.X) / 2, takeBtn.Y + (BtnH - ts.Y) / 2), Color.White);

            var replyBtn = new Rectangle(lx + 160, y, 100, BtnH);
            hov = replyBtn.Contains(hMouse.X, hMouse.Y);
            sb.Draw(SpriteCache.Pixel, replyBtn, hov ? CBtnSendHover : CBtnSend);
            ts = font.MeasureString("Ответить");
            sb.DrawString(font, "Ответить", new Vector2(replyBtn.X + (replyBtn.Width - ts.X) / 2, replyBtn.Y + (BtnH - ts.Y) / 2), Color.White);
        }
        else if (_selectedTab == 0)
        {
            var replyBtn = new Rectangle(lx, y, 100, BtnH);
            bool hov = replyBtn.Contains(hMouse.X, hMouse.Y);
            sb.Draw(SpriteCache.Pixel, replyBtn, hov ? CBtnSendHover : CBtnSend);
            var ts = font.MeasureString("Ответить");
            sb.DrawString(font, "Ответить", new Vector2(replyBtn.X + (replyBtn.Width - ts.X) / 2, replyBtn.Y + (BtnH - ts.Y) / 2), Color.White);
        }

        var delBtn = new Rectangle(ContentX + ContentW - 100, y, 100, BtnH);
        bool dHov = delBtn.Contains(hMouse.X, hMouse.Y);
        sb.Draw(SpriteCache.Pixel, delBtn, dHov ? CBtnDeleteHover : CBtnDelete);
        var dts = font.MeasureString("Удалить");
        sb.DrawString(font, "Удалить", new Vector2(delBtn.X + (delBtn.Width - dts.X) / 2, delBtn.Y + (BtnH - dts.Y) / 2), Color.White);

        var backBtn = new Rectangle(ContentX + ContentW - 80, y + BtnH + 8, 80, BtnH);
        bool bHov = backBtn.Contains(hMouse.X, hMouse.Y);
        sb.Draw(SpriteCache.Pixel, backBtn, bHov ? new Color(80, 80, 100) : new Color(50, 50, 65));
        var bts = font.MeasureString("Назад");
        sb.DrawString(font, "Назад", new Vector2(backBtn.X + (backBtn.Width - bts.X) / 2, backBtn.Y + (BtnH - bts.Y) / 2), Color.White);
    }

    private void DrawCompose(SpriteBatch sb, SpriteFont font, MouseState hMouse)
    {
        int y = ContentY + TabH + 8;
        int lx = ContentX;
        string[] labels = { "Кому:", "Тема:", "Текст:", "Золото:" };

        for (int f = 0; f < 4; f++)
        {
            DrawText(sb, labels[f], lx, y + 3, CLight, font);
            var fr = GetFieldRect(f);
            bool active = f == _activeField;
            sb.Draw(SpriteCache.Pixel, fr, active ? CFieldActive : CFieldBg);
            UIHelper.DrawRectOutline(sb, fr, active ? Color.Gold : CFieldBorder);

            string display = f switch
            {
                0 => _composeRecipient,
                1 => _composeSubject,
                2 => _composeBody,
                3 => _composeGold > 0 ? _composeGold.ToString() : "",
                _ => ""
            };
            if (active)
            {
                display = _fieldBuffer.ToString();
                if ((Environment.TickCount / 500) % 2 == 0) display += "|";
            }
            DrawText(sb, display, fr.X + 4, fr.Y + 3, Color.White, font);
            y += FieldH + 6;
        }

        int itemY = ContentY + TabH + 200;
        var addBtn = new Rectangle(lx, itemY, ContentW, BtnH);
        bool addHov = addBtn.Contains(hMouse.X, hMouse.Y);
        sb.Draw(SpriteCache.Pixel, addBtn, addHov ? CBtnActionHover : CBtnAction);
        string addLabel = string.IsNullOrEmpty(_composeItemId) ? "+ Вложить предмет" : "Заменить предмет";
        var addTs = font.MeasureString(addLabel);
        sb.DrawString(font, addLabel, new Vector2(addBtn.X + (addBtn.Width - addTs.X) / 2, addBtn.Y + (BtnH - addTs.Y) / 2), Color.White);

        if (!string.IsNullOrEmpty(_composeItemId))
        {
            DrawText(sb, $"Предмет: {_composeItemName}", lx, itemY + BtnH + 4, CLight, font);
            var rmBtn = new Rectangle(lx + ContentW - 120, itemY + BtnH + 2, 120, BtnH - 4);
            bool rmHov = rmBtn.Contains(hMouse.X, hMouse.Y);
            sb.Draw(SpriteCache.Pixel, rmBtn, rmHov ? CBtnDeleteHover : CBtnDelete);
            var rmTs = font.MeasureString("Убрать");
            sb.DrawString(font, "Убрать", new Vector2(rmBtn.X + (rmBtn.Width - rmTs.X) / 2, rmBtn.Y + ((BtnH - 4) - rmTs.Y) / 2), Color.White);
        }

        if (_showItemPicker)
        {
            DrawItemPicker(sb, font, hMouse);
        }

        var sendBtn = new Rectangle(lx, ContentY + TabH + 395, 120, BtnH);
        bool sHov = sendBtn.Contains(hMouse.X, hMouse.Y);
        sb.Draw(SpriteCache.Pixel, sendBtn, sHov ? CBtnSendHover : CBtnSend);
        var sTs = font.MeasureString("Отправить");
        sb.DrawString(font, "Отправить", new Vector2(sendBtn.X + (sendBtn.Width - sTs.X) / 2, sendBtn.Y + (BtnH - sTs.Y) / 2), Color.White);
    }

    private void DrawItemPicker(SpriteBatch sb, SpriteFont font, MouseState hMouse)
    {
        int pickerY = ContentY + TabH + 240;
        int pickerH = 150;
        sb.Draw(SpriteCache.Pixel, new Rectangle(ContentX, pickerY, ContentW, pickerH), new Color(25, 28, 36));
        UIHelper.DrawRectOutline(sb, new Rectangle(ContentX, pickerY, ContentW, pickerH), CFieldBorder);
        DrawText(sb, "Выберите предмет:", ContentX + 8, pickerY + 4, CGold, font);

        int startY = pickerY + 24;
        int maxPickScroll = Math.Max(0, _inventory.Count - 5);
        for (int i = 0; i < 5 && i < _inventory.Count; i++)
        {
            int idx = i + _itemPickerScroll;
            if (idx >= _inventory.Count) break;
            var item = _inventory[idx];
            var row = new Rectangle(ContentX + 4, startY + i * RowH, ContentW - 8, RowH);
            bool hov = row.Contains(hMouse.X, hMouse.Y);
            sb.Draw(SpriteCache.Pixel, row, hov ? new Color(50, 55, 70) : new Color(35, 38, 48));
            DrawText(sb, $"{item.Name} x{item.Qty}", ContentX + 12, startY + i * RowH + 4, CLight, font);
        }

        if (_inventory.Count == 0)
            DrawText(sb, "Инвентарь пуст.", ContentX + 12, startY + 8, new Color(120, 120, 130), font);
    }

    private static string FormatDate(string iso)
    {
        if (DateTime.TryParse(iso, out var dt))
            return dt.ToString("dd.MM.yyyy HH:mm");
        return iso;
    }
}

public class MailEntry
{
    public int Id { get; set; }
    public string SenderName { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public int GoldAmount { get; set; }
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string ItemType { get; set; } = "";
    public int ItemQuantity { get; set; }
    public string SentAt { get; set; } = "";
    public string ReadAt { get; set; } = "";
    public string TakenAt { get; set; } = "";
}
