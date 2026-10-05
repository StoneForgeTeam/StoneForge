using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace StoneForge;

// Where the game calls in: Initialize (from StoneForge.Bridge, once), then every frame and around each code entry
// a mod subscribed to. Exceptions never cross back into the game: each handler is caught on its own.
internal static unsafe class Hooks
{
    internal static Action<string, string>? Faulted;
    internal static int FailureThreshold { get; set; } = 3;
    private readonly struct FailureKey(string mod, string where, object? source) : IEquatable<FailureKey>
    {
        internal string Mod => mod;
        public bool Equals(FailureKey other) => mod == other.Mod && where == other.Where && ReferenceEquals(source, other.Source);
        private string Where => where;
        private object? Source => source;
        public override bool Equals(object? value) => value is FailureKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(mod, where, source == null ? 0 : RuntimeHelpers.GetHashCode(source));
    }
    private static readonly Dictionary<FailureKey, int> Failures = new();
    private static readonly HashSet<string> Suspended = new();
    internal static bool IsSuspended(string mod) => Suspended.Contains(mod);
    internal static void ResetFault(string mod)
    {
        Suspended.Remove(mod);
        foreach (var key in Failures.Keys.Where(k => k.Mod == mod).ToList()) Failures.Remove(key);
    }

    internal static bool Invoke(string mod, string where, Func<bool> handler, object? source = null)
    {
        if (Suspended.Contains(mod)) return false;
        // (Timed while the profiler shows: every mod handler comes through here.)
        long start = Profiler.Visible ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        try
        {
            bool result = handler();
            Failures.Remove(new FailureKey(mod, where, source));
            return result;
        }
        catch (Exception e) { Fail(mod, where, e, source); return false; }
        finally
        {
            if (start != 0)
                Profiler.Record(mod, where, section: false, start);
        }
    }

    private static void BoundaryFailure(string where, Exception e)
    {
        // Never let even logging failures escape an UnmanagedCallersOnly entry point.
        try { Fail("StoneForge", where, e); } catch { }
    }

    [UnmanagedCallersOnly]
    internal static void OnFrame()
    {
        try { Game.MarkGameThread(); using var lease = new CallbackLifetime(); FrameCore(); }
        catch (Exception e) { BoundaryFailure("frame boundary", e); }
    }

    [UnmanagedCallersOnly]
    internal static int OnCodeBefore(byte* name, IntPtr self, IntPtr other)
    {
        try { Game.MarkGameThread(); using var lease = new CallbackLifetime(); return CodeBeforeCore(name, self, other); }
        catch (Exception e) { BoundaryFailure("before boundary", e); return 0; }
    }

    [UnmanagedCallersOnly]
    internal static void OnCodeAfter(byte* name, IntPtr self, IntPtr other)
    {
        try { Game.MarkGameThread(); using var lease = new CallbackLifetime(); CodeAfterCore(name, self, other); }
        catch (Exception e) { BoundaryFailure("after boundary", e); }
    }

    [UnmanagedCallersOnly]
    internal static int OnScript(byte* name, IntPtr self, IntPtr other, NValue* args, int count, NValue* result)
    {
        try { Game.MarkGameThread(); using var lease = new CallbackLifetime(); return ScriptCore(name, self, other, args, count, result); }
        catch (Exception e) { BoundaryFailure("script boundary", e); return 0; }
    }
    internal static readonly List<(string Mod, Action Handler)> FrameHandlers = new();
    internal static readonly List<(string Mod, Action Handler)> DrawGuiHandlers = new();
    internal static readonly List<(string Mod, Action Handler)> DrawHudHandlers = new();
    // Mods' ITickables (the mod classes that are, and whatever they added): ticked every frame.
    internal static readonly List<(string Mod, ITickable Tickable)> Tickables = new();
    // The loaded mods, and the clock the ITickables' delta time comes from.
    internal static readonly List<(string Name, IStoneMod Mod)> Mods = new();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _lastFrame;
    private static readonly Dictionary<string, List<(string Mod, Func<Instance, Instance, bool>? Before, Action<Instance, Instance>? After, int Order)>> Code = new();
    // Code names by the engine's pointer to them (stable for the game's lifetime): no string per call.
    private static readonly Dictionary<IntPtr, string> Names = new();

