using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using System.Text.Json;

namespace RPGGame.ClientMonoGame.Rendering;

/// <summary>
/// Описание анимации из спрайт-листа (атласа PNG): сетка кадров cols x rows,
/// кадры перебираются по времени с заданной частотой (fps).
/// </summary>
public sealed class SpriteAnimation
{
    public Texture2D Sheet { get; }
    public int Cols { get; }
    public int Rows { get; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }
    public float FrameDuration { get; } // секунды на кадр

    public int FrameCount => Cols * Rows;

    public SpriteAnimation(Texture2D sheet, int cols, int rows, int frameWidth, int frameHeight, float frameDuration)
    {
        Sheet = sheet;
        Cols = Math.Max(1, cols);
        Rows = Math.Max(1, rows);
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        FrameDuration = frameDuration > 0 ? frameDuration : 0.125f;
    }

    public Rectangle GetSourceRect(int frameIndex)
    {
        int c = frameIndex % Cols;
        int r = frameIndex / Cols;
        return new Rectangle(c * FrameWidth, r * FrameHeight, FrameWidth, FrameHeight);
    }
}

public static class SpriteCache
{
    private static readonly Dictionary<string, Texture2D> _textures = new();
    private static readonly Dictionary<string, SpriteAnimation> _animations = new();
    private static Texture2D _pixel = null!;
    private static SpriteFont _font = null!;
    private static SpriteFont _fontSmall = null!;
    private static GraphicsDevice _device = null!;

        private static readonly Dictionary<string, string> MonsterSpriteMap = new()
        {
            ["M0001"] = "rat", ["M0002"] = "spyder", ["M0003"] = "zombie",
            ["M0004"] = "goblin", ["M0005"] = "skelet", ["M0006"] = "wolf",
            ["M0007"] = "bear", ["M0008"] = "ork", ["M0009"] = "dark_mage",
            ["M0010"] = "dragon_baby", ["M0011"] = "dragon", ["M0012"] = "lich",
            ["M0013"] = "snake"
        };

    public static SpriteFont Font => _font;
    public static SpriteFont FontSmall => _fontSmall;
    public static Texture2D Pixel => _pixel;

