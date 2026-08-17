using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace LostAndDivine.ClientMonoGame.Windows;

/// <summary>
/// Окно «Что нового»: список изменений обновлений. Показывается после входа
/// в мир, если на сервере версия новее, чем уже видел игрок.
/// </summary>
public sealed class ChangelogWindow : GameWindow
{
    private ChangelogData _data = new();
    private int _scrollOffset;
    private new MouseState _prevMouse;

    private const int LineHeight = 14;
    private const int HeaderHeight = 28;
    private const int EntryPadX = 12;
    private const int EntryPadY = 8;
    private const int EntrySpacing = 10;
    private const int BulletIndent = 16;

    private static readonly Color BgEntry = new(38, 40, 52);
    private static readonly Color AccentGold = new(220, 200, 120);
    private static readonly Color TextWhite = Color.White;
    private static readonly Color TextBody = new(200, 200, 210);
    private static readonly Color TextMuted = new(150, 150, 160);

    public ChangelogWindow()
    {
        Title = "Что нового";
        Width = 500;
        Height = 430;
        Visible = false;
    }

    public void SetData(ChangelogData data)
    {
        _data = data ?? new ChangelogData();
        _scrollOffset = 0;
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) return;

        base.Update(gameTime, keyboard, mouse);
        if (!Visible) return;

        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (wheel != 0)
            _scrollOffset += wheel > 0 ? -30 : 30;

        if (keyboard.IsKeyDown(Keys.PageUp))
            _scrollOffset = Math.Max(0, _scrollOffset - 30);
        if (keyboard.IsKeyDown(Keys.PageDown))
            _scrollOffset += 30;

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        if (clicked)
        {
            int cx = ContentX, cy = ContentY + HeaderHeight, cw = ContentW, ch = ContentH;
            int listH = ch - HeaderHeight - 32;
            int btnY = cy + listH + 6;
            int btnW = 120, btnH = 22;
            int btnX = cx + (cw - btnW) / 2;
            if (mouse.X >= btnX && mouse.X <= btnX + btnW && mouse.Y >= btnY && mouse.Y <= btnY + btnH)
                Visible = false;
        }

        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;

        base.Draw(sb, Mouse.GetState());

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        int cx = ContentX;
        int cy = ContentY;
        int cw = ContentW;
        int ch = ContentH;

        string header = "ЧТО НОВОГО";
        var headerSize = font.MeasureString(header);
        DrawText(sb, header, cx + (cw - (int)headerSize.X) / 2, cy, AccentGold);
        cy += HeaderHeight;

        int listH = ch - HeaderHeight - 32;

        sb.Draw(SpriteCache.Pixel, new Rectangle(cx, cy, cw, listH), new Color(20, 22, 28));

        if (_data.Entries.Count == 0)
        {
            string empty = "Изменений пока нет.";
            var emptySize = font.MeasureString(empty);
            DrawText(sb, empty, cx + (cw - (int)emptySize.X) / 2, cy + listH / 2 - (int)emptySize.Y / 2, TextMuted);
        }
        else
        {
            int totalContentHeight = 0;
            foreach (var e in _data.Entries)
                totalContentHeight += GetEntryHeight(e, cw, font) + EntrySpacing;
            totalContentHeight = Math.Max(0, totalContentHeight - EntrySpacing);

            int maxScroll = Math.Max(0, totalContentHeight - listH);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);

            int drawY = cy - _scrollOffset;
            var clipRect = new Rectangle(cx, cy, cw, listH);

            sb.End();
            var oldScissor = sb.GraphicsDevice.ScissorRectangle;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
                new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
            sb.GraphicsDevice.ScissorRectangle = clipRect;

            foreach (var e in _data.Entries)
            {
                int entryH = GetEntryHeight(e, cw, font);
                if (drawY < cy + listH && drawY + entryH > cy)
                    DrawEntry(sb, e, cx, drawY, cw, font);
                drawY += entryH + EntrySpacing;
            }

            sb.End();
            sb.GraphicsDevice.ScissorRectangle = oldScissor;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

