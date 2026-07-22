using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Windows;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Screens;

internal class GameHudRenderer
{
    private readonly HudRenderer _hud;
    private readonly MapRenderer _map;

    // Icon bar state
    private Rectangle[] _iconRects = Array.Empty<Rectangle>();
    private const int IconSize = 40;
    private const int IconGap = 6;

    // Party button rects (used for click detection in Update)
    internal Rectangle InvitePartyRect = Rectangle.Empty;
    internal Rectangle TradePlayerRect = Rectangle.Empty;
    internal Rectangle PartyLeaveRect = Rectangle.Empty;
    internal Rectangle PartyDisbandRect = Rectangle.Empty;

    internal Rectangle[] IconRects => _iconRects;

    internal GameHudRenderer(HudRenderer hud, MapRenderer map)
    {
        _hud = hud;
        _map = map;
    }

    internal void DrawTopBar(SpriteBatch sb, int w, int h, GameMain game)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(0, 0, w, h), new Color(220, 225, 235));

        var font = SpriteCache.Font;
        if (font == null) return;

        var client = game.Client;
        string status = client.IsConnected ? "Подключено" : "Отключено";
        Color statusColor = client.IsConnected ? Color.LimeGreen : Color.Red;

        sb.DrawString(font, "IP:", new Vector2(10, 10), Color.Black);
        sb.DrawString(font, "127.0.0.1", new Vector2(40, 10), Color.Black);
        sb.DrawString(font, status, new Vector2(200, 10), statusColor);

        if (client.Status != null)
        {
            var info = $"Золото: {client.Status.Gold}  |  ATK {client.Status.PhysAttack}  DEF {client.Status.Defense}";
            sb.DrawString(font, info, new Vector2(350, 10), new Color(60, 60, 70));
        }
    }

    internal void DrawQuestTracker(SpriteBatch sb, int screenW, List<QuestInfo> activeQuests)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null || activeQuests.Count == 0) return;

        int pad = 8;
        int maxTracked = 4;
        var tracked = activeQuests.Take(maxTracked).ToList();

        int lineH = 16;
        int headerH = 18;
        int blockH = headerH + tracked.Count * lineH + 8;
        int maxW = (int)font.MeasureString("ЗАДАНИЯ").X + pad * 2;
        int maxTextW = 320;
        foreach (var q in tracked)
        {
            string objLine = $"• {q.Title}  [{Math.Min(q.Current, q.Target)}/{q.Target}]";
            maxW = Math.Max(maxW, (int)font.MeasureString(objLine).X);
        }
        maxW = Math.Min(maxW, maxTextW);
        int boxW = maxW + pad * 2;
        int boxX = screenW - boxW - 12;
        int boxY = 48 + 30;

        sb.Draw(SpriteCache.Pixel, new Rectangle(boxX, boxY, boxW, blockH), new Color(20, 22, 30, 200));
        sb.Draw(SpriteCache.Pixel, new Rectangle(boxX, boxY, boxW, 2), new Color(90, 95, 115));

        sb.DrawString(font, "ЗАДАНИЯ", new Vector2(boxX + pad, boxY + 4), new Color(220, 200, 120));
        int y = boxY + headerH;
        foreach (var q in tracked)
        {
            bool done = q.Completed || (q.Current >= q.Target && q.Target > 0);
            Color c = done ? new Color(255, 210, 60) : Color.White;
            string line = $"• {q.Title}  [{Math.Min(q.Current, q.Target)}/{q.Target}]";
            while (line.Length > 4 && font.MeasureString(line).X > maxTextW)
                line = line.Substring(0, line.Length - 4) + "...";
            sb.DrawString(font, line, new Vector2(boxX + pad, y), c);
            y += lineH;
        }
    }

    internal void DrawPartyPanel(SpriteBatch sb, int x, int y, int panelW, GameMain game)
    {
        var party = _hud.Party;
        if (party == null || party.Members.Count == 0) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        bool isLeader = game.Client.PlayerId == party.LeaderId;

        int headerH = 22;
        int memberNameH = 16;
        int barH = 14;
        int memberH = memberNameH + barH + 6;
        int btnH = 26;
        int btnGap = 4;
        int padding = 10;

        int buttonsH = btnH + padding;
        if (isLeader) buttonsH += btnH + btnGap;

        int panelH = headerH + party.Members.Count * memberH + buttonsH;

        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, panelW, panelH), new Color(30, 35, 48, 220));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, panelW, 1), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y + panelH - 1, panelW, 1), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, 1, panelH), new Color(80, 90, 110));
        sb.Draw(SpriteCache.Pixel, new Rectangle(x + panelW - 1, y, 1, panelH), new Color(80, 90, 110));

        int cx = x + 8;
        int cw = panelW - 16;
        int cy = y + 4;

        string title = $"Группа ({party.Members.Count}/5)";
        sb.DrawString(font, title, new Vector2(cx, cy), new Color(220, 200, 120));
        cy += headerH;

        foreach (var m in party.Members)
        {
            bool mLdr = m.PlayerId == party.LeaderId;
            string nameStr = (mLdr ? "★ " : "  ") + m.Name;
            sb.DrawString(font, nameStr, new Vector2(cx, cy), mLdr ? new Color(220, 200, 120) : new Color(200, 200, 210));
            cy += memberNameH;

            float hpPct = m.MaxHealth > 0 ? (float)m.Health / m.MaxHealth : 0;
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, barH), new Color(60, 30, 30));
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, (int)(cw * hpPct), barH), new Color(180, 50, 50));
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, 1), new Color(80, 40, 40));
            sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy + barH - 1, cw, 1), new Color(80, 40, 40));

            string hpText = $"{m.Health}/{m.MaxHealth}";
            var hpSize = font.MeasureString(hpText);
            sb.DrawString(font, hpText, new Vector2(cx + (cw - hpSize.X) / 2, cy + (barH - hpSize.Y) / 2), Color.White);

            cy += barH + 4;
        }

        cy += btnGap;
        var ms = Mouse.GetState();

        PartyLeaveRect = new Rectangle(cx, cy, cw, btnH);
        bool leaveHov = PartyLeaveRect.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, PartyLeaveRect, leaveHov ? new Color(180, 70, 70) : new Color(130, 50, 50));
        string leaveTxt = "Покинуть группу";
        var leaveSize = font.MeasureString(leaveTxt);
        sb.DrawString(font, leaveTxt, new Vector2(PartyLeaveRect.X + (PartyLeaveRect.Width - leaveSize.X) / 2, PartyLeaveRect.Y + (PartyLeaveRect.Height - leaveSize.Y) / 2), Color.White);

        if (isLeader)
        {
            cy += btnH + btnGap;
            PartyDisbandRect = new Rectangle(cx, cy, cw, btnH);
            bool disbandHov = PartyDisbandRect.Contains(ms.X, ms.Y);
            sb.Draw(SpriteCache.Pixel, PartyDisbandRect, disbandHov ? new Color(190, 80, 80) : new Color(150, 60, 60));
            string disbandTxt = "Распустить группу";
            var disbandSize = font.MeasureString(disbandTxt);
            sb.DrawString(font, disbandTxt, new Vector2(PartyDisbandRect.X + (PartyDisbandRect.Width - disbandSize.X) / 2, PartyDisbandRect.Y + (PartyDisbandRect.Height - disbandSize.Y) / 2), Color.White);
        }
        else
        {
            PartyDisbandRect = Rectangle.Empty;
        }
    }

    internal void DrawIconBar(SpriteBatch sb)
    {
        var mouse = Mouse.GetState();
        if (_iconRects.Length < 7) return;
        var icons = new Texture2D?[]
        {
            SpriteCache.GetIconStatus(),
            SpriteCache.GetIconInventory(),
            SpriteCache.GetIconSkills(),
            SpriteCache.ForItemType("weapon"),
            SpriteCache.GetIconCommunication(),
            null,
            SpriteCache.GetIconSettings()
        };

        for (int i = 0; i < 7; i++)
        {
            var r = _iconRects[i];
            bool hover = r.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, r, hover ? new Color(70, 75, 95) : new Color(45, 48, 60));
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, 2), new Color(90, 95, 115));

            var spr = icons[i];
            if (spr != null)
            {
                int pad = 6;
                sb.Draw(spr, new Rectangle(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2), Color.White);
            }
            else
            {
                var font = SpriteCache.FontSmall ?? SpriteCache.Font;
                if (font != null)
                    sb.DrawString(font, "J", new Vector2(r.X + r.Width / 2 - 4, r.Y + r.Height / 2 - 8), Color.Gray);
            }
        }
    }

    internal void LayoutIconBar(int w, int h)
    {
        int iconCount = 7;
        int iconsTotalW = iconCount * IconSize + (iconCount - 1) * IconGap;
        int iconY = h - IconSize - 8;
        int iconStartX = w - 8 - iconsTotalW;
        _iconRects = new Rectangle[iconCount];
        for (int i = 0; i < iconCount; i++)
            _iconRects[i] = new Rectangle(iconStartX + i * (IconSize + IconGap), iconY, IconSize, IconSize);
    }

    internal void DrawDragOverlay(SpriteBatch sb, Item? dragItem, ClientSkillInfo? dragSkill, int hitHotbarIdx, GameMain game)
    {
        if (dragItem != null)
        {
            var ms = Mouse.GetState();
            var spr = SpriteCache.ForItemType(dragItem.Type);
            int sz = 44;
            var r = new Rectangle(ms.X - sz / 2, ms.Y - sz / 2, sz, sz);
            if (spr != null)
                sb.Draw(spr, r, Color.White * 0.95f);
            else
                sb.Draw(SpriteCache.Pixel, r, new Color(120, 120, 140, 240));

            if (hitHotbarIdx >= 0)
                DrawHotbarHighlight(sb, hitHotbarIdx, game, new Color(90, 200, 120, 130));
        }
        else if (dragSkill != null)
        {
            var ms = Mouse.GetState();
            int sz = 44;
            var r = new Rectangle(ms.X - sz / 2, ms.Y - sz / 2, sz, sz);
            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            var spr = SpriteCache.ForItemType(dragSkill.Type);
            sb.Draw(SpriteCache.Pixel, r, new Color(44, 48, 64, 235));
            if (spr != null)
                sb.Draw(spr, new Rectangle(r.X + 8, r.Y + 8, 28, 28), Color.White);
            if (font != null)
            {
                var label = dragSkill.Name;
                var m = font.MeasureString(label);
                sb.DrawString(font, label, new Vector2(ms.X - m.X / 2, ms.Y + sz / 2 + 2), new Color(200, 220, 255));
            }

            if (hitHotbarIdx >= 0)
                DrawHotbarHighlight(sb, hitHotbarIdx, game, new Color(90, 150, 220, 120));
        }
    }

    private void DrawHotbarHighlight(SpriteBatch sb, int idx, GameMain game, Color color)
    {
        var g = game.Graphics;
        int hbW = (int)(g.PreferredBackBufferWidth * 0.35f);
        int hbX = (g.PreferredBackBufferWidth - hbW) / 2;
        int hbY = g.PreferredBackBufferHeight - 64 - 8;
        int slotW = hbW / 10;
        int hbSize = slotW - 6;
        var slotRect = new Rectangle(hbX + idx * slotW + (slotW - hbSize) / 2, hbY + (64 - hbSize) / 2, hbSize, hbSize);
        sb.Draw(SpriteCache.Pixel, slotRect, color);
    }

    internal void DrawTargetButtons(SpriteBatch sb, int w, GameMain game)
    {
        var sel = _map.GetSelectedEntity();
        InvitePartyRect = Rectangle.Empty;
        TradePlayerRect = Rectangle.Empty;
        var party = _hud.Party;
        bool canInvite = sel != null && sel.Type == "player" &&
            (party == null || party.Members.Count == 0 || game.Client.PlayerId == party.LeaderId);
        bool targetInParty = party != null && sel != null && sel.Type == "player" &&
            party.Members.Any(m => m.Name == sel.Name);
        if (sel != null && sel.Type == "player")
        {
            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (font != null)
            {
                int btnW = 200;
                int btnH = 24;
                int btnX = (w - btnW) / 2;
                int btnY = 84;

                if (canInvite)
                {
                    InvitePartyRect = new Rectangle(btnX, btnY, btnW, btnH);
                    var ms = Mouse.GetState();
                    bool hov = InvitePartyRect.Contains(ms.X, ms.Y);
                    sb.Draw(SpriteCache.Pixel, InvitePartyRect, hov ? new Color(70, 190, 100) : new Color(50, 160, 80));
                    var txt = "Пригласить в группу";
                    var ts = font.MeasureString(txt);
                    sb.DrawString(font, txt, new Vector2(btnX + (btnW - ts.X) / 2, btnY + (btnH - ts.Y) / 2), Color.White);
                    btnY += btnH + 4;
                }
                else if (targetInParty)
                {
                    var infoRect = new Rectangle(btnX, btnY, btnW, btnH);
                    sb.Draw(SpriteCache.Pixel, infoRect, new Color(70, 75, 90));
                    var txt = "В группе";
                    var ts = font.MeasureString(txt);
                    sb.DrawString(font, txt, new Vector2(btnX + (btnW - ts.X) / 2, btnY + (btnH - ts.Y) / 2), new Color(180, 185, 195));
                    btnY += btnH + 4;
                }

                TradePlayerRect = new Rectangle(btnX, btnY, btnW, btnH);
                var tMs = Mouse.GetState();
                bool tHov = TradePlayerRect.Contains(tMs.X, tMs.Y);
                sb.Draw(SpriteCache.Pixel, TradePlayerRect, tHov ? new Color(200, 170, 60) : new Color(160, 130, 40));
                var tTxt = "Обмен";
                var tSz = font.MeasureString(tTxt);
                sb.DrawString(font, tTxt, new Vector2(btnX + (btnW - tSz.X) / 2, btnY + (btnH - tSz.Y) / 2), Color.White);
            }
        }
    }
}
