using System.Diagnostics;
using System.Reflection;

namespace StoneForge.Loader;

/// <summary>Loads the mods in &lt;game&gt;\mods - each folder's C# compiled and checked (ModCompiler,
/// ModSecurity) into its own collectible load context - and switches them on and off while the game runs
/// (the Mods window): switched off, everything a mod registered is taken back (hooks, ITickables, DrawGui,
/// its UI, main menu buttons, its items - removed from the game) and its code unloaded; switched on, its folder
/// is compiled again (so edits to it show) and it's loaded.
/// At start the folders are compiled one after another on a background thread (pure Roslyn - nothing of the
/// game) while the game waits in its first room, its own loading held (LoadingScreen); each is loaded on the
/// game's thread at the start of the frame after it's ready, and the loading screen (LoadingScreen) shows how far it's got (<see cref="Startup"/>).</summary>
internal static class ModManager
{
    private sealed class Loaded
    {
        public required string Id;
        public required string Name;
        public required string Folder;
        public required IStoneMod Mod;
        public required ModLoadContext Context;
    }

    // A folder's mod.json (null: none, or invalid - then Result says why) and its source compiled.
    private sealed record Compiled(ModManifest? Manifest, ModCompiler.Result Result, long Milliseconds);

    private static readonly List<Loaded> Mods = new();
    // Switches asked for (the Mods window): done at the start of a frame, outside any of the game's events and
    // the loader's own handler lists - once start-up has finished.
    private static readonly Queue<(string Id, bool On)> Requests = new();
    private static readonly Queue<string> Faults = new();
    internal static void QueueFault(string id) => Faults.Enqueue(id);
    // Unloaded contexts, checked a few seconds on: really gone, or still held by something.
    private static readonly List<(string Name, WeakReference Context, int Frames)> Unloading = new();
    // Start-up: the folders, and their compiles (each after the one before).
    private static List<string> _folders = new();
    private static Task<Compiled>[] _compiles = Array.Empty<Task<Compiled>>();

