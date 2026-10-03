namespace StoneForge;

/// <summary>Drawing on the screen, in GUI coordinates (0,0 top left; <see cref="Width"/> x <see cref="Height"/>,
/// in units of the game's UI scale - <see cref="Scale"/>),
/// from a <see cref="ModContext.DrawGui"/> handler - the game's Draw GUI pass, over everything. Panels and text
/// are the game's own (its menus' background, its text with a shadow).</summary>
public static class Draw
{
    public const int AlignLeft = 0, AlignCenter = 1, AlignRight = 2;
    public const int AlignTop = 0, AlignMiddle = 1, AlignBottom = 2;

    /// <summary>A colour from red, green, blue (0-255), as GameMaker stores them.</summary>
    public static int Rgb(int r, int g, int b) => (r & 255) | (g & 255) << 8 | (b & 255) << 16;
    public static readonly int White = Rgb(255, 255, 255);
    public static readonly int Black = 0;
    /// <summary>The game's muted text colour (hover texts, labels).</summary>
    public static readonly int Muted = Rgb(149, 121, 106);
    /// <summary>The game's panel colour (its menus' background).</summary>
    public static readonly int PanelColour = Rgb(27, 25, 38);

    /// <summary>The UI's scale: screen pixels per unit of these coordinates. It's the game's own UI scale (its
    /// camera scale - 2 in a window around 1280x720), so mods' UI is the size of the game's, its pixel art as
    /// crisp; everything here (and <see cref="Mouse"/>) is in its units.</summary>
    public static double Scale { get; private set; } = 1;

    /// <summary>The screen's size, in the UI's units (<see cref="Scale"/>).</summary>
    public static double Width => Game.CallBuiltin("display_get_gui_width").AsReal / Scale;
    public static double Height => Game.CallBuiltin("display_get_gui_height").AsReal / Scale;

    // Each Draw GUI pass: the game's UI unit (window_ratio * cameraScale window pixels, as its own GUI) in GUI
    // pixels. (1 until the game's camera is set up.)
    internal static void UpdateScale()
    {
        double unit = Game.Global["window_ratio"].AsReal * Game.Global["cameraScale"].AsReal;
        double guiWidth = Game.CallBuiltin("display_get_gui_width").AsReal;
        double windowWidth = Game.CallBuiltinTrusted("window_get_width", default, default).AsReal;
        double scale = windowWidth > 0 ? unit * guiWidth / windowWidth : 0;
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        if (scale != Scale)
            Game.Log($"UI scale: {scale} (the game's: window_ratio {Game.Global["window_ratio"]}, cameraScale {Game.Global["cameraScale"]})");
        Scale = scale;
    }

    /// <summary>A panel: the game's menu background colour (scr_drawBG) with a two-tone border.</summary>
    public static void Panel(double x, double y, double width, double height, int? colour = null, double alpha = 1)
    {
        Scripts.scr_drawBG.Call(null, x, y, width, height, colour ?? PanelColour, alpha);
        Rectangle(x, y, x + width - 1, y + height - 1, Rgb(12, 11, 18), alpha, outline: true);
        Rectangle(x + 1, y + 1, x + width - 2, y + height - 2, Rgb(74, 66, 86), alpha, outline: true);
    }

    /// <summary>Text as the game draws it (its font, half size, with a shadow).</summary>
    public static void Text(double x, double y, string text, int? colour = null, int halign = AlignLeft, int valign = AlignTop, double alpha = 1)
        => Scripts.scr_drawText.Call(null, x, y, text, colour ?? White, halign, valign, GmValue.Undefined, GmValue.Undefined, alpha);

