namespace StoneForge;

// UIElement.ClipChildren: the children drawn into a surface the size of the clipped area (GUI pixels, at the
// game's UI scale), drawn back in its place - as the game clips its own scrolled lists (it has no scissor on this
// runtime). Begin switches to the surface and End back, keeping the Draw GUI pass's matrices (nested clips stack). A
// surface not used for a while (its element gone or hidden) is freed.
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
    // The matrices each Begin found (view, projection, world: matrix_get 0, 1, 2), put back by its End.
    private static readonly Stack<GmValue[]> Matrices = new();
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
        Matrices.Push(new[] { Builtin("matrix_get", 0), Builtin("matrix_get", 1), Builtin("matrix_get", 2) });
        Builtin("surface_set_target", entry.Surface);
        // (Cleared: c_black, transparent.)
        Builtin("draw_clear_alpha", 0, 0);
        using (GmArray? world = Builtin("matrix_build", -x, -y, 0, 0, 0, 0, scale, scale, 1).AsArray)
            Builtin("matrix_set", 2, world);
        return true;
    }

    internal static void End(UIElement element)
    {
        var entry = Surfaces[element];
        Builtin("surface_reset_target");
        if (Matrices.TryPop(out var matrices))
            for (int i = 0; i < 3; i++)
            {
                Builtin("matrix_set", i, matrices[i]);
                matrices[i].AsArray?.Dispose();
            }
        // (Back at the pass's scale, which it's drawn under: shrunk by it. 16777215: c_white.)
        double scale = Draw.Scale;
        Builtin("draw_surface_ext", entry.Surface, entry.X, entry.Y, 1 / scale, 1 / scale, 0, 16777215, 1);
    }

    private static GmValue Builtin(string name, params GmValue[] args) => Game.CallBuiltinTrusted(name, default, default, args);

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