    public static void Load(GraphicsDevice device, Microsoft.Xna.Framework.Content.ContentManager content)
    {
        _device = device;

        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });

        // Нативный SpriteFont, собранный через MGCB (чёткий, читаемый)
        _font = content.Load<SpriteFont>("Fonts/GameFont");
        _fontSmall = _font;

        var spriteNames = new[]
        {
            "player", "zombie", "wolf", "weapon", "water", "spyder",
            "snake", "skelet", "sand", "rat", "ork", "misc", "lich",
            "grass", "gold", "goblin", "dragon_baby", "dragon",
            "dark_mage", "consumable", "collectible", "bear", "armor", "accessory",
            "trader", "quest_desk",
            "icon_communication", "icon_inventory", "icon_settings", "icon_skills", "icon_status",
            "skill",
            "Character_Sprite_1", "Character_Sprite_2_Left", "Character_Sprite_3_Right", "Character_Sprite_4"
        };
        foreach (var name in spriteNames)
            LoadTexture(name);

        Logger.Info($"SpriteCache loaded {_textures.Count}/{spriteNames.Length} textures");
    }

    private static Texture2D? LoadTexture(string name)
    {
        if (_textures.ContainsKey(name)) return _textures[name];
        try
        {
            var resName = $"RPGGame.ClientMonoGame.Content.Sprites.{name}.png";
            var asm = typeof(SpriteCache).Assembly;
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null)
            {
                var names = string.Join(", ", asm.GetManifestResourceNames().Where(n => n.Contains("Sprites")));
                Logger.Warn($"LoadTexture '{name}': resource stream is null. Available: [{names}]");
                return null;
            }
            var tex = Texture2D.FromStream(_device, stream);
            _textures[name] = tex;
            Logger.Debug($"LoadTexture '{name}' OK ({tex.Width}x{tex.Height})");
            return tex;
        }
        catch (Exception ex)
        {
            Logger.Error($"LoadTexture '{name}' failed", ex);
            return null;
        }
    }

    public static Texture2D? Get(string key) =>
        _textures.TryGetValue(key, out var tex) ? tex : null;

    public static Texture2D? GetMonsterSprite(string templateId) =>
        MonsterSpriteMap.TryGetValue(templateId, out var key) ? Get(key) : null;

    public static Texture2D? GetPlayerSprite() => Get("Character_Sprite_1");

    // Направление взгляда игрока: "down" | "up" | "left" | "right"
    public static Texture2D? GetPlayerSprite(string dir) => dir switch
    {
        "left" => Get("Character_Sprite_2_Left"),
        "right" => Get("Character_Sprite_3_Right"),
        "up" => Get("Character_Sprite_4"),
        _ => Get("Character_Sprite_1")
    };

    public static SpriteAnimation? GetAnimation(string key) =>
        _animations.TryGetValue(key, out var a) ? a : null;

    public static SpriteAnimation? GetPlayerAnimation() => GetAnimation("player");

    // Загружает описания анимаций (animations.json) и спрайт-листы из папки Content.
    // contentRoot — папка Content рядом с исполняемым файлом клиента.
    public static void LoadAnimations(string contentRoot)
    {
        _animations.Clear();
        try
        {
            string jsonPath = Path.Combine(contentRoot, "animations.json");
            if (!File.Exists(jsonPath)) return;
            var entries = JsonSerializer.Deserialize<List<AnimEntry>>(File.ReadAllText(jsonPath));
            if (entries == null) return;

            string animDir = Path.Combine(contentRoot, "Animations");
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e?.Key) || string.IsNullOrWhiteSpace(e.Sheet)) continue;
                string sheetPath = Path.Combine(animDir, e.Sheet);
                if (!File.Exists(sheetPath)) continue;
                try
                {
                    using var stream = File.OpenRead(sheetPath);
                    var tex = Texture2D.FromStream(_device, stream);
                    int cols = Math.Max(1, e.Cols);
                    int rows = Math.Max(1, e.Rows);
                    int fw = tex.Width / cols;
                    int fh = tex.Height / rows;
                    float fd = e.Fps > 0 ? 1f / e.Fps : 0.125f;
                    _animations[e.Key] = new SpriteAnimation(tex, cols, rows, fw, fh, fd);
                    Logger.Info($"SpriteCache: анимация '{e.Key}' загружена ({cols}x{rows}, {e.Fps} fps)");
                }
                catch (Exception ex)
                {
                    Logger.Error($"SpriteCache: не удалось загрузить анимацию '{e.Key}'", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SpriteCache.LoadAnimations failed", ex);
        }
    }

    private sealed class AnimEntry
    {
        public string Key { get; set; } = "";
        public string Sheet { get; set; } = "";
        public int Cols { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public int Fps { get; set; } = 8;
    }

    public static Texture2D? GetTraderSprite() => Get("trader");
    public static Texture2D? GetBoardSprite() => Get("quest_desk");
    public static Texture2D? GetCollectibleSprite() => Get("collectible");
    public static Texture2D? GetGrassSprite() => Get("grass");
    public static Texture2D? GetCorpseSprite() => Get("gold");

    public static Texture2D? GetIconStatus() => Get("icon_status");
    public static Texture2D? GetIconInventory() => Get("icon_inventory");
    public static Texture2D? GetIconSkills() => Get("icon_skills");
    public static Texture2D? GetIconCommunication() => Get("icon_communication");
    public static Texture2D? GetIconSettings() => Get("icon_settings");

    public static Texture2D? ForItemType(string? type) => (type ?? "").ToLower() switch
    {
        "weapon" => Get("weapon"),
        "armor" => Get("armor"),
        "accessory" => Get("accessory"),
        "consumable" => Get("consumable"),
        "collectible" => Get("collectible"),
        "trophy" => Get("misc"),
        _ => Get("misc")
    };

    public static void Unload()
    {
        foreach (var tex in _textures.Values)
            tex.Dispose();
        _textures.Clear();
        _pixel?.Dispose();
    }
}

