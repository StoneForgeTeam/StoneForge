using System.Diagnostics;

namespace StoneForge;

/// <summary>Where the frame goes: an overlay (Ctrl+Shift+P) with the frame rate and, mod by mod, how long its code
/// takes each frame - its Tick, Frame and Draw GUI handlers, its script and code hooks, its objects' events - on average
/// and at worst over the last second, and how often it runs. StoneForge times all of that itself; a mod can also time its
/// own parts (<see cref="Measure(ModContext, string, Action)"/>), listed under it. Nothing is timed while the overlay is
/// off.</summary>
public static class Profiler
{
    // (Averages over this long - tests: 0, every frame; a mod's parts listed at most.)
    internal static double WindowSeconds = 1;
    private const int PartsPerMod = 10;

    private readonly record struct Key(string Mod, string Name, bool Section);
    private sealed class Totals
    {
        public double Ms, Max;
        public int Calls;
    }

    /// <summary>A part's timing over the last second: per frame on average (ms), its worst frame (ms), and how many times
    /// it ran per frame.</summary>
    public readonly record struct Timing(string Mod, string Name, bool Section, double Average, double Worst, double CallsPerFrame);

    private static readonly Dictionary<Key, (double Ms, int Calls)> ThisFrame = new();
    private static readonly Dictionary<Key, Totals> ThisWindow = new();
    private static readonly Stopwatch Window = Stopwatch.StartNew();
    private static readonly Stopwatch FrameClock = Stopwatch.StartNew();
    private static int _frames;
    private static double _worstFrame;

    /// <summary>Whether the overlay shows (and timing is on). Ctrl+Shift+P switches it.</summary>
    public static bool Visible { get; set; }

    /// <summary>The last second's timings, slowest first (empty while the overlay's off).</summary>
    public static IReadOnlyList<Timing> Timings { get; private set; } = Array.Empty<Timing>();

    /// <summary>The frame rate over the last second, and its slowest frame (ms).</summary>
    public static double Fps { get; private set; }
    public static double WorstFrameMs { get; private set; }