    internal static void Add(string mod, string codeName, Func<Instance, Instance, bool>? before, Action<Instance, Instance>? after,
        int order = HookOrder.Normal)
    {
        if (!Code.TryGetValue(codeName, out var list))
        {
            list = new();
            Code[codeName] = list;
            byte* p = Game.Utf8(codeName);
            try { Game.Api->HookCode(p); }
            finally { NativeMemory.Free(p); }
        }
        list.Insert(Place(list.Select(h => h.Order), order), (mod, before, after, order));
    }

    // Where a hook of this order goes among a name's: after every one of its order or earlier - by order, then in the
    // order they were added.
    private static int Place(IEnumerable<int> orders, int order)
    {
        int at = 0, i = 0;
        foreach (var existing in orders)
        {
            i++;
            if (existing <= order)
                at = i;
        }
        return at;
    }

    // ---- two mods replacing the same call ----

    /// <summary>A conflict found (once per name and pair of mods): the loader shows it in the Mods window.</summary>
    internal static Action<HookConflict>? Conflicted;
    private static readonly HashSet<(string Name, string Earlier, string Winner)> Conflicts = new();

    // Another mod's before hook replaced (skipped) a call one had already: said once.
    private static void Conflict(string name, bool script, string earlier, string winner)
    {
        if (!Conflicts.Add((name, earlier, winner)))
            return;
        var conflict = new HookConflict(name, script, earlier, winner);
        Game.Log("Hook conflict: " + conflict.Describe(mod => mod));
        Conflicted?.Invoke(conflict);
    }

    /// <summary>The conflicts found so far.</summary>
    internal static IReadOnlyCollection<(string Name, string Earlier, string Winner)> ConflictsFound => Conflicts;

    // (Tests: none found yet.)
    internal static void ResetConflictsForTests() => Conflicts.Clear();

    /// <summary>Where mods might conflict: two or more of them with before hooks on the same script or code entry at the
    /// same order (StoneForge's own aside), by name.</summary>
    internal static List<HookOverlap> Overlaps()
    {
        var overlaps = new List<HookOverlap>();
        void Find(string name, bool script, IEnumerable<(string Mod, int Order)> befores)
        {
            foreach (var group in befores.Where(b => b.Mod != LoaderId).GroupBy(b => b.Order))
            {
                var mods = group.Select(b => b.Mod).Distinct().ToList();
                if (mods.Count > 1)
                    overlaps.Add(new HookOverlap(name, script, group.Key, mods));
            }
        }
        foreach (var (name, list) in Scripts)
            Find(name, true, list.Where(h => h.Before != null).Select(h => (h.Mod, h.Order)));
        foreach (var (name, list) in Code)
            Find(name, false, list.Where(h => h.Before != null).Select(h => (h.Mod, h.Order)));
        return overlaps.OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
    }

    private static string Name(byte* p)
    {
        if (!Names.TryGetValue((IntPtr)p, out var name))
        {
            name = Game.FromUtf8(p);
            Names[(IntPtr)p] = name;
        }
        return name;
    }

    // Hooked scripts: handlers by script name. Each one's global.__smh_<name> flag (StoneModHooks' block
    // checks it) is set when the first handler arrives, and set again about once a second in case the game
    // ever clears globals.
    private static readonly Dictionary<string, List<(string Mod, Func<ScriptCall, bool>? Before, Action<ScriptCall>? After, int Order)>> Scripts = new();
    // A script being called as the game's own version (CallOriginal): its hook block's call into C# is let through
    // once - the call's own, the first thing its body does - so calls it makes in turn are hooked as ever.
    private static string? _passThrough;
    // (Starts due: the first frame sets the flags.)
    private static int _flagTimer = 60;
    private static List<IntPtr> _resultStrings = new();