    /// <summary>Text wrapped at <paramref name="width"/>, as <see cref="Text"/>.</summary>
    public static void TextWrapped(double x, double y, string text, double width, int? colour = null)
    {
        GmValue font = Game.Global["f_dmg"];
        GmValue previous = Game.CallBuiltin("draw_get_font");
        Game.CallBuiltin("draw_set_font", font);
        Game.CallBuiltin("draw_set_halign", 0);
        Game.CallBuiltin("draw_set_valign", 0);
        Game.CallBuiltin("draw_set_colour", Black);
        Game.CallBuiltin("draw_text_ext_transformed", x + 1, y + 1, text, -1, width * 2, 0.5, 0.5, 0);
        Game.CallBuiltin("draw_set_colour", colour ?? White);
        Game.CallBuiltin("draw_text_ext_transformed", x, y, text, -1, width * 2, 0.5, 0.5, 0);
        Game.CallBuiltin("draw_set_colour", White);
        Game.CallBuiltin("draw_set_font", previous);
    }

    /// <summary>How wide <see cref="Text"/> draws this text (one line).</summary>
    public static double TextWidth(string text)
    {
        GmValue previous = Game.CallBuiltin("draw_get_font");
        Game.CallBuiltin("draw_set_font", Game.Global["f_dmg"]);
        double width = Game.CallBuiltin("string_width", text).AsReal / 2;
        Game.CallBuiltin("draw_set_font", previous);
        return width;
    }

    /// <summary>The frame of the game's hover windows (tooltips): its background, edges and corners.</summary>
    public static void Frame(double x, double y, double width, double height)
        => Scripts.scr_hoversDrawBoard.Call(null, x, y, width, height, 1, true, GmValue.From(global::StoneForge.Sprite.s_hcorner));

    private static readonly Dictionary<int, (double Width, double Height)> SpriteSizes = new();

    private static (double Width, double Height) SpriteSize(int sprite)
    {
        if (!SpriteSizes.TryGetValue(sprite, out var size))
        {
            size = (Game.CallBuiltin("sprite_get_width", sprite).AsReal, Game.CallBuiltin("sprite_get_height", sprite).AsReal);
            SpriteSizes[sprite] = size;
        }
        return size;
    }

    /// <summary>A sprite's size, in pixels (unscaled).</summary>
    public static double SpriteWidth(int sprite) => sprite < 0 ? 0 : SpriteSize(sprite).Width;
    public static double SpriteHeight(int sprite) => sprite < 0 ? 0 : SpriteSize(sprite).Height;
    public static double SpriteWidth(Sprite sprite) => SpriteWidth((int)sprite);
    public static double SpriteHeight(Sprite sprite) => SpriteHeight((int)sprite);

    /// <summary>A game sprite, stretched to <paramref name="xscale"/> x <paramref name="yscale"/>.</summary>
    public static void Sprite(Sprite sprite, double x, double y, int frame = 0, double xscale = 1, double yscale = 1, int? colour = null, double alpha = 1)
        => Game.CallBuiltin("draw_sprite_ext", (int)sprite, frame, x, y, xscale, yscale, 0, colour ?? White, alpha);

    /// <summary>Part of a sprite (from <paramref name="left"/>, <paramref name="top"/> in it), as the game draws
    /// its health bars.</summary>
    public static void SpritePart(int sprite, int frame, double left, double top, double width, double height, double x, double y, double xscale = 1, double yscale = 1, double alpha = 1)
    {
        if (sprite >= 0 && width > 0 && height > 0)
            Game.CallBuiltin("draw_sprite_part_ext", sprite, frame, left, top, width, height, x, y, xscale, yscale, White, alpha);
    }

    /// <summary>How tall <see cref="TextWrapped"/> draws this text.</summary>
    public static double TextHeight(string text, double width)
    {
        GmValue previous = Game.CallBuiltin("draw_get_font");
        Game.CallBuiltin("draw_set_font", Game.Global["f_dmg"]);
        double height = Game.CallBuiltin("string_height_ext", text, -1, width * 2).AsReal / 2;
        Game.CallBuiltin("draw_set_font", previous);
        return height;
    }

