using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Collections.Generic;

namespace RPGGame.ClientMonoGame.Windows;

public class SocialWindow : GameWindow
{
    private enum Tab { Friends, Group, Guild }

    private Tab _activeTab = Tab.Friends;
    private readonly List<FriendInfo> _friends = new();
    private string _resultMessage = "";
    private float _resultTimer;

    // Текущее состояние группы (для вкладки "Пати")
    private PartyInfo? _party;
    private int _selectedMemberIndex = -1;
    private Rectangle _transferBtnRect;
    private Rectangle _kickBtnRect;

    private Rectangle _addFieldRect;
    private Rectangle _addBtnRect;
    private Rectangle _whisperBtnRect;
    private Rectangle _removeBtnRect;
    private Rectangle _groupBtnRect;
    private int _selectedFriendIndex = -1;
    private string _addText = "";
    private bool _addFieldFocused;

    private readonly GameClient _client;

    // Статус группы (для блокировки кнопки "В группу")
    private bool _inParty;
    private bool _isPartyLeader;
    private bool _showLeaderTip;
    private readonly HashSet<string> _partyMemberNames = new(StringComparer.OrdinalIgnoreCase);

    // Собственный prevMouse (НЕ базовый, т.к. base.Update затирает свой)
    private MouseState _prevMouseSocial;

    // Отслеживание нажатых VK для ввода имени
    private HashSet<uint> _prevDownVks = new();

    // Максимальное число видимых строк списка (резервируем место, чтобы
    // поле ввода никогда не перекрывало надпись "Список пуст")
    private const int ListRowH = 26;
    private const int ListVisibleRows = 6;

    public SocialWindow(GameClient client)
    {
        _client = client;
        Title = "Общение";
        Width = 360;
        Height = 460;

        _client.FriendListUpdated += OnFriendList;
        _client.FriendResultReceived += (ok, msg) => { _resultMessage = msg; _resultTimer = 3f; };
        _client.PartyUpdated += OnPartyUpdated;
        _client.PartyDisbanded += OnPartyDisbanded;
    }

    private void OnPartyDisbanded()
    {
        // Группа распущена/покинута: сбрасываем состояние UI, иначе кнопка
        // "В группу" остаётся заблокированной (тултип "только лидер может приглашать"),
        // хотя сервер уже удалил группу и игрок может приглашать снова.
        _inParty = false;
        _isPartyLeader = false;
        _partyMemberNames.Clear();
        _party = null;
        _selectedMemberIndex = -1;
    }

    private void OnFriendList(List<FriendInfo> friends)
    {
        _friends.Clear();
        _friends.AddRange(friends);
        if (_selectedFriendIndex >= _friends.Count) _selectedFriendIndex = -1;
    }

    private void OnPartyUpdated(PartyInfo data)
    {
        _inParty = data.Members.Count > 0;
        _isPartyLeader = data.LeaderId == _client.PlayerId;
        _partyMemberNames.Clear();
        foreach (var m in data.Members)
            _partyMemberNames.Add(m.Name);
        _party = data;
        if (_selectedMemberIndex >= (data.Members?.Count ?? 0))
            _selectedMemberIndex = -1;
    }

    public void Open()
    {
        // Открываем по центру экрана
        var g = GameMain.Instance?.Graphics;
        if (g != null)
        {
            X = Math.Max(0, (g.PreferredBackBufferWidth - Width) / 2);
            Y = Math.Max(0, (g.PreferredBackBufferHeight - Height) / 2);
        }
        Visible = true;
        _client.RequestFriendList();
    }

    public event Action<string>? WhisperRequested;

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        // clicked вычисляем ПО СВОЕМУ предыдущему состоянию (до base.Update!)
        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouseSocial.LeftButton == ButtonState.Released;

        base.Update(gameTime, keyboard, mouse); // drag/close заголовка
        if (!Visible) { _prevMouseSocial = mouse; return; }

        if (_resultTimer > 0) _resultTimer -= (float)gameTime.ElapsedGameTime.TotalSeconds;

        int cx = ContentX;
        int cy = ContentY;

        var friendsTab = new Rectangle(cx, cy, 100, 24);
        var groupTab = new Rectangle(cx + 104, cy, 100, 24);
        var guildTab = new Rectangle(cx + 208, cy, 100, 24);
        if (clicked)
        {
            if (friendsTab.Contains(mouse.X, mouse.Y)) _activeTab = Tab.Friends;
            else if (groupTab.Contains(mouse.X, mouse.Y)) _activeTab = Tab.Group;
            else if (guildTab.Contains(mouse.X, mouse.Y)) _activeTab = Tab.Guild;
        }