/// <summary>
/// Generates a SpriteFont at runtime using System.Drawing (GDI) to rasterize
/// characters from a system TrueType font into a texture atlas.
/// Uses tight per-glyph packing so letters sit close together, and includes
/// the full ASCII + Cyrillic + common UI symbol set.
/// </summary>
internal static class FontGenerator
{
    private static System.Drawing.Font CreateFont(string preferred, float size)
    {
        var candidates = new[] { preferred, "Segoe UI", "Verdana", "Tahoma", "Arial", "Calibri" };
        foreach (var name in candidates)
        {
            try
            {
                // GraphicsUnit.Point даёт корректную высоту глифа (без "сплюснутости" Pixel-юнита)
                var f = new System.Drawing.Font(name, size, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
                if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || name == preferred)
                    return f;
                f.Dispose();
            }
            catch { }
        }
        return new System.Drawing.Font("Segoe UI", size, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    }

    public static SpriteFont Generate(GraphicsDevice device, string fontFamily, float fontSize)
    {
        string chars = BuildCharSet();

        using var sysFont = CreateFont(fontFamily, fontSize);

        using var measureBmp = new System.Drawing.Bitmap(1, 1);
        using var measureG = System.Drawing.Graphics.FromImage(measureBmp);
        measureG.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        var charWidths = new float[chars.Length];
        float maxH = 0;
        for (int i = 0; i < chars.Length; i++)
        {
            var size = measureG.MeasureString(chars[i].ToString(), sysFont);
            float w = (float)Math.Ceiling(size.Width);
            if (w < 1) w = 1;
            charWidths[i] = w;
            if (size.Height > maxH) maxH = size.Height;
        }

        // Вертикальный запас, чтобы глифы (ascender/descender) не обрезались
        int padY = (int)Math.Ceiling(fontSize * 0.35f) + 2;
        int charH = (int)Math.Ceiling(maxH) + padY * 2;
        int tracking = 0;
        var cellWidths = new int[chars.Length];
        var cellX = new int[chars.Length];
        int penX = 0;
        int texW = 0;
        for (int i = 0; i < chars.Length; i++)
        {
            // Не сжимаем ширину — иначе буквы "склеиваются" и теряются
            int w = (int)charWidths[i];
            cellX[i] = penX;
            cellWidths[i] = w;
            penX += w + tracking;
            texW = penX;
        }
        int texH = charH;

        using var bmp = new System.Drawing.Bitmap(texW, texH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Transparent);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var sf = new System.Drawing.StringFormat(System.Drawing.StringFormatFlags.NoWrap)
        {
            Alignment = System.Drawing.StringAlignment.Near,
            LineAlignment = System.Drawing.StringAlignment.Near
        };
        var layoutRect = new System.Drawing.RectangleF(0, 0, 0, charH);

        for (int i = 0; i < chars.Length; i++)
        {
            layoutRect.X = cellX[i];
            layoutRect.Width = cellWidths[i];
            // Глиф рисуется с верхним отступом padY — текст позиционируется от Y как раньше
            layoutRect.Y = padY;
            g.DrawString(chars[i].ToString(), sysFont, brush, layoutRect, sf);
        }

        // Convert Bitmap -> Texture2D
        var tex = new Texture2D(device, texW, texH);
        var texData = new Color[texW * texH];

        var bmpData = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, texW, texH),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        unsafe
        {
            var src = (byte*)bmpData.Scan0;
            for (int py = 0; py < texH; py++)
            {
                for (int px = 0; px < texW; px++)
                {
                    int si = py * bmpData.Stride + px * 4;
                    byte b = src[si], g2 = src[si + 1], r = src[si + 2], a = src[si + 3];
                    texData[py * texW + px] = new Color(r, g2, b, a);
                }
            }
        }
        bmp.UnlockBits(bmpData);
        tex.SetData(texData);

        // Build SpriteFont glyph data (each glyph its own width, packed tightly)
        var glyphRects = new List<Rectangle>();
        var cropping = new List<Rectangle>();
        var characters = new List<char>();
        var kerning = new List<Vector3>();

        for (int i = 0; i < chars.Length; i++)
        {
            int w = cellWidths[i];
            characters.Add(chars[i]);
            glyphRects.Add(new Rectangle(cellX[i], 0, w, charH));
            cropping.Add(new Rectangle(0, 0, w, charH));
            kerning.Add(new Vector3(0, w + tracking, 0));
        }

        return new SpriteFont(tex, glyphRects, cropping, characters, charH, 0, kerning, '?');
    }

    private static string BuildCharSet()
    {
        var set = new SortedSet<char>();
        // ASCII printable
        for (char c = ' '; c <= '~'; c++)
            set.Add(c);
        // Кириллица (включая Ё/ё)
        for (char c = 'А'; c <= 'Я'; c++) set.Add(c);
        for (char c = 'а'; c <= 'я'; c++) set.Add(c);
        set.Add('Ё');
        set.Add('ё');
        // Часто используемые символы в UI / луте / названиях предметов
        foreach (var c in "…—–•·’“”«»™°√×÷±≥≤⇒→←↔★☆♦♣♥♠⚔⚒⚙⚗⛏") set.Add(c);
        // Латинские расширенные (акценты)
        for (char c = 'À'; c <= 'ÿ'; c++) set.Add(c);
        return new string(set.ToArray());
    }
}