    // The scripts the game data makes hookable - dotnet\stoneforge-hooks.txt, the patcher's list of those it hooked
    // (the loader's, and every mod's [assembly: HookScript]). Null: not known (an install from before it was written),
    // so not checked.
    internal static HashSet<string>? Hookable;

    internal static void LoadHookable(string path)
    {
        try
        {
            Hookable = File.Exists(path)
                ? new HashSet<string>(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.Ordinal)
                : null;
        }
        catch (Exception e)
        {
            Hookable = null;
            Game.Log($"Couldn't read the hookable scripts ({path}): {e.Message} - hooks aren't checked");
        }
    }

    // (StoneForge's own scripts - scr_stonemod_* - call into C# themselves: only mods' hooks are checked.)
    internal const string LoaderId = "StoneForge";

    internal static void AddScript(string mod, string scriptName, Func<ScriptCall, bool>? before, Action<ScriptCall>? after = null,
        int order = HookOrder.Normal)
    {
        if (before == null && after == null)
            throw new ArgumentException("A script hook needs a before or an after handler.");
        // A hook on a script the game data doesn't hook would never be called: said at once, not left silent.
        if (mod != LoaderId && Hookable != null && !Hookable.Contains(scriptName))
            throw new ArgumentException($"{scriptName} isn't hookable: add [assembly: HookScript(nameof(Scripts.{scriptName}))] to the mod "
                + "(any of its files), then restart the game - the game data is rebuilt with it hookable.", nameof(scriptName));
        if (!Scripts.TryGetValue(scriptName, out var list))
        {
            list = new();
            Scripts[scriptName] = list;
            // (While mods load the game isn't running: the first frame sets it - AssertScriptFlags.)
            if (Game.Running)
                Game.Global["__smh_" + scriptName] = true;
        }
        list.Insert(Place(list.Select(h => h.Order), order), (mod, before, after, order));
    }

    /// <summary>Calls a script's own code, skipping its hooks for this call only (not for the calls it makes, a
    /// recursive one included), with these self, other and arguments.</summary>
    internal static GmValue CallOriginal(string name, Instance self, Instance other, GmValue[] args)
    {
        _passThrough = name;
        // (Its self and other as the game lent them: a thrown item's turn runs as it's destroyed, say.)
        try { return Game.CallScript(name, self, other, args, lent: true); }
        // (Let go of if it wasn't used: the script isn't hooked in the game data, or its flag is off.)
        finally { _passThrough = null; }
    }

    // A mod switched off: all its handlers go. A script nothing hooks any more has its flag cleared, so the
    // game stops calling into us for it. (Code entries stay hooked natively: with no handler they cost a lookup.)
    internal static void RemoveMod(string mod)
    {
        FrameHandlers.RemoveAll(h => h.Mod == mod);
        DrawGuiHandlers.RemoveAll(h => h.Mod == mod);
        DrawHudHandlers.RemoveAll(h => h.Mod == mod);
        Tickables.RemoveAll(t => t.Mod == mod);
        Mods.RemoveAll(m => m.Name == mod);
        foreach (var list in Code.Values)
            list.RemoveAll(h => h.Mod == mod);
        foreach (var (name, list) in Scripts.ToList())
        {
            list.RemoveAll(h => h.Mod == mod);
            if (list.Count == 0)
            {
                Scripts.Remove(name);
                if (Game.Running)
                    Game.Global["__smh_" + name] = false;
            }
        }
    }

    private static void AssertScriptFlags()
    {
        if (Scripts.Count == 0 || ++_flagTimer < 60)
            return;
        _flagTimer = 0;
        foreach (string name in Scripts.Keys)
            Game.Global["__smh_" + name] = true;
    }