    /// <summary>Times <paramref name="work"/> as a part of this mod's, by <paramref name="name"/> ("loot sync"), listed
    /// under the mod in the overlay. Only while it shows; the work runs either way.</summary>
    public static void Measure(ModContext context, string name, Action work)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(work);
        if (!Visible)
        {
            work();
            return;
        }
        long start = Stopwatch.GetTimestamp();
        try { work(); }
        finally { Record(context.Id, name, section: true, start); }
    }

    /// <inheritdoc cref="Measure(ModContext, string, Action)"/>
    public static T Measure<T>(ModContext context, string name, Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        T result = default!;
        Measure(context, name, () => { result = work(); });
        return result;
    }

    // The time since start, as a mod's handler (where it was called from: "Tick", a hook's code name...) or a part it
    // timed itself.
    internal static void Record(string mod, string name, bool section, long start)
    {
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var key = new Key(mod, name, section);
        var (total, calls) = ThisFrame.GetValueOrDefault(key);
        ThisFrame[key] = (total + ms, calls + 1);
    }

    // The start of a frame (the loader's): the hotkey; the last frame's times into the second's; each second, the
    // averages published.
    internal static void NewFrame()
    {
        if (Keyboard.Down(Keyboard.Control) && Keyboard.Down(Keyboard.Shift) && Keyboard.Pressed('P'))
        {
            Visible = !Visible;
            Reset();
        }
        if (!Visible)
            return;
        double frameMs = FrameClock.Elapsed.TotalMilliseconds;
        FrameClock.Restart();
        _worstFrame = Math.Max(_worstFrame, frameMs);
        _frames++;
        foreach (var (key, (ms, calls)) in ThisFrame)
        {
            if (!ThisWindow.TryGetValue(key, out var totals))
                ThisWindow[key] = totals = new Totals();
            totals.Ms += ms;
            totals.Max = Math.Max(totals.Max, ms);
            totals.Calls += calls;
        }
        ThisFrame.Clear();
        double seconds = Window.Elapsed.TotalSeconds;
        if (seconds < WindowSeconds)
            return;
        Fps = _frames / seconds;
        WorstFrameMs = _worstFrame;
        Timings = ThisWindow.Select(p => new Timing(p.Key.Mod, p.Key.Name, p.Key.Section, p.Value.Ms / _frames, p.Value.Max,
                p.Value.Calls / (double)_frames))
            .OrderByDescending(t => t.Average).ToArray();
        ThisWindow.Clear();
        _frames = 0;
        _worstFrame = 0;
        Window.Restart();
    }

    private static void Reset()
    {
        ThisFrame.Clear();
        ThisWindow.Clear();
        Timings = Array.Empty<Timing>();
        Fps = WorstFrameMs = 0;
        _frames = 0;
        _worstFrame = 0;
        Window.Restart();
        FrameClock.Restart();
    }

    // The overlay, in the Draw GUI pass (over everything, mod windows too).
    internal static void DrawOverlay()
    {
        if (!Visible)
            return;
        const double x = 4, lineHeight = 9, width = 300;
        var lines = new List<(string Text, string Ms, string Worst, string Calls, int Colour)>();
        var mods = Timings.GroupBy(t => t.Mod)
            .Select(g => (Mod: g.Key, Total: g.Where(t => !t.Section).Sum(t => t.Average), Parts: g.ToList()))
            .OrderByDescending(m => m.Total).ToList();
        foreach (var (mod, total, parts) in mods)
        {
            lines.Add((mod, Ms(total), "", "", Colour(total, 2, 0.5, Draw.White)));
            foreach (var part in parts.Take(PartsPerMod))
                lines.Add(((part.Section ? "   - " : "   ") + Short(part.Name), Ms(part.Average), Ms(part.Worst), $"{part.CallsPerFrame:0.#}/f",
                    Colour(part.Average, 1, 0.25, Draw.Muted)));
            if (parts.Count > PartsPerMod)
                lines.Add(($"   ... {parts.Count - PartsPerMod} more", "", "", "", Draw.Muted));
        }
        double modsTotal = mods.Sum(m => m.Total);
        double height = 4 + lineHeight * (lines.Count + 2) + 4;
        Draw.Panel(x, 4, width, height, alpha: 0.85);
        double y = 8;
        string header = Fps > 0
            ? $"StoneForge profiler - {Fps:0} fps (worst frame {WorstFrameMs:0} ms) - mods {modsTotal:0.00} ms/frame"
            : "StoneForge profiler - measuring...";
        Draw.Text(x + 4, y, header, Draw.White);
        y += lineHeight;
        Draw.Text(x + 4, y, "per frame: average", Draw.Muted);
        Draw.Text(x + width - 100, y, "avg", Draw.Muted, Draw.AlignRight);
        Draw.Text(x + width - 52, y, "worst", Draw.Muted, Draw.AlignRight);
        Draw.Text(x + width - 6, y, "runs", Draw.Muted, Draw.AlignRight);
        y += lineHeight;
        foreach (var (text, ms, worst, calls, colour) in lines)
        {
            Draw.Text(x + 4, y, text, colour);
            Draw.Text(x + width - 100, y, ms, colour, Draw.AlignRight);
            Draw.Text(x + width - 52, y, worst, colour, Draw.AlignRight);
            Draw.Text(x + width - 6, y, calls, colour, Draw.AlignRight);
            y += lineHeight;
        }
    }

    private static string Ms(double ms) => $"{ms:0.00} ms";

    // Red over a frame's worth of slow, yellow over a little, else the line's own colour.
    private static int Colour(double ms, double slow, double warn, int otherwise)
        => ms >= slow ? Draw.Rgb(235, 90, 70) : ms >= warn ? Draw.Rgb(230, 200, 90) : otherwise;

    // A part's name, shorter: an object event's code name without its "gml_Object_", cut to fit.
    private static string Short(string name)
    {
        if (name.StartsWith("gml_Object_", StringComparison.Ordinal))
            name = name["gml_Object_".Length..];
        return name.Length > 44 ? name[..43] + "..." : name;
    }
}
