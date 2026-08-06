using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.ClientMonoGame.Networking;
using LostAndDivine.Shared.Models;
using System.Text;

namespace LostAndDivine.ClientMonoGame.Windows;

public class MailWindow : GameWindow
{
    public event Action? InboxRequested;
    public event Action? OutboxRequested;
    public event Action<int>? ReadRequested;
    public event Action<int>? TakeAttachmentRequested;
    public event Action<int>? DeleteRequested;
    public event Action<string, string, string, int, List<MailAttachment>>? SendRequested;
    public event Action? AttachmentRequested;

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
    private List<MailAttachment> _composeAttachments = new();
    private int _activeField = -1;
    private StringBuilder _fieldBuffer = new();

    private int _attScrollOffset;
    private const int AttRowH = 20;
    private const int AttVisibleRows = 5;
    private int _bodyScroll;

    private const int BodyFieldH = 80;

    public bool IsInputActive => _activeField >= 0;
    public int SelectedMailId => _selectedMailId;
    public List<MailAttachment> ComposeAttachments => _composeAttachments;

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
    private const int CounterH = 13;

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
        _composeAttachments = new();
        _attScrollOffset = 0;
        _bodyScroll = 0;
        InboxRequested?.Invoke();
    }

    public void SetInbox(List<MailEntry> mails) { _inbox = mails; if (_lastRequestedFolder == 0) _selectedTab = 0; }
    public void SetOutbox(List<MailEntry> mails) { _outbox = mails; if (_lastRequestedFolder == 1) _selectedTab = 1; }

    public void RefreshInboxIfOpen()
    {
        if (Visible && _selectedTab == 0 && _selectedMail == null)
            InboxRequested?.Invoke();
    }

    public void UpdateMail(MailEntry mail)
    {
        for (int i = 0; i < _inbox.Count; i++)
            if (_inbox[i].Id == mail.Id) { _inbox[i] = mail; break; }
        for (int i = 0; i < _outbox.Count; i++)
            if (_outbox[i].Id == mail.Id) { _outbox[i] = mail; break; }
        if (_selectedMailId == mail.Id)
            _selectedMail = mail;
    }

    public void SetComposeAttachments(List<MailAttachment> attachments)
    {
        _composeAttachments = (attachments ?? new List<MailAttachment>())
            .GroupBy(a => a.TemplateId)
            .Select(g =>
            {
                var first = g.First();
                return new MailAttachment
                {
                    TemplateId = first.TemplateId, Name = first.Name, Type = first.Type,
                    Quantity = g.Sum(x => x.Quantity),
                    WeaponSubtype = first.WeaponSubtype, HealAmount = first.HealAmount, RestoreMana = first.RestoreMana
                };
            })
            .Where(a => a.Quantity > 0)
            .ToList();
        _attScrollOffset = 0;
    }

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

            int scrollDelta = mouse.ScrollWheelValue - _prevScroll;
            if (scrollDelta != 0 && _activeField == 2)
            {
                var font = SpriteCache.FontSmall ?? SpriteCache.Font;
                if (font != null)
                {
                    var wrapped = UIHelper.WrapText(font, _fieldBuffer.ToString(), GetFieldRect(2).Width - 8);
                    int visibleLines = BodyFieldH / 18;
                    int maxScroll = Math.Max(0, wrapped.Count - visibleLines);
                    if (scrollDelta < 0) _bodyScroll = Math.Min(maxScroll, _bodyScroll + 1);
                    else _bodyScroll = Math.Max(0, _bodyScroll - 1);
                }
            }

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
                int btnY = GetDetailButtonY();

                bool hasTakeable = (_selectedMail.GoldAmount > 0 || _selectedMail.Attachments.Count > 0) && _selectedMail.TakenAt == "" && _selectedTab == 0;

                    var takeBtn = new Rectangle(ContentX, btnY, 150, BtnH);
                    if (takeBtn.Contains(mouse.X, mouse.Y) && hasTakeable)
                    {
                        TakeAttachmentRequested?.Invoke(_selectedMailId);
                        _prevMouse = mouse;
                        return;
                    }

                    if (_selectedTab < 2)
                    {
                        int replyX = hasTakeable ? ContentX + 160 : ContentX;
                        var replyBtn = new Rectangle(replyX, btnY, 100, BtnH);
                        if (replyBtn.Contains(mouse.X, mouse.Y))
                        {
                            _selectedTab = 2;
                            _composeRecipient = _selectedMail.SenderName;
                            _composeSubject = $"RE: {_selectedMail.Subject}";
                            if (_composeSubject.Length > 48) _composeSubject = _composeSubject.Substring(0, 48);
                            _composeBody = "";
                            _composeGold = 0;
                            _composeAttachments = new();
                            _attScrollOffset = 0;
                            _selectedMail = null;
                            _selectedMailId = -1;
                            _prevMouse = mouse;
                            return;
                        }

                        var delBtn = new Rectangle(ContentX + ContentW - 100, btnY, 100, BtnH);
                        if (delBtn.Contains(mouse.X, mouse.Y))
                        {
                            DeleteRequested?.Invoke(_selectedMailId);
                            _selectedMail = null;
                            _selectedMailId = -1;
                            InboxRequested?.Invoke();
                            _prevMouse = mouse;
                            return;
                        }
                    }
            }

            if (_selectedTab == 2)
            {
                var layout = GetComposeLayout();

                for (int f = 0; f < 4; f++)
                {
                    var fr = GetFieldRect(f);
                    if (fr.Contains(mouse.X, mouse.Y))
                    {
                        _activeField = f;
                        _fieldBuffer.Clear();
                        _fieldBuffer.Append(f switch { 0 => _composeRecipient, 1 => _composeSubject, 2 => _composeBody, 3 => _composeGold > 0 ? _composeGold.ToString() : "", _ => "" });
                        _bodyScroll = 0;
                        _prevMouse = mouse;
                        _prevDownVks.Clear();
                        return;
                    }
                }

                // Удаление вложений из письма
                for (int i = 0; i < _composeAttachments.Count; i++)
                {
                    if (i < _attScrollOffset) continue;
                    int row = i - _attScrollOffset;
                    if (row >= AttVisibleRows) break;
                    var rmBtn = new Rectangle(ContentX + ContentW - 60, layout.AttStartY + 18 + row * AttRowH, 56, AttRowH - 2);
                    if (rmBtn.Contains(mouse.X, mouse.Y))
                    {
                        _composeAttachments.RemoveAt(i);
                        if (_attScrollOffset > Math.Max(0, _composeAttachments.Count - AttVisibleRows))
                            _attScrollOffset = Math.Max(0, _composeAttachments.Count - AttVisibleRows);
                        _prevMouse = mouse;
                        return;
                    }
                }

                var addItemBtn = new Rectangle(ContentX, layout.ItemBtnY, ContentW, BtnH);
                if (addItemBtn.Contains(mouse.X, mouse.Y))
                {
                    AttachmentRequested?.Invoke();
                    _prevMouse = mouse;
                    return;
                }

                var sendBtn = new Rectangle(ContentX, layout.SendY, 120, BtnH);
                if (sendBtn.Contains(mouse.X, mouse.Y))
                {
                    SendRequested?.Invoke(_composeRecipient, _composeSubject, _composeBody, _composeGold, _composeAttachments);
                    _selectedTab = 0;
                    _composeRecipient = ""; _composeSubject = ""; _composeBody = ""; _composeGold = 0; _composeAttachments = new(); _attScrollOffset = 0;
                    InboxRequested?.Invoke();
                    _prevMouse = mouse;
                    return;
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
                int btnY = GetDetailButtonY();
                var backBtn = new Rectangle(ContentX + ContentW - 80, btnY + BtnH + 8, 80, BtnH);
                if (backBtn.Contains(mouse.X, mouse.Y))
                {
                    _selectedMail = null;
                    _selectedMailId = -1;
                    _prevMouse = mouse;
                    return;
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
        else if (delta != 0 && _selectedTab == 2 && _activeField < 0 && _composeAttachments.Count > 0)
        {
            var layout = GetComposeLayout();
            var attArea = new Rectangle(ContentX, layout.AttStartY, ContentW, AttVisibleRows * AttRowH);
            if (attArea.Contains(mouse.X, mouse.Y))
            {
                int maxScroll = Math.Max(0, _composeAttachments.Count - AttVisibleRows);
                if (delta < 0) _attScrollOffset = Math.Min(maxScroll, _attScrollOffset + 1);
                else _attScrollOffset = Math.Max(0, _attScrollOffset - 1);
            }
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
                if (_activeField == 2 && _fieldBuffer.Length < 200)
                {
                    _fieldBuffer.Append('\n');
                }
                else
                {
                    CommitField();
                    _activeField = -1;
                    _prevDownVks = nowDown;
                    return;
                }
            }
            else if (vk == 0x1B)
            {
                _activeField = -1;
                _prevDownVks = nowDown;
                return;
            }
            else if (vk == 0x20)
            {
                if (_activeField >= 0 && _activeField < 3 && _fieldBuffer.Length < GetFieldMaxLength(_activeField))
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
                if (_fieldBuffer.Length < GetFieldMaxLength(_activeField) && KeyCharMap.TryGetCharByVk(vk, russian, shiftDown, out char ch))
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

    private static int GetFieldMaxLength(int fieldIndex)
        => fieldIndex == 2 ? 200 : fieldIndex == 1 ? 48 : 50;

    private Rectangle GetTabRect(int index)
    {
        int tabW = ContentW / 3;
        return new Rectangle(ContentX + index * tabW, ContentY, tabW, TabH);
    }

    private Rectangle GetFieldRect(int fieldIndex)
    {
        int y;
        if (fieldIndex <= 1)
            y = ContentY + TabH + 4 + fieldIndex * (FieldH + 6);
        else if (fieldIndex == 2)
            y = ContentY + TabH + 4 + 2 * (FieldH + 6) + CounterH;
        else
            y = ContentY + TabH + 4 + 2 * (FieldH + 6) + CounterH + BodyFieldH + 6 + CounterH;
        int h = fieldIndex == 2 ? BodyFieldH : FieldH;
        int w = fieldIndex == 3 ? 140 : ContentW - 80;
        return new Rectangle(ContentX + 80, y, w, h);
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        var hoverMouse = Mouse.GetState();
        base.Draw(sb, hoverMouse);

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
            bool hasAttach = (m.GoldAmount > 0 || m.Attachments.Count > 0);

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
        var bodyLines = UIHelper.WrapText(font, m.Body ?? "", ContentW - 12);
        int bodyY = y + 6;
        for (int i = 0; i < bodyLines.Count; i++)
        {
            if (bodyY + 18 > y + 120) break;
            DrawText(sb, bodyLines[i], lx + 6, bodyY + i * 18, CLight, font);
        }
        y += 130;

        if (m.GoldAmount > 0 || m.Attachments.Count > 0)
        {
            DrawText(sb, "Вложение:", lx, y, CGold, font); y += 18;
            if (m.GoldAmount > 0) { DrawText(sb, $"  Золото: {m.GoldAmount}", lx, y, CGold, font); y += 16; }
            foreach (var att in m.Attachments)
            {
                if (att.Quantity <= 0) continue;
                DrawText(sb, $"  {att.Name} x{att.Quantity}", lx, y, CLight, font); y += 16;
            }
        }

        y += 8;

        if (_selectedTab == 0 && (m.GoldAmount > 0 || m.Attachments.Count > 0) && m.TakenAt == "")
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

            if (f == 2)
            {
                var wrapped = UIHelper.WrapText(font, display, fr.Width - 8);
                int visibleLines = BodyFieldH / 18;
                int maxScroll = Math.Max(0, wrapped.Count - visibleLines);
                _bodyScroll = Math.Clamp(_bodyScroll, 0, maxScroll);
                for (int i = 0; i < visibleLines; i++)
                {
                    int idx = i + _bodyScroll;
                    if (idx >= wrapped.Count) break;
                    DrawText(sb, wrapped[idx], fr.X + 4, fr.Y + 3 + i * 18, Color.White, font);
                }
                if (maxScroll > 0)
                {
                    if (_bodyScroll > 0)
                        DrawText(sb, "▲", fr.X + fr.Width - 16, fr.Y + 2, new Color(120, 120, 130), font);
                    if (_bodyScroll < maxScroll)
                        DrawText(sb, "▼", fr.X + fr.Width - 16, fr.Y + fr.Height - 18, new Color(120, 120, 130), font);
                }
            }
            else
            {
                DrawText(sb, display, fr.X + 4, fr.Y + 3, Color.White, font);
            }

            if (f == 3)
            {
                int myGold = GameMain.Instance!.Client.Inventory?.Gold ?? 0;
                int entered = active && int.TryParse(_fieldBuffer.ToString(), out int b) ? b : _composeGold;
                int remaining = Math.Max(0, myGold - entered);
                string goldInfo = $"/ {remaining}";
                DrawText(sb, goldInfo, fr.Right + 6, fr.Y + 3, new Color(160, 160, 170), font);
            }

            if (f == 1 || f == 2)
            {
                int cur = active ? _fieldBuffer.Length : display.Length;
                int max = GetFieldMaxLength(f);
                string counter = $"{cur}/{max}";
                var cs = font.MeasureString(counter);
                DrawText(sb, counter, fr.Right - (int)cs.X, fr.Bottom + 2, new Color(130, 130, 140), font);
            }

            y += f switch
            {
                1 => FieldH + 6 + CounterH,
                2 => BodyFieldH + 6 + CounterH,
                _ => FieldH + 6
            };
        }

        var layout = GetComposeLayout();

        // Список вложений
        if (_composeAttachments.Count > 0)
        {
            DrawText(sb, "Вложения:", lx, layout.AttStartY, CGold, font);

            int maxScroll = Math.Max(0, _composeAttachments.Count - AttVisibleRows);
            if (_attScrollOffset > maxScroll) _attScrollOffset = maxScroll;

            int rows = Math.Min(AttVisibleRows, _composeAttachments.Count);
            for (int r = 0; r < rows; r++)
            {
                int idx = r + _attScrollOffset;
                if (idx >= _composeAttachments.Count) break;
                var att = _composeAttachments[idx];
                int rowY = layout.AttStartY + 18 + r * AttRowH;

                var rmBtn = new Rectangle(ContentX + ContentW - 60, rowY, 56, AttRowH - 2);
                bool rmHov = rmBtn.Contains(hMouse.X, hMouse.Y);
                sb.Draw(SpriteCache.Pixel, rmBtn, rmHov ? CBtnDeleteHover : CBtnDelete);
                var rmTs = font.MeasureString("Убрать");
                sb.DrawString(font, "Убрать", new Vector2(rmBtn.X + (rmBtn.Width - rmTs.X) / 2, rmBtn.Y + ((AttRowH - 2) - rmTs.Y) / 2), Color.White);

                string label = $"{att.Name} x{att.Quantity}";
                var ls = font.MeasureString(label);
                if (ls.X > ContentW - 72) label = att.Name;
                DrawText(sb, label, lx, rowY + 1, CLight, font);
            }

            if (maxScroll > 0)
            {
                if (_attScrollOffset > 0) DrawText(sb, "▲", ContentX + ContentW - 76, layout.AttStartY + 14, new Color(120, 120, 130), font);
                if (_attScrollOffset < maxScroll) DrawText(sb, "▼", ContentX + ContentW - 76, layout.AttStartY + 18 + (rows - 1) * AttRowH, new Color(120, 120, 130), font);
            }
        }

        var addBtn = new Rectangle(lx, layout.ItemBtnY, ContentW, BtnH);
        bool addHov = addBtn.Contains(hMouse.X, hMouse.Y);
        sb.Draw(SpriteCache.Pixel, addBtn, addHov ? CBtnActionHover : CBtnAction);
        string addLabel = _composeAttachments.Count == 0 ? "+ Вложить предмет" : "+ Добавить предмет";
        var addTs = font.MeasureString(addLabel);
        sb.DrawString(font, addLabel, new Vector2(addBtn.X + (addBtn.Width - addTs.X) / 2, addBtn.Y + (BtnH - addTs.Y) / 2), Color.White);

        var sendBtn = new Rectangle(lx, layout.SendY, 120, BtnH);
        bool sHov = sendBtn.Contains(hMouse.X, hMouse.Y);
        sb.Draw(SpriteCache.Pixel, sendBtn, sHov ? CBtnSendHover : CBtnSend);
        var sTs = font.MeasureString("Отправить");
        sb.DrawString(font, "Отправить", new Vector2(sendBtn.X + (sendBtn.Width - sTs.X) / 2, sendBtn.Y + (BtnH - sTs.Y) / 2), Color.White);
    }

    private struct ComposeLayout
    {
        public int ItemBtnY, AttStartY, SendY;
    }
    private ComposeLayout GetComposeLayout()
    {
        int goldBottom = GetFieldRect(3).Bottom;
        int attStartY = goldBottom + 8;
        int attAreaH = _composeAttachments.Count > 0 ? (18 + Math.Min(AttVisibleRows, _composeAttachments.Count) * AttRowH) : 0;
        int itemBtnY = attStartY + attAreaH + 6;
        int sendY = itemBtnY + BtnH + 8;
        return new ComposeLayout { ItemBtnY = itemBtnY, AttStartY = attStartY, SendY = sendY };
    }

    private int GetDetailButtonY()
    {
        var m = _selectedMail;
        if (m == null) return ContentY + TabH + 260;
        int y = ContentY + TabH + 8;
        y += 18 + 18 + 22;
        y += 130;
        if (m.GoldAmount > 0 || m.Attachments.Count > 0)
        {
            y += 18;
            if (m.GoldAmount > 0) y += 16;
            y += m.Attachments.Count * 16;
        }
        y += 8;
        return y;
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
    public List<MailAttachment> Attachments { get; set; } = new();
    public string SentAt { get; set; } = "";
    public string ReadAt { get; set; } = "";
    public string TakenAt { get; set; } = "";
}

public class MailAttachment
{
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string WeaponSubtype { get; set; } = "";
    public int HealAmount { get; set; }
    public int RestoreMana { get; set; }

    public MailAttachment Clone() => (MailAttachment)MemberwiseClone();
}