    private static int ScriptCore(byte* scriptName, IntPtr self, IntPtr other, NValue* args, int argCount, NValue* result)
    {
        Game.Running = true;
        var values = new GmValue[argCount];
        for (int i = 0; i < argCount; i++)
            values[i] = Game.FromNative(args[i]);
        if (!ScriptCalled(Game.FromUtf8(scriptName), new Instance(self), new Instance(other), values, out GmValue value))
            return 0;
        // (A string result has to outlive this call - the native side copies it into the game's value right
        // after - so it's kept until the next replaced call, then freed.)
        var strings = new List<IntPtr>();
        *result = Game.ToNative(value, strings);
        foreach (var old in _resultStrings)
            NativeMemory.Free((void*)old);
        _resultStrings = strings;
        return 1;
    }

    /// <summary>A hooked script's block calling in, at the start of its body: its handlers run. True: the call is
    /// replaced - the script returns <paramref name="result"/> without running its own code (done here already, for
    /// after handlers, or not at all).</summary>
    internal static bool ScriptCalled(string name, Instance self, Instance other, GmValue[] args, out GmValue result)
    {
        result = GmValue.Undefined;
        if (name == _passThrough)
        {
            _passThrough = null;
            return false;
        }
        if (!Scripts.TryGetValue(name, out var list))
            return false;
        var call = new ScriptCall(name, self, other, args);
        bool replace = false;
        // (Which mod's before hook replaced it - another's doing it too is a conflict.)
        string? replacedBy = null;
        var handlers = list.ToArray();
        foreach (var (mod, before, _, _) in handlers)
        {
            if (before == null || !Invoke(mod, name + " (script)", () => before(call), before))
                continue;
            if (replacedBy != null && replacedBy != mod)
                Conflict(name, script: true, replacedBy, mod);
            replacedBy = mod;
            replace = true;
        }
        // (After handlers: the call is made here - the game's own version, unless a before handler replaced it - and
        // they see its result, and can change it.)
        if (handlers.Any(h => h.After != null && !IsSuspended(h.Mod)))
        {
            if (!replace)
            {
                try { call.Result = CallOriginal(name, self, other, args); }
                catch (Exception e)
                {
                    Game.Log($"Calling {name} for its after hooks failed: {e.Message}");
                    return false;
                }
                replace = true;
            }
            foreach (var (mod, _, after, _) in handlers)
            {
                if (after != null)
                    Invoke(mod, name + " (script after)", () => { after(call); return false; }, after);
            }
        }
        result = call.Result;
        return replace;
    }

    private static void Fail(string mod, string where, Exception e, object? source = null)
    {
        var key = new FailureKey(mod, where, source);
        int count = Failures.GetValueOrDefault(key) + 1;
        Failures[key] = count;
        if (count == 1)
        {
            Game.Log($"[{mod}] {where} threw: {e}");
            ErrorWindows.ModFailed(mod, where, e);
        }
        if (count != Math.Max(1, FailureThreshold) || mod == "StoneForge") return;
        Suspended.Add(mod);
        string reason = $"{where} failed {count} consecutive times: {e.Message}";
        Game.Log($"[{mod}] Paused: {reason}. Reload it from the Mods window to retry.");
        Faulted?.Invoke(mod, reason);
    }