        if (_activeTab == Tab.Friends)
        {
            int listY = cy + 32;
            // Резервируем фиксированную высоту под список
            int listBlockH = ListVisibleRows * ListRowH;

            // Строки списка
            for (int i = 0; i < _friends.Count && i < ListVisibleRows; i++)
            {
                var row = new Rectangle(cx, listY + i * ListRowH, ContentW, ListRowH);
                if (clicked && row.Contains(mouse.X, mouse.Y))
                    _selectedFriendIndex = i;
            }

            // Поле ввода — СТРОГО под зарезервированным блоком списка
            int fy = listY + listBlockH + 8;
            _addFieldRect = new Rectangle(cx, fy, ContentW - 90, 24);
            _addBtnRect = new Rectangle(cx + ContentW - 84, fy, 84, 24);

            if (clicked && _addFieldRect.Contains(mouse.X, mouse.Y))
                _addFieldFocused = true;
            else if (clicked && !_addFieldRect.Contains(mouse.X, mouse.Y))
                _addFieldFocused = false;

            if (clicked && _addBtnRect.Contains(mouse.X, mouse.Y) && _addText.Length > 0)
            {
                _client.AddFriend(_addText.Trim());
                _addText = "";
                _addFieldFocused = false;
            }

            if (_selectedFriendIndex >= 0 && _selectedFriendIndex < _friends.Count)
            {
                var sel = _friends[_selectedFriendIndex];
                int by = fy + 30;
                int btnW = (ContentW - 16) / 3; // три кнопки в ряд
                _whisperBtnRect = new Rectangle(cx, by, btnW, 24);
                _groupBtnRect = new Rectangle(cx + btnW + 8, by, btnW, 24);
                _removeBtnRect = new Rectangle(cx + (btnW + 8) * 2, by, btnW, 24);

                if (clicked && _whisperBtnRect.Contains(mouse.X, mouse.Y))
                    WhisperRequested?.Invoke(sel.Name);
                if (clicked && _groupBtnRect.Contains(mouse.X, mouse.Y) && CanInvite(sel))
                {
                    // Пригласить в группу (серверная логика уже есть: party_invite)
                    _client.SendAsync("party_invite", new { TargetName = sel.Name });
                    _resultMessage = $"Приглашение в группу отправлено {sel.Name}";
                    _resultTimer = 3f;
                }
                if (clicked && _removeBtnRect.Contains(mouse.X, mouse.Y))
                    _client.RemoveFriend(sel.Name);
            }

            HandleNameInput();
        }
        else if (_activeTab == Tab.Group)
        {
            int listY = cy + 32;
            int rowH = 30;
            var members = _party?.Members;
            int count = members?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var row = new Rectangle(cx, listY + i * rowH, ContentW, rowH);
                if (clicked && row.Contains(mouse.X, mouse.Y))
                    _selectedMemberIndex = i;
            }

