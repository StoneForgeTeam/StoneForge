namespace StoneForge;

// UIElement.ClipChildren: the children drawn into a surface the size of the clipped area (GUI pixels, at the
// game's UI scale), drawn back in its place - as the game clips its own scrolled lists (it has no scissor on this
// runtime). The patcher's scr_stonemod_clip_begin / _end switch to the surface and back, keeping the Draw GUI
// pass's matrices. A surface not used for a while (its element gone or hidden) is freed.
internal static class Clip
{
    private const int UnusedFrames = 300;

    private sealed class Entry
    {
        public int Surface = -1;
        public int Width, Height;
        public int LastFrame;
        public double X, Y;
    }

    private static readonly Dictionary<UIElement, Entry> Surfaces = new(ReferenceEqualityComparer.Instance);
    private static int _frame;

    // Drawing into the element's surface from here (false: nothing to draw into - an empty area).
    internal static bool Begin(UIElement element, (double X, double Y, double Width, double Height) area)
    {
        double scale = Draw.Scale;
        int width = (int)Math.Ceiling(area.Width * scale), height = (int)Math.Ceiling(area.Height * scale);
        if (width <= 0 || height <= 0)
            return false;
        if (!Surfaces.TryGetValue(element, out var entry))
            Surfaces[element] = entry = new Entry();
        if (entry.Surface >= 0 && (entry.Width != width || entry.Height != height || !Exists(entry.Surface)))
        {
            Free(entry);
        }
        if (entry.Surface < 0)
        {
            entry.Surface = Game.CallBuiltinTrusted("surface_create", default, default, width, height).AsInt;
            entry.Width = width;
            entry.Height = height;
            if (entry.Surface < 0)
                return false;
        }
        entry.LastFrame = _frame;
        // (Whole pixels, so what's drawn isn't blurred moving the surface back.)
        double x = Math.Floor(area.X * scale), y = Math.Floor(area.Y * scale);
        entry.X = x / scale;
        entry.Y = y / scale;
        Game.CallScript("scr_stonemod_clip_begin", default, entry.Surface, scale, x, y);
        return true;
    }

    internal static void End(UIElement element)
    {
        var entry = Surfaces[element];
        Game.CallScript("scr_stonemod_clip_end", default, entry.Surface, Draw.Scale, entry.X, entry.Y);
    }

    // After each Draw GUI pass: surfaces no element has drawn into lately, freed.
    internal static void Sweep()
    {
        _frame++;
        if (_frame % 60 != 0 || Surfaces.Count == 0)
            return;
        foreach (var (element, entry) in Surfaces.ToList())
        {
            if (_frame - entry.LastFrame < UnusedFrames)
                continue;
            Free(entry);
            Surfaces.Remove(element);
        }
    }

    private static bool Exists(int surface) => Game.CallBuiltinTrusted("surface_exists", default, default, surface).AsBool;

    private static void Free(Entry entry)
    {
        if (entry.Surface >= 0 && Exists(entry.Surface))
            Game.CallBuiltinTrusted("surface_free", default, default, entry.Surface);
        entry.Surface = -1;
    }
}