    // The game's Draw GUI pass (o_stonemod_gui, the loader's: always there, over everything).
    private static bool _drewOnce;
    internal static void DrawGui()
    {
        if (!_drewOnce)
        {
            _drewOnce = true;
            Game.Log($"Draw GUI pass running ({DrawGuiHandlers.Count} handler(s)), GUI {Game.CallBuiltin("display_get_gui_width")}x{Game.CallBuiltin("display_get_gui_height")}");
        }
        // (At the game's UI scale - Draw.Scale - and back to none after, for the game's cursor drawn next.)
        Draw.UpdateScale();
        bool scaled = Draw.Scale != 1;
        if (scaled)
            Game.CallScript("scr_stonemod_gui_matrix", default, Draw.Scale);
        try
        {
            for (int i = 0; i < DrawGuiHandlers.Count; i++)
            {
                var (mod, handler) = DrawGuiHandlers[i];
                Invoke(mod, "DrawGui", () => { handler(); return false; }, handler);
            }
            // (Mod windows over all of it - their screens and controls timed as StoneForge's UI.)
            long start = Profiler.Visible ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try { UIScreen.DrawWindows(); }
            catch (Exception e) { Game.Log("Drawing windows threw: " + e); }
            if (start != 0)
                Profiler.Record(LoaderId, "UI (screens and windows)", section: false, start);
            // (The profiler's overlay over everything.)
            try { Profiler.DrawOverlay(); }
            catch (Exception e) { Game.Log("Drawing the profiler threw: " + e); }
        }
        finally
        {
            if (scaled)
                Game.CallScript("scr_stonemod_gui_matrix", default, 1);
        }
        // (Every screen has said what it covers: the game's input kept off it.)
        InputBlock.Flush();
        Clip.Sweep();
    }

    // The mods' HUD pass (o_stonemod_hud's Draw, at HudDepth: with the game's HUD, under its windows). The game draws its
    // UI in the world's Draw pass, a UI unit to a world pixel, laid out from its visible base container
    // (global.guiBaseContainerVisible, at -5000, -5000 in the room): mods draw in the same UI coordinates as in Draw GUI,
    // moved there by the world matrix. What it draws the game's windows and bottom panel cover; what its screens cover
    // is reported for the input block as theirs (InputBlock.Flush, at the end of the Draw GUI pass).
    internal static void DrawHud()
    {
        if (DrawHudHandlers.Count == 0)
            return;
        Draw.UpdateScale();
        // (The game's GUI is laid out from its visible base container, off in the room at -5000, -5000 - put on screen
        // while the game draws its UI, from that container's depth on. Nothing to draw before it's made.)
        GmValue container = Game.Global["guiBaseContainerVisible"];
        Instance origin = container.Kind == GmKind.Instance ? container.AsInstance
            : container.Kind == GmKind.Real && container.AsInt >= 0 ? Instance.FromId(container.AsInt) : default;
        if (origin.IsNone || !origin.Exists)
            return;
        // (2: matrix_world.)
        using (GmArray? moved = Game.CallBuiltinTrusted("matrix_build", default, default, origin["x"], origin["y"], 0, 0, 0, 0, 1, 1, 1).AsArray)
            Game.CallBuiltinTrusted("matrix_set", default, default, 2, moved);
        try
        {
            for (int i = 0; i < DrawHudHandlers.Count; i++)
            {
                var (mod, handler) = DrawHudHandlers[i];
                Invoke(mod, "DrawHud", () => { handler(); return false; }, handler);
            }
        }
        finally
        {
            using GmArray? identity = Game.CallBuiltinTrusted("matrix_build_identity", default, default).AsArray;
            Game.CallBuiltinTrusted("matrix_set", default, default, 2, identity);
        }
    }

    /// <summary>The HUD pass's depth: in front of the world and the game's HUD bars, behind its windows (-12100, their
    /// parts just in front) and its bottom panel (-12150) - they draw after it, over it.</summary>
    internal const int HudDepth = -12050;

