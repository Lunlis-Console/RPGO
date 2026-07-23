using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;

namespace RPGGame.ClientMonoGame.Windows;

public class DialogueWindow : GameWindow
{
    private string _speaker = "";
    private string _text = "";
    private List<DialogueChoiceUi> _choices = new();
    private int _scrollY;
    private MouseState _prevMouse;
    private bool _wasVisible;

    public event Action<int>? ChoiceSelected;
    public event Action? DialogueClosed;

    public bool HasContent => _choices.Count > 0;

    protected override void OnClose()
    {
        base.OnClose();
        DialogueClosed?.Invoke();
    }

    public DialogueWindow()
    {
        Title = "Диалог";
        Width = 420;
        Height = 260;
        Visible = false;
    }

    public void SetNode(string speaker, string text, List<(string Text, int Index)> choices)
    {
        _speaker = speaker;
        _text = text;
        _choices = choices.Select(c => new DialogueChoiceUi { Text = c.Text, Index = c.Index }).ToList();
        _scrollY = 0;
        Visible = true;
    }

    public void CloseDialogue()
    {
        Visible = false;
        _speaker = "";
        _text = "";
        _choices.Clear();
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible && _wasVisible)
        {
            _wasVisible = false;
            if (_choices.Count > 0)
            {
                _choices.Clear();
                DialogueClosed?.Invoke();
            }
        }
        _wasVisible = Visible;
        if (!Visible) { _prevMouse = mouse; return; }

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

        int contentY = Y + TitleH + 8;
        int contentX = X + 12;
        int contentW = Width - 24;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) { _prevMouse = mouse; return; }

        int textStartY = contentY + 20;
        int textH = MeasureWrappedHeight(font, _text, contentW) + 8;
        int choiceH = 26;
        int choiceGap = 3;
        int choiceY = textStartY + textH + 4;

        int totalChoicesH = _choices.Count * (choiceH + choiceGap);
        int neededH = TitleH + 12 + textH + 8 + totalChoicesH + 16;
        if (neededH > Height) Height = neededH;

        if (clicked)
        {
            var closeRect = new Rectangle(X + Width - 24, Y + 4, 20, 20);
            if (closeRect.Contains(mouse.X, mouse.Y))
            {
                Visible = false;
                _speaker = "";
                _text = "";
                _choices.Clear();
                DialogueClosed?.Invoke();
                _prevMouse = mouse;
                return;
            }

            for (int i = 0; i < _choices.Count; i++)
            {
                var r = new Rectangle(contentX, choiceY + i * (choiceH + choiceGap), contentW, choiceH);
                if (r.Contains(mouse.X, mouse.Y))
                {
                    ChoiceSelected?.Invoke(_choices[i].Index);
                    break;
                }
            }
        }

        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;

        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        // Dynamic height based on content
        int contentX = X + 12;
        int contentY = Y + TitleH + 8;
        int contentW = Width - 24;

        var textSize = new Vector2(0, MeasureWrappedHeight(font, _text, contentW));
        int textH = (int)textSize.Y + 8;
        int choiceH = 26;
        int choiceGap = 3;
        int totalChoicesH = _choices.Count * (choiceH + choiceGap);
        int neededH = TitleH + 12 + textH + 8 + totalChoicesH + 16;
        if (neededH > Height) Height = neededH;

        // Background
        var windowRect = new Rectangle(X, Y, Width, Height);
        sb.Draw(SpriteCache.Pixel, windowRect, new Color(25, 28, 38));

        // Title bar
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, TitleH), new Color(40, 50, 70));

        // Borders
        var border = new Color(70, 80, 100);
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, 2), border);
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y + Height - 2, Width, 2), border);
        sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, 2, Height), border);
        sb.Draw(SpriteCache.Pixel, new Rectangle(X + Width - 2, Y, 2, Height), border);

        // Title
        var titleSize = font.MeasureString(Title);
        sb.DrawString(font, Title, new Vector2(X + 8, Y + (TitleH - titleSize.Y) / 2), Color.White);

        // Speaker
        sb.DrawString(font, _speaker, new Vector2(contentX, contentY), new Color(220, 200, 120));

        // NPC text
        int textStartY = contentY + 20;
        DrawWrappedText(sb, _text, contentX, textStartY, contentW, Color.White);

        // Choices
        int choiceY = textStartY + textH + 4;
        var mouse = Mouse.GetState();
        for (int i = 0; i < _choices.Count; i++)
        {
            var r = new Rectangle(contentX, choiceY + i * (choiceH + choiceGap), contentW, choiceH);
            bool hover = r.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, r, hover ? new Color(60, 70, 100) : new Color(40, 45, 65));

            // Border
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, 1), new Color(80, 90, 110));

            string label = $"{_choices[i].Text}";
            var labelSize = font.MeasureString(label);
            sb.DrawString(font, label, new Vector2(r.X + 8, r.Y + (choiceH - labelSize.Y) / 2), hover ? new Color(220, 220, 240) : new Color(180, 185, 200));
        }

        // Close button
        var closeRect = new Rectangle(X + Width - 24, Y + 4, 20, 20);
        Color closeColor = closeRect.Contains(mouse.X, mouse.Y) ? new Color(200, 60, 60) : new Color(140, 40, 40);
        sb.Draw(SpriteCache.Pixel, closeRect, closeColor);
        var xSize = font.MeasureString("X");
        sb.DrawString(font, "X", new Vector2(closeRect.X + (closeRect.Width - xSize.X) / 2, closeRect.Y + (closeRect.Height - xSize.Y) / 2), Color.White);
    }

    private void DrawWrappedText(SpriteBatch sb, string text, int x, int y, int maxWidth, Color color)
    {
        var font = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        var words = text.Split(' ');
        var line = "";
        int lineY = y;
        int lineH = (int)font.MeasureString("Wg").Y + 2;

        foreach (var word in words)
        {
            var test = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (font.MeasureString(test).X > maxWidth && !string.IsNullOrEmpty(line))
            {
                sb.DrawString(font, line, new Vector2(x, lineY), color);
                line = word;
                lineY += lineH;
            }
            else
            {
                line = test;
            }
        }
        if (!string.IsNullOrEmpty(line))
            sb.DrawString(font, line, new Vector2(x, lineY), color);
    }

    private int MeasureWrappedHeight(SpriteFont font, string text, int maxWidth)
    {
        var words = text.Split(' ');
        var line = "";
        int lineH = (int)font.MeasureString("Wg").Y + 2;
        int lines = 1;

        foreach (var word in words)
        {
            var test = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (font.MeasureString(test).X > maxWidth && !string.IsNullOrEmpty(line))
            {
                lines++;
                line = word;
            }
            else
            {
                line = test;
            }
        }
        return lines * lineH;
    }

    private class DialogueChoiceUi
    {
        public string Text { get; set; } = "";
        public int Index { get; set; }
    }
}