    /// <summary>A filled rectangle (or its outline).</summary>
    public static void Rectangle(double x1, double y1, double x2, double y2, int colour, double alpha = 1, bool outline = false)
    {
        Game.CallBuiltin("draw_set_alpha", alpha);
        Game.CallBuiltin("draw_rectangle_colour", x1, y1, x2, y2, colour, colour, colour, colour, outline);
        Game.CallBuiltin("draw_set_alpha", 1);
    }

    /// <summary>A sprite stretched to <paramref name="width"/> x <paramref name="height"/> but for its ends:
    /// <paramref name="cap"/> pixels at its left and right stay their size (a button's rounded ends).</summary>
    public static void SpriteSliced(int sprite, int frame, double x, double y, double width, double height, double cap, double alpha = 1)
    {
        if (sprite < 0)
            return;
        double spriteWidth = SpriteWidth(sprite), spriteHeight = SpriteHeight(sprite);
        double yScale = height / spriteHeight;
        cap = Math.Min(cap, Math.Min(spriteWidth, width) / 2);
        double middle = spriteWidth - cap * 2, middleWidth = width - cap * 2;
        Game.CallBuiltin("draw_sprite_part_ext", sprite, frame, 0, 0, cap, spriteHeight, x, y, 1, yScale, White, alpha);
        if (middle > 0 && middleWidth > 0)
            Game.CallBuiltin("draw_sprite_part_ext", sprite, frame, cap, 0, middle, spriteHeight, x + cap, y, middleWidth / middle, yScale, White, alpha);
        Game.CallBuiltin("draw_sprite_part_ext", sprite, frame, spriteWidth - cap, 0, cap, spriteHeight, x + width - cap, y, 1, yScale, White, alpha);
    }

    /// <summary>A sprite 9-sliced to <paramref name="width"/> x <paramref name="height"/>: its corners
    /// (<paramref name="borders"/> in from each edge) stay their size, its edges stretch along, its middle both ways - a
    /// window frame made any size.</summary>
    public static void SpriteNineSlice(int sprite, int frame, double x, double y, double width, double height, UIInsets borders, double alpha = 1)
    {
        if (sprite < 0)
            return;
        double sw = SpriteWidth(sprite), sh = SpriteHeight(sprite);
        double l = Math.Min(borders.Left, sw / 2), r = Math.Min(borders.Right, sw / 2);
        double t = Math.Min(borders.Top, sh / 2), b = Math.Min(borders.Bottom, sh / 2);
        // (Source columns / rows, and where and how big they're drawn.)
        double[] srcX = { 0, l, sw - r }, srcW = { l, sw - l - r, r };
        double[] dstX = { x, x + l, x + width - r }, dstW = { l, width - l - r, r };
        double[] srcY = { 0, t, sh - b }, srcH = { t, sh - t - b, b };
        double[] dstY = { y, y + t, y + height - b }, dstH = { t, height - t - b, b };
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                if (srcW[col] <= 0 || srcH[row] <= 0 || dstW[col] <= 0 || dstH[row] <= 0)
                    continue;
                Game.CallBuiltin("draw_sprite_part_ext", sprite, frame, srcX[col], srcY[row], srcW[col], srcH[row],
                    dstX[col], dstY[row], dstW[col] / srcW[col], dstH[row] / srcH[row], White, alpha);
            }
    }

    // The game's text in one of its fonts (a global's name: "f_digits" for its buttons and titles) at a size.
    internal static void GameText(double x, double y, string text, int colour, int halign, int valign, string font, double scale, double alpha = 1)
        => Scripts.scr_drawText.Call(null, x, y, text, colour, halign, valign, Game.Global[font], scale, alpha);

    /// <summary>A sprite (e.g. from <see cref="ModContext.LoadSprite"/>), scaled.</summary>
    public static void Sprite(int sprite, double x, double y, double scale = 1, int frame = 0, double alpha = 1)
    {
        if (sprite >= 0)
            Game.CallBuiltin("draw_sprite_ext", sprite, frame, x, y, scale, scale, 0, White, alpha);
    }
}