            if (totalContentHeight > listH && maxScroll > 0)
            {
                int barH = Math.Max(30, (int)((float)listH / totalContentHeight * listH));
                int barY = cy + (int)((float)_scrollOffset / maxScroll * (listH - barH));
                sb.Draw(SpriteCache.Pixel, new Rectangle(cx + cw - 5, barY, 4, barH), new Color(100, 110, 130));
            }
        }

        int btnY = cy + listH + 6;
        int btnW = 120;
        int btnH = 22;
        int btnX = cx + (cw - btnW) / 2;
        var ms = Mouse.GetState();
        var closeRect = new Rectangle(btnX, btnY, btnW, btnH);
        bool closeHover = closeRect.Contains(ms.X, ms.Y);
        sb.Draw(SpriteCache.Pixel, closeRect, closeHover ? new Color(150, 60, 60) : new Color(80, 40, 40));
        DrawText(sb, "Понятно", btnX + (btnW - (int)font.MeasureString("Понятно").X) / 2, btnY + (btnH - (int)font.MeasureString("Понятно").Y) / 2, Color.White);
    }

    private int GetEntryHeight(ChangelogEntry e, int availableWidth, SpriteFont font)
    {
        int innerW = availableWidth - EntryPadX * 2 - BulletIndent;
        int h = EntryPadY * 2 + LineHeight; // заголовок «версия — дата»
        foreach (var item in e.Items)
        {
            if (string.IsNullOrEmpty(item)) { h += LineHeight; continue; }
            h += MeasureWrappedText(item, innerW, font).H;
            h += 2;
        }
        return h;
    }

    private void DrawEntry(SpriteBatch sb, ChangelogEntry e, int x, int y, int w, SpriteFont font)
    {
        int innerW = w - EntryPadX * 2 - BulletIndent;
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, w, GetEntryHeight(e, w, font)), BgEntry);
        sb.Draw(SpriteCache.Pixel, new Rectangle(x, y, 3, GetEntryHeight(e, w, font)), AccentGold);

        int textX = x + EntryPadX + 3;
        int textY = y + EntryPadY;

        string header = string.IsNullOrEmpty(e.Date) ? e.Version : $"{e.Version} — {e.Date}";
        DrawText(sb, header, textX, textY, AccentGold);
        textY += LineHeight;

        foreach (var item in e.Items)
        {
            if (string.IsNullOrEmpty(item))
            {
                textY += LineHeight;
                continue;
            }

            DrawText(sb, "•", x + EntryPadX + 3, textY, AccentGold);
            DrawWrappedText(sb, item, textX + BulletIndent, textY, innerW, TextBody, font);
            textY += MeasureWrappedText(item, innerW, font).H + 2;
        }
    }

    private static (int W, int H) MeasureWrappedText(string text, int maxW, SpriteFont font)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);

        int lines = 1;
        float lineWidth = 0;
        float spaceW = font.MeasureString(" ").X;

        foreach (var word in text.Split(' '))
        {
            float wordW = font.MeasureString(word).X;
            if (lineWidth > 0 && lineWidth + spaceW + wordW > maxW)
            {
                lines++;
                lineWidth = wordW;
            }
            else
            {
                lineWidth += (lineWidth > 0 ? spaceW : 0) + wordW;
            }
        }

        return ((int)Math.Min(lineWidth, maxW), lines * (int)font.LineSpacing);
    }

    private void DrawWrappedText(SpriteBatch sb, string text, int x, int y, int maxW, Color color, SpriteFont font)
    {
        if (string.IsNullOrEmpty(text)) return;

        float spaceW = font.MeasureString(" ").X;
        int curY = y;
        int lineHeight = (int)font.LineSpacing;
        float lineWidth = 0;

        foreach (var word in text.Split(' '))
        {
            float wordW = font.MeasureString(word).X;
            if (lineWidth > 0 && lineWidth + spaceW + wordW > maxW)
            {
                curY += lineHeight;
                lineWidth = 0;
            }
            float wordX = x + lineWidth + (lineWidth > 0 ? spaceW : 0);
            sb.DrawString(font, word, new Vector2(wordX, curY), color);
            lineWidth += (lineWidth > 0 ? spaceW : 0) + wordW;
        }
    }
}