    // o_stonemod_gui (persistent): made on the 5th frame - the game's first real one, as early as it can be, for
    // the loading screen (the first four come while the runner is still starting, half a second apart, and
    // asset_get_index then crashes the game) - and made again should anything remove it.
    private static int _guiObject = -2, _hudObject = -2, _guiCheck = 25;
    internal static void KeepGuiObject()
    {
        if ((DrawGuiHandlers.Count == 0 && DrawHudHandlers.Count == 0 && !Profiler.Visible) || ++_guiCheck < 30)
            return;
        // (The HUD pass's object, when anything draws there - the Draw GUI pass's below still runs the input block.)
        if (DrawHudHandlers.Count > 0)
            KeepHudObject();
        _guiCheck = 0;
        if (_guiObject == -2)
        {
            _guiObject = Gm.AssetGetIndex("o_stonemod_gui");
            if (_guiObject < 0)
                Game.Log("No o_stonemod_gui in the game data - mods can't draw (run StoneForge.Patcher install)");
        }
        if (_guiObject < 0)
            return;
        if (!Game.CallBuiltin("instance_exists", _guiObject).AsBool)
        {
            // (Depth is its z: -16000 is where GameMaker's camera sits, which clips everything drawn there. -14000:
            // in front of the game's own menus, about -12500, and still in view.)
            GmValue made = Game.CallBuiltin("instance_create_depth", 0, 0, -14000, _guiObject);
            Game.Log($"Made the Draw GUI object (o_stonemod_gui {_guiObject}): {made}, in room {Gm.Room}");
        }
    }

    private static void KeepHudObject()
    {
        if (_hudObject == -2)
        {
            _hudObject = Gm.AssetGetIndex("o_stonemod_hud");
            if (_hudObject < 0)
                Game.Log("No o_stonemod_hud in the game data - mods can't draw on the HUD (run StoneForge.Patcher install)");
        }
        if (_hudObject >= 0 && !Game.CallBuiltin("instance_exists", _hudObject).AsBool)
        {
            GmValue made = Game.CallBuiltin("instance_create_depth", 0, 0, HudDepth, _hudObject);
            Game.Log($"Made the HUD object (o_stonemod_hud {_hudObject}): {made}, in room {Gm.Room}");
        }
    }

    // The loader's work at the start of each frame (switching mods on and off - StoneForge.Loader's ModManager):
    // done here, before any handler list is walked.
    internal static Action? BeforeFrame;

    // The game's frames since the loader started (caches good for one frame key on it: Culling).
    internal static long Frame { get; private set; }

    private static void FrameCore()
    {
        Frame++;
        Game.Running = true;
        // (Arrays and structs C# let go of since the last frame.)
        GmRef.ReleaseQueued();
        // (The profiler: its hotkey, and the last frame's times.)
        try { Profiler.NewFrame(); }
        catch (Exception e) { Game.Log("The profiler threw: " + e.Message); }
        BeforeFrame?.Invoke();
        AssertScriptFlags();
        KeepGuiObject();
        double now = Clock.Elapsed.TotalSeconds;
        double delta = now - _lastFrame;
        _lastFrame = now;
        for (int i = 0; i < Tickables.Count; i++)
        {
            var (name, tickable) = Tickables[i];
            Invoke(name, "Tick", () => { tickable.Tick(delta); return false; }, tickable);
        }
        for (int i = 0; i < FrameHandlers.Count; i++)
        {
            var (mod, handler) = FrameHandlers[i];
            Invoke(mod, "Frame", () => { handler(); return false; }, handler);
        }
    }

    private static int CodeBeforeCore(byte* codeName, IntPtr self, IntPtr other)
    {
        Game.Running = true;
        if (!Code.TryGetValue(Name(codeName), out var list))
            return 0;
        string? skippedBy = null;
        foreach (var (mod, before, _, _) in list.ToArray())
        {
            if (before == null || !Invoke(mod, Name(codeName) + " (before)", () => before(new Instance(self), new Instance(other)), before))
                continue;
            if (skippedBy != null && skippedBy != mod)
                Conflict(Name(codeName), script: false, skippedBy, mod);
            skippedBy = mod;
        }
        return skippedBy != null ? 1 : 0;
    }

    private static void CodeAfterCore(byte* codeName, IntPtr self, IntPtr other)
    {
        Game.Running = true;
        if (!Code.TryGetValue(Name(codeName), out var list))
            return;
        foreach (var (mod, _, after, _) in list.ToArray())
        {
            if (after == null)
                continue;
            Invoke(mod, Name(codeName) + " (after)", () => { after(new Instance(self), new Instance(other)); return false; }, after);
        }
    }
}
