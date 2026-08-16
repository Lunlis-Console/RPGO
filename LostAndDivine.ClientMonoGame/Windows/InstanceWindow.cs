using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Windows;

/// <summary>Окно выбора инстанса: список подземелий с кнопками Соло/Группа
/// и панель статусов участников при активном приглашении (для лидера).</summary>
public class InstanceWindow : GameWindow
{
    private readonly List<InstanceInfo> _instances = new();
    private readonly List<(string Name, string Status)> _sessionMembers = new();
    private string _sessionName = "";
    private bool _isLeader;
    private int _scrollOffset;
    private int _maxScroll;

    public Action<string>? SoloRequested;
    public Action<string>? GroupRequested;
    public Action? StartRequested;

    private const int RowH = 34;
    private const int ColBtnW = 64;
    private const int GroupBtnW = 76;

    private static readonly Color CGreen = new(50, 160, 80);
    private static readonly Color CGreenHover = new(70, 190, 100);
    private static readonly Color CGray = new(70, 75, 90);
    private static readonly Color CGrayHover = new(95, 100, 120);
    private static readonly Color CGold = new(220, 200, 120);
    private static readonly Color CReady = new(70, 190, 100);
    private static readonly Color CWaiting = new(200, 180, 90);
    private static readonly Color CDeclined = new(170, 70, 70);
    private static readonly Color CLight = new(210, 210, 220);

    public InstanceWindow()
    {
        Title = "Инстансы";
        Width = 460;
        Height = 520;
        Visible = false;
        IsModal = true;
    }

    public void Show(bool isLeader)
    {
        _isLeader = isLeader;
        _sessionMembers.Clear();
        _sessionName = "";
        _scrollOffset = 0;
        var g = GameMain.Instance!.Graphics;
        X = (g.PreferredBackBufferWidth - Width) / 2;
        Y = (g.PreferredBackBufferHeight - Height) / 2;
        Visible = true;
    }

    public void SetInstances(List<InstanceInfo> list)
    {
        _instances.Clear();
        _instances.AddRange(list);
        RecalcScroll();
    }

    public void SetSession(string templateName, List<InstanceMemberInfo> members)
    {
        _sessionName = templateName;
        _sessionMembers.Clear();
        foreach (var m in members)
            _sessionMembers.Add((m.Name, m.Status));
    }

    public void OnStarted(string templateName, string mode)
    {
        _sessionName = "";
        _sessionMembers.Clear();
    }

    private void RecalcScroll()
    {
        int rowArea = ContentH - (SessionPanelH() + HintH() + 8);
        _maxScroll = Math.Max(0, _instances.Count * RowH - rowArea);
        if (_scrollOffset > _maxScroll) _scrollOffset = _maxScroll;
    }

    private int SessionPanelH() => _sessionMembers.Count > 0 ? 60 + _sessionMembers.Count * 18 : 0;
    private int HintH() => 22;

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        int wheelDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

        base.Update(gameTime, keyboard, mouse);
        if (!Visible) return;

        if (wheelDelta != 0)
            _scrollOffset = Math.Clamp(_scrollOffset - wheelDelta / 120 * 30, 0, _maxScroll);

        int listTop = ContentY;
        int listBottom = listTop + ContentH - (SessionPanelH() + HintH() + 8);
        int btnX = X + Width - 8 - GroupBtnW;
        int soloX = btnX - ColBtnW - 6;

        // Кнопки строк списка
        for (int i = 0; i < _instances.Count; i++)
        {
            int y = listTop + i * RowH - _scrollOffset;
            if (y < listTop - RowH || y > listBottom) continue;
            var soloRect = new Rectangle(soloX, y + 4, ColBtnW, RowH - 8);
            var groupRect = new Rectangle(btnX, y + 4, GroupBtnW, RowH - 8);
            if (clicked)
            {
                if (soloRect.Contains(mouse.X, mouse.Y))
                    SoloRequested?.Invoke(_instances[i].Id);
                else if (groupRect.Contains(mouse.X, mouse.Y))
                    GroupRequested?.Invoke(_instances[i].Id);
            }
        }

        // Кнопка «Начать» (лидер, при активной сессии)
        if (_sessionMembers.Count > 0)
        {
            int startY = ContentY + ContentH - HintH() - 36;
            var startRect = new Rectangle(ContentX + ContentW - 110, startY, 110, 30);
            if (clicked && startRect.Contains(mouse.X, mouse.Y))
                StartRequested?.Invoke();
        }
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        base.Draw(sb, Mouse.GetState());

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;
        var ms = Mouse.GetState();