            if (_selectedMemberIndex >= 0 && _selectedMemberIndex < count)
            {
                var sel = members![_selectedMemberIndex];
                int by = listY + count * rowH + 8;
                int btnW = (ContentW - 8) / 2;
                _transferBtnRect = new Rectangle(cx, by, btnW, 26);
                _kickBtnRect = new Rectangle(cx + btnW + 8, by, btnW, 26);

                // Кнопка "сделать лидером" только для лидера и не для самого себя
                bool canTransfer = _isPartyLeader && sel.PlayerId != _client.PlayerId;
                if (clicked && _transferBtnRect.Contains(mouse.X, mouse.Y) && canTransfer)
                {
                    _client.SendAsync("party_transfer", new { TargetName = sel.Name });
                    _resultMessage = $"Лидерство передано {sel.Name}";
                    _resultTimer = 3f;
                    _selectedMemberIndex = -1;
                }

                bool canKick = _isPartyLeader && sel.PlayerId != _client.PlayerId;
                if (clicked && _kickBtnRect.Contains(mouse.X, mouse.Y) && canKick)
                {
                    _client.SendAsync("party_kick", new { TargetName = sel.Name });
                    _resultMessage = $"{sel.Name} исключён из группы";
                    _resultTimer = 3f;
                    _selectedMemberIndex = -1;
                }
            }
            else
            {
                _transferBtnRect = Rectangle.Empty;
                _kickBtnRect = Rectangle.Empty;
            }
        }

        _prevMouseSocial = mouse;
    }

    private bool CanInvite(FriendInfo friend)
    {
        if (!friend.Online) return false;
        // Нельзя приглашать того, кто уже в твоей группе (защита на стороне UI;
        // сервер тоже запрещает — шлёт "уже в группе").
        if (_inParty && _partyMemberNames.Contains(friend.Name))
            return false;
        // Можно приглашать, если нет группы, либо ты её лидер
        if (!_inParty) return true;
        return _isPartyLeader;
    }

    private void HandleNameInput()
    {
        if (!_addFieldFocused) { _prevDownVks.Clear(); return; }

        bool shift = KeyboardLayoutHelper.IsShiftDown();
        bool russian = KeyboardLayoutHelper.IsRussianForeground();
        var nowDown = new HashSet<uint>(KeyboardLayoutHelper.GetPressedVks());
        foreach (var vk in nowDown)
        {
            if (_prevDownVks.Contains(vk)) continue;
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14 ||
                vk == 0x09 || vk == 0x0D || vk == 0x1B) continue;

            if (vk == 0x08) // Backspace
            {
                if (_addText.Length > 0) _addText = _addText[..^1];
                continue;
            }
            if (vk == 0x0D) // Enter — добавить
            {
                if (_addText.Length > 0) { _client.AddFriend(_addText.Trim()); _addText = ""; }
                _addFieldFocused = false;
                continue;
            }
            if (KeyCharMap.TryGetCharByVk(vk, russian, shift, out char ch))
                _addText += ch;
        }
        _prevDownVks = nowDown;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        base.Draw(sb);
        var mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();

        int cx = ContentX;
        int cy = ContentY;

        DrawTab(sb, "Друзья", new Rectangle(cx, cy, 100, 24), _activeTab == Tab.Friends, mouse);
        DrawTab(sb, "Группа", new Rectangle(cx + 104, cy, 100, 24), _activeTab == Tab.Group, mouse);
        DrawTab(sb, "Гильдия", new Rectangle(cx + 208, cy, 100, 24), _activeTab == Tab.Guild, mouse);

        if (_activeTab == Tab.Friends)
        {
            int listY = cy + 32;
            int listBlockH = ListVisibleRows * ListRowH;

            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            for (int i = 0; i < _friends.Count && i < ListVisibleRows; i++)
            {
                var f = _friends[i];
                var row = new Rectangle(cx, listY + i * ListRowH, ContentW, ListRowH);
                if (i == _selectedFriendIndex)
                    sb.Draw(SpriteCache.Pixel, row, new Color(60, 70, 95));
                Color dot = f.Online ? new Color(80, 200, 80) : new Color(120, 120, 130);
                sb.Draw(SpriteCache.Pixel, new Rectangle(cx + 4, listY + i * ListRowH + 9, 8, 8), dot);
                DrawText(sb, f.Name + (f.Online ? "" : " (оффлайн)"), cx + 18, listY + i * ListRowH + 4,
                    f.Online ? Color.White : new Color(180, 180, 190));
                if (font != null && f.Level > 0)
                    DrawText(sb, $"{f.Level} ур.", cx + ContentW - 50, listY + i * ListRowH + 4, new Color(200, 200, 120));
            }

            // Надпись "пусто" — внутри зарезервированного блока, НЕ под ним
            if (_friends.Count == 0)
                DrawText(sb, "Список пуст. Добавьте друга ниже.", cx, listY + 6, new Color(150, 150, 160));

            int fy = listY + listBlockH + 8;
            sb.Draw(SpriteCache.Pixel, _addFieldRect, _addFieldFocused ? new Color(25, 28, 36) : new Color(20, 22, 28));
            sb.Draw(SpriteCache.Pixel, new Rectangle(_addFieldRect.X, _addFieldRect.Y, _addFieldRect.Width, 1), new Color(80, 90, 110));
            DrawText(sb, _addText.Length > 0 ? _addText : "Имя друга...", _addFieldRect.X + 4, _addFieldRect.Y + 4,
                _addText.Length > 0 ? Color.White : new Color(130, 130, 140));
            DrawButton(sb, "Добавить", _addBtnRect, new Color(60, 110, 70), mouse, _prevMouseSocial);

            if (_selectedFriendIndex >= 0 && _selectedFriendIndex < _friends.Count)
            {
                var sel = _friends[_selectedFriendIndex];
                int by = fy + 30;
                DrawButton(sb, "Личное", _whisperBtnRect, new Color(70, 90, 130), mouse, _prevMouseSocial);

                // "В группу" активна только для онлайн-друзей, когда игрок
                // не в группе либо является её лидером
                bool canInvite = CanInvite(sel);
                Color groupColor = canInvite ? new Color(90, 110, 70) : new Color(70, 70, 78);
                DrawButton(sb, "В группу", _groupBtnRect, groupColor, mouse, _prevMouseSocial);

                DrawButton(sb, "Удалить", _removeBtnRect, new Color(140, 60, 60), mouse, _prevMouseSocial);

                // Подсказка под кнопками, если заблокировано из-за лидерства
                _showLeaderTip = !canInvite && _inParty && !_isPartyLeader &&
                                 _groupBtnRect.Contains(mouse.X, mouse.Y);
            }

            if (_resultTimer > 0)
                DrawText(sb, _resultMessage, cx, ContentY + ContentH - 20, new Color(220, 220, 150));

            // Подсказка "только лидер может приглашать" (стиль как в инвентаре),
            // привязана к курсору; показывается при наведении на "В группу" когда заблокировано
            if (_showLeaderTip)
                DrawTooltip(sb, new[] { "Только лидер может приглашать" }, mouse);
        }
        else if (_activeTab == Tab.Group)
        {
            int listY = cy + 32;
            int rowH = 30;
            var members = _party?.Members;
            int count = members?.Count ?? 0;

            if (!_inParty || count == 0)
            {
                DrawText(sb, "Вы не состоите в группе.", cx, listY, new Color(150, 150, 160));
            }
            else
            {
                var font = SpriteCache.FontSmall ?? SpriteCache.Font;
                for (int i = 0; i < count; i++)
                {
                    var m = members![i];
                    var row = new Rectangle(cx, listY + i * rowH, ContentW, rowH);
                    if (i == _selectedMemberIndex)
                        sb.Draw(SpriteCache.Pixel, row, new Color(60, 70, 95));
                    bool isLeader = m.PlayerId == _party!.LeaderId;
                    string nameStr = (isLeader ? "★ " : "  ") + m.Name + $" (ур. {m.Level})";
                    Color c = isLeader ? new Color(220, 200, 100) : new Color(200, 200, 210);
                    DrawText(sb, nameStr, cx + 4, listY + i * rowH + 6, c);
                    if (font != null)
                        DrawText(sb, $"{m.Health}/{m.MaxHealth}", cx + ContentW - 90, listY + i * rowH + 6, new Color(180, 180, 190));
                }

                if (_selectedMemberIndex >= 0 && _selectedMemberIndex < count)
                {
                    var sel = members![_selectedMemberIndex];
                    int by = listY + count * rowH + 8;
                    int btnW = (ContentW - 8) / 2;
                    _transferBtnRect = new Rectangle(cx, by, btnW, 26);
                    _kickBtnRect = new Rectangle(cx + btnW + 8, by, btnW, 26);

                    bool canTransfer = _isPartyLeader && sel.PlayerId != _client.PlayerId;
                    Color trColor = canTransfer ? new Color(90, 110, 70) : new Color(70, 70, 78);
                    DrawButton(sb, "Сделать лидером", _transferBtnRect, trColor, mouse, _prevMouseSocial);

                    bool canKick = _isPartyLeader && sel.PlayerId != _client.PlayerId;
                    Color kickColor = canKick ? new Color(140, 60, 60) : new Color(70, 70, 78);
                    DrawButton(sb, "Исключить", _kickBtnRect, kickColor, mouse, _prevMouseSocial);
                }
            }

            if (_resultTimer > 0)
                DrawText(sb, _resultMessage, cx, ContentY + ContentH - 20, new Color(220, 220, 150));
        }
        else
        {
            DrawText(sb, "Гильдии появятся в следующем обновлении.", cx, cy + 40, new Color(150, 150, 160));
        }
    }

    private void DrawTab(SpriteBatch sb, string label, Rectangle rect, bool active, MouseState mouse)
    {
        sb.Draw(SpriteCache.Pixel, rect, active ? new Color(60, 70, 95) : new Color(40, 44, 55));
        sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(90, 100, 120));
        int w = (int)(SpriteCache.FontSmall?.MeasureString(label).X ?? 0);
        DrawText(sb, label, rect.X + (rect.Width - w) / 2, rect.Y + 4,
            active ? Color.White : new Color(170, 170, 180));
    }

    // Тултип в том же стиле, что и в инвентаре: тёмная панель с синей
    // верхней полосой и текстом (первый регистр — золотистый).
    // Привязан к курсору мыши (mouse.X + 16, mouse.Y + 16), как в инвентаре.
    private void DrawTooltip(SpriteBatch sb, string[] lines, MouseState mouse)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null || lines.Length == 0) return;

        int pad = 8;
        float tw = 0;
        foreach (var l in lines) tw = Math.Max(tw, font.MeasureString(l).X);
        int ww = (int)tw + pad * 2;
        int th = lines.Length * 18 + pad * 2;

        int tx = mouse.X + 16;
        int ty = mouse.Y + 16;
        if (tx + ww > GameMain.Instance!.Graphics.PreferredBackBufferWidth)
            tx = GameMain.Instance!.Graphics.PreferredBackBufferWidth - ww - 4;
        if (ty + th > GameMain.Instance!.Graphics.PreferredBackBufferHeight)
            ty = GameMain.Instance!.Graphics.PreferredBackBufferHeight - th - 4;

        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, th), new Color(20, 22, 30, 230));
        sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, 2), new Color(80, 120, 200));
        for (int i = 0; i < lines.Length; i++)
        {
            var color = i == 0 ? new Color(230, 220, 140) : Color.White;
            sb.DrawString(font, lines[i], new Vector2(tx + pad, ty + pad + i * 18), color);
        }
    }
}