    internal static string ModsDir => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!, "..", "mods"));

    /// <summary>How start-up loading is going (the loading screen shows it).</summary>
    internal static StartupProgress Startup { get; } = new();

    /// <summary>How many mods are loaded now.</summary>
    internal static int LoadedCount => Mods.Count;

    // At start: every mod folder, compiling in the background from now on (Frame loads each once it's ready).
    internal static void BeginLoadAll()
    {
        string modsDir = ModsDir;
        Directory.CreateDirectory(modsDir);
        _folders = Directory.GetDirectories(modsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        GmlRuntime.Snapshot();
        Game.Log($"{_folders.Count} mod folder(s) in {modsDir}");
        foreach (string folder in _folders)
            // (A mod's own build output - bin, obj, from editing it in an IDE - is expected and ignored; only a
            // trusted mod's own DLLs are loaded.)
            if (ModCompiler.Libraries(folder).Count > 0 && !IsTrusted(folder))
                Game.Log($"{Path.GetFileName(folder)}: has DLLs - ignored (mods are C# source; only a trusted mod's DLLs are loaded - mod.json \"trusted\": true)");
        Startup.Begin(_folders.Count);
        _compiles = new Task<Compiled>[_folders.Count];
        Task previous = Task.CompletedTask;
        for (int i = 0; i < _folders.Count; i++)
        {
            string folder = _folders[i];
            _compiles[i] = previous.ContinueWith(_ => CompileSource(folder), TaskScheduler.Default);
            previous = _compiles[i];
        }
        if (_folders.Count == 0)
            Startup.Finish(0, 0);
    }

    /// <summary>Switches a mod (by its ID) on or off from the next frame (and for the next start - ModRegistry).</summary>
    internal static void Request(string id, bool on) => Requests.Enqueue((id, on));

    // Each frame, first: start-up's compiled folders loaded, the switches asked for, the check on unloaded mods.
    internal static void Frame()
    {
        while (Faults.TryDequeue(out string? fault))
        {
            try { UIWindow.ShutMod(fault); }
            catch (Exception e) { Game.Log($"[{fault}] Closing faulted UI: {e.Message}"); }
            UITextBox.ReleaseFocus();
        }
        if (!Startup.Finished)
        {
            LoadCompiled();
            return;
        }
        while (Requests.Count > 0)
        {
            var (name, on) = Requests.Dequeue();
            try
            {
                if (on)
                    SwitchOn(name);
                else
                    SwitchOff(name);
            }
            catch (Exception e) { Game.Log($"Switching {name} {(on ? "on" : "off")} failed: {e}"); }
        }
        for (int i = Unloading.Count - 1; i >= 0; i--)
        {
            var (name, context, frames) = Unloading[i];
            if (++frames < 180)
            {
                Unloading[i] = (name, context, frames);
                continue;
            }
            Unloading.RemoveAt(i);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Game.Log(context.IsAlive
                ? $"{name}: its code is still in memory - something still refers to it (it's switched off all the same)"
                : $"{name}: its code is unloaded");
        }
    }

    // Start-up: the folders whose compile has finished, in order, loaded - in the game's first room, its own
    // loading held meanwhile (LoadingScreen). A mod's Load runs on the game's thread, so nothing is drawn while it
    // runs: one folder at a time, each named on the loading screen two frames before (what's on screen lags a
    // frame behind what's drawn), so a slow Load leaves its own name up. Nothing loads before the screen is up and
    // the game's resolution has settled (LoadingScreen.Ready - or, should it never be, 10 seconds on). (Nothing of
    // the game is touched before the 5th frame: the first four come while the runner is still starting, half a
    // second apart, and asset_get_index then crashes it.)
    private static int _frames;
    private static int _namedFrames;
    private static bool _loggedStart;
    private static Stopwatch? _sinceFifth;
    private static void LoadCompiled()
    {
        int done = Startup.Done;
        if (done < _folders.Count && _namedFrames == 0)
            Startup.Loading(Path.GetFileName(_folders[done]), done);
        if (++_frames < 5)
            return;
        _sinceFifth ??= Stopwatch.StartNew();
        if (!LoadingScreen.Ready && _sinceFifth.Elapsed.TotalSeconds < 10)
            return;
        if (!_loggedStart)
        {
            _loggedStart = true;
            Game.Log($"Loading mods from frame {_frames}" + (LoadingScreen.Ready ? "" : " - the loading screen isn't up (10 s on)"));
        }
        if (done < _folders.Count)
        {
            if (!_compiles[done].IsCompleted)
                return;
            // (Its name drawn - and on screen - first.)
            if (++_namedFrames <= 2)
                return;
            _namedFrames = 0;
            string folder = _folders[done];
            var timer = Stopwatch.StartNew();
            var compiled = _compiles[done].Result;
            var (context, mod) = Instantiate(folder, compiled);
            if (context == null || mod == null)
                Startup.Failed++;
            else
                LoadFolder(folder, compiled.Manifest!, context, mod);
            if (timer.Elapsed.TotalSeconds >= 1)
                Game.Log($"{Path.GetFileName(folder)}: loading took {timer.Elapsed.TotalSeconds:0.0} s - the game waits while a mod's Load runs");
            Startup.Done = ++done;
            return;
        }
        Startup.Finish(Mods.Count, Startup.Failed);
        Game.Log($"{Mods.Count} mod(s) loaded" + (Startup.Failed > 0 ? $", {Startup.Failed} folder(s) not" : ""));
    }

    // A folder's mod: its details registered (the Mods window), and loaded if it's switched on.
    private static void LoadFolder(string folder, ModManifest manifest, ModLoadContext context, IStoneMod mod)
    {
        string folderName = Path.GetFileName(folder);
        // (A second folder with an ID already taken isn't loaded.)
        if (ModRegistry.All.FirstOrDefault(m => m.Id == manifest.Id) is { } taken)
        {
            Game.Log($"{folderName}: mod ID \"{manifest.Id}\" is already {Path.GetFileName(taken.Folder)}'s - not loaded");
            ModRegistry.All.Add(Info(folder, manifest, false, $"Not loaded: mod ID \"{manifest.Id}\" is already used by {Path.GetFileName(taken.Folder)}", idSuffix: "@" + folderName));
            UnloadIfUnused(context, folderName);
            return;
        }
        bool enabled = ModRegistry.MayRun(manifest.Id, manifest.Trusted);
        ModRegistry.All.Add(Info(folder, manifest, enabled));
        if (enabled)
            Start(manifest, folder, mod, context);
        else if (manifest.Trusted && !ModRegistry.Disabled.Contains(manifest.Id))
            Game.Log($"{manifest.Name} {manifest.Version} asks for full access (trusted) - not loaded until it's allowed in the Mods window");
        else
            Game.Log($"{manifest.Name} {manifest.Version} is switched off (Mods window) - not loaded");
        UnloadIfUnused(context, folderName);
    }

    private static ModInfo Info(string folder, ModManifest manifest, bool enabled, string? error = null, string idSuffix = "")
        => new(manifest.Id + idSuffix, manifest.Name, manifest.Description, manifest.Author, manifest.Version, enabled, folder,
            error, ContainsGml: GmlRuntime.ContainsGml(folder), Trusted: manifest.Trusted);

    private static void SwitchOn(string id)
    {
        if (Hooks.IsSuspended(id)) SwitchOff(id);
        if (Mods.Any(m => m.Id == id))
            return;
        var info = ModRegistry.All.FirstOrDefault(m => m.Id == id);
        if (info != null && info.Trusted && !ModRegistry.Allowed.Contains(id))
        {
            Game.Log($"{info.Name}: asks for full access - allow it in the Mods window first");
            return;
        }
        if (info == null || info.Error != null)
        {
            Game.Log($"{info?.Name ?? id}: can't be switched on{(info?.Error != null ? " - " + info.Error : "")}");
            return;
        }
        var timer = Stopwatch.StartNew();
        var compiled = CompileSource(info.Folder);
        var (context, mod) = Instantiate(info.Folder, compiled);
        if (context == null || mod == null)
            return;
        if (compiled.Manifest!.Id != id)
            Game.Log($"{info.Name}: its mod.json's id is now \"{compiled.Manifest.Id}\" - restart the game to load it");
        else
        {
            Start(compiled.Manifest, info.Folder, mod, context);
            ModRegistry.Update(id, enabled: true);
            Game.Log($"{info.Name} switched on ({timer.ElapsedMilliseconds} ms)");
        }
        UnloadIfUnused(context, info.Name);
    }

    private static void SwitchOff(string id)
    {
        var loaded = Mods.FirstOrDefault(m => m.Id == id);
        ModRegistry.Update(id, enabled: false);
        if (loaded == null)
            return;
        try { loaded.Mod.Unload(); }
        catch (Exception e) { Game.Log($"{loaded.Name}: Unload threw: {e}"); }
        TakeBack(id);
        Mods.Remove(loaded);
        UnloadIfUnused(loaded.Context, loaded.Name);
        Game.Log($"{loaded.Name} switched off");
    }

    // Everything a mod registered, by its ID: hooks, handlers, its UI and windows, main menu buttons, its items.
    private static void TakeBack(string id)
    {
        string name = id;
        // One failing cleanup must not strand every later resource or prevent the load context unloading.
        Action[] cleanup = {
            () => GameObjects.RemoveMod(name), () => GmlScripts.RemoveMod(name),
            () => Hooks.RemoveMod(name), () => MainMenu.RemoveMod(name), () => Items.RemoveMod(name),
            () => Consumables.RemoveMod(name), () => Skills.RemoveMod(name), () => Buffs.RemoveMod(name),
            () => UIWindow.ShutMod(name), () => ModSettings.RemoveMod(name), UITextBox.ReleaseFocus,
            () => ModContent.RemoveMod(name), () => Hooks.ResetFault(name)
        };
        foreach (var release in cleanup)
            try { release(); }
            catch (Exception e) { Game.Log($"[{name}] Cleanup failed: {e.Message}"); }
    }

    private static void Start(ModManifest manifest, string folder, IStoneMod mod, ModLoadContext context)
    {
        string id = manifest.Id, name = manifest.Name;
        try
        {
            Hooks.ResetFault(id);
            ModRegistry.SetFault(id, null);
            var modContext = new ModContext(manifest, folder);
            GmlRuntime.Activate(folder, id);
            if (manifest.Trusted) Game.Log($"WARNING: {name}: {ModsWindow.TrustedWarning}");
            if (GmlRuntime.ContainsGml(folder)) Game.Log($"WARNING: {name}: {ModsWindow.GmlWarning}");
            mod.Load(modContext);
            Hooks.Mods.Add((id, mod));
            if (mod is ITickable tickable)
                modContext.AddTickable(tickable);
            Mods.Add(new Loaded { Id = id, Name = name, Folder = folder, Mod = mod, Context = context });
            Game.Log($"Loaded {name} ({id}) {manifest.Version}" + (manifest.Author.Length > 0 ? $" by {manifest.Author}" : "")
                + (manifest.Description.Length > 0 ? $" - {manifest.Description}" : ""));
        }
        catch (Exception e)
        {
            Game.Log($"{name} failed to load: {e}");
            // (What it registered before failing goes.)
            TakeBack(id);
            ModRegistry.SetFault(id, "Load failed: " + e.Message);
        }
    }

    // A folder's source compiled and checked - on any thread: nothing of the game is touched.
    // (Its mod.json first: without a valid one - or needing a newer StoneForge - it isn't compiled.)
    private static Compiled CompileSource(string folder)
    {
        var timer = Stopwatch.StartNew();
        ModManifest manifest;
        try
        {
            manifest = ModManifest.Read(folder);
            if (manifest.StoneForge != null && !ModIdentity.Satisfies(LoaderVersion.Text, manifest.StoneForge))
                return new Compiled(manifest, new ModCompiler.Result(null, new List<string> {
                    $"needs StoneForge {manifest.StoneForge} or newer (this is {LoaderVersion.Text})" }), 0);
        }
        catch (Exception e) { return new Compiled(null, new ModCompiler.Result(null, new List<string> { e.Message }), 0); }
        ModCompiler.Result result;
        try { result = ModCompiler.Compile(folder, manifest.Trusted); }
        catch (Exception e) { result = new ModCompiler.Result(null, new List<string> { e.Message }); }
        return new Compiled(manifest, result, Math.Max(1, timer.ElapsedMilliseconds));
    }

    // A compiled folder loaded into a new context, with its IStoneMod class made (on the game's thread) - one per
    // folder; the Mods window's entry says why when it didn't load.
    private static (ModLoadContext? Context, IStoneMod? Mod) Instantiate(string folder, Compiled compiled)
    {
        string folderName = Path.GetFileName(folder);
        void Failed(string why, IEnumerable<string> details)
        {
            Game.Log($"{folderName}: not loaded - {why}" + string.Concat(details.Select(d => "\n    " + d)));
            if (!ModRegistry.All.Any(m => m.Folder == folder))
            {
                var m = compiled.Manifest;
                ModRegistry.All.Add(new ModInfo(m?.Id ?? folderName, m?.Name ?? folderName, m?.Description ?? "", m?.Author ?? "", m?.Version ?? "?",
                    !ModRegistry.Disabled.Contains(m?.Id ?? folderName), folder,
                    "Not loaded: " + why + string.Concat(details.Take(6).Select(d => "\n" + d)),
                    ContainsGml: GmlRuntime.ContainsGml(folder), Trusted: m?.Trusted ?? false));
            }
        }
        // (No compile happened: a missing or invalid mod.json, or a StoneForge too old for it.)
        if (compiled.Manifest == null || compiled.Milliseconds == 0)
        {
            Failed(compiled.Result.Errors.FirstOrDefault() ?? "no mod.json", Array.Empty<string>());
            return (null, null);
        }
        if (compiled.Result.Assembly == null)
        {
            Failed("it doesn't compile", compiled.Result.Errors);
            return (null, null);
        }
        Game.Log($"{folderName}: compiled and checked in {compiled.Milliseconds} ms");
        var context = new ModLoadContext(folderName, compiled.Manifest.Trusted ? ModCompiler.Libraries(folder) : null);
        try
        {
            Assembly assembly = context.LoadFromStream(new MemoryStream(compiled.Result.Assembly));
            var types = assembly.GetExportedTypes().Where(t => !t.IsAbstract && typeof(IStoneMod).IsAssignableFrom(t)).ToList();
            if (types.Count != 1)
            {
                Failed(types.Count == 0 ? "it has no public IStoneMod class" : "it has more than one IStoneMod class ("
                    + string.Join(", ", types.Select(t => t.Name)) + ") - one mod per folder", Array.Empty<string>());
                context.Unload();
                return (null, null);
            }
            return (context, (IStoneMod)Activator.CreateInstance(types[0])!);
        }
        catch (Exception e)
        {
            Failed("couldn't load: " + (e is TargetInvocationException { InnerException: { } inner } ? inner.Message : e.Message), Array.Empty<string>());
            context.Unload();
            return (null, null);
        }
    }

    // Whether a folder's mod.json asks for full access (false: it doesn't, or it has no valid one).
    private static bool IsTrusted(string folder)
    {
        try { return ModManifest.Read(folder).Trusted; }
        catch { return false; }
    }

    // A context no loaded mod uses any more (its mods switched off, or never on): unloaded.
    private static void UnloadIfUnused(ModLoadContext context, string name)
    {
        if (Mods.Any(m => m.Context == context))
            return;
        context.Unload();
        Unloading.Add((name, new WeakReference(context), 0));
    }
}