        int listTop = ContentY;
        int listBottom = listTop + ContentH - (SessionPanelH() + HintH() + 8);
        int btnX = X + Width - 8 - GroupBtnW;
        int soloX = btnX - ColBtnW - 6;

        // Список инстансов
        if (_instances.Count == 0)
        {
            sb.DrawString(font, "Загрузка...", new Vector2(ContentX, listTop), CLight);
        }
        else
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                int y = listTop + i * RowH - _scrollOffset;
                if (y < listTop - RowH || y > listBottom) continue;
                if (i % 2 == 0)
                    sb.Draw(SpriteCache.Pixel, new Rectangle(ContentX, y, ContentW, RowH), new Color(38, 41, 50));

                var inst = _instances[i];
                sb.DrawString(font, inst.Name, new Vector2(ContentX + 6, y + 5), Color.White);
                if (inst.MinLevel > 0)
                {
                    string lvl = $"ур. {inst.MinLevel}-{inst.MaxLevel}";
                    var lsz = font.MeasureString(lvl);
                    sb.DrawString(font, lvl, new Vector2(ContentX + 6, y + 20), CGold);
                }

                var soloRect = new Rectangle(soloX, y + 4, ColBtnW, RowH - 8);
                bool soloHover = soloRect.Contains(ms.X, ms.Y);
                sb.Draw(SpriteCache.Pixel, soloRect, soloHover ? CGreenHover : CGreen);
                var sz = font.MeasureString("Соло");
                sb.DrawString(font, "Соло", new Vector2(soloRect.X + (soloRect.Width - sz.X) / 2, soloRect.Y + (soloRect.Height - sz.Y) / 2), Color.White);

                var groupRect = new Rectangle(btnX, y + 4, GroupBtnW, RowH - 8);
                bool groupHover = groupRect.Contains(ms.X, ms.Y);
                sb.Draw(SpriteCache.Pixel, groupRect, groupHover ? CGrayHover : CGray);
                var gz = font.MeasureString("Группа");
                sb.DrawString(font, "Группа", new Vector2(groupRect.X + (groupRect.Width - gz.X) / 2, groupRect.Y + (groupRect.Height - gz.Y) / 2), Color.White);
            }
        }

        // Панель сессии приглашения (для лидера)
        if (_sessionMembers.Count > 0)
        {
            int panelY = listBottom + 4;
            sb.Draw(SpriteCache.Pixel, new Rectangle(ContentX, panelY, ContentW, SessionPanelH()), new Color(42, 38, 30));
            sb.DrawString(font, $"Приглашение в «{_sessionName}»", new Vector2(ContentX + 6, panelY + 4), CGold);
            int mY = panelY + 24;
            foreach (var (name, status) in _sessionMembers)
            {
                Color stColor = status == "ready" ? CReady : status == "declined" ? CDeclined : CWaiting;
                string stLabel = status == "ready" ? "Готов" : status == "declined" ? "Отказано" : "Ожидание";
                sb.DrawString(font, name, new Vector2(ContentX + 16, mY), Color.White);
                var stsz = font.MeasureString(stLabel);
                sb.DrawString(font, stLabel, new Vector2(ContentX + ContentW - 16 - stsz.X, mY), stColor);
                mY += 18;
            }

            if (_isLeader)
            {
                int startY = panelY + SessionPanelH() - 34;
                var startRect = new Rectangle(ContentX + ContentW - 110, startY, 110, 30);
                bool startHover = startRect.Contains(ms.X, ms.Y);
                sb.Draw(SpriteCache.Pixel, startRect, startHover ? CGreenHover : CGreen);
                var stsz2 = font.MeasureString("Начать");
                sb.DrawString(font, "Начать", new Vector2(startRect.X + (startRect.Width - stsz2.X) / 2, startRect.Y + (startRect.Height - stsz2.Y) / 2), Color.White);
            }
        }

        // Подсказка
        string hint = "Соло — в одиночку. Группа — до 5 игроков, мобов и награды больше.";
        var hsz = font.MeasureString(hint);
        sb.DrawString(font, hint, new Vector2(X + (Width - hsz.X) / 2, Y + Height - HintH() - 6), CLight);
    }
}