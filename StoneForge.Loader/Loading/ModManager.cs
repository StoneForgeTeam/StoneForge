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
/// game's thread at the start of the frame after it's ready, and the loading screen (LoadingScreen) shows how far it's got (<see cref="Startup"/>).
/// They load in folder order, but each after the mods it requires and loads after (mod.json "requires", "after":
/// LoadOrder) - compiled against the ones it requires, and loaded only if they are. Switching a mod off switches off the
/// mods requiring it (their code uses its), and they come back with it; switching one on switches on what it requires.</summary>
internal static class ModManager
{
    private sealed class Loaded
    {
        public required string Id;
        public required string Name;
        public required string Folder;
        public required IStoneMod Mod;
        public required ModLoadContext Context;
        public required ModManifest Manifest;
        // Its code, as loaded and as compiled: what the mods requiring it are loaded with, and compiled against.
        public required Assembly Assembly;
        public required byte[] Image;
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
    private static List<ModManifest?> _manifests = new();
    private static Dictionary<string, string> _orderProblems = new();
    private static Task<Compiled>[] _compiles = Array.Empty<Task<Compiled>>();
    // The mods switched off because a mod they require was (by its ID): switched on again with it - unless they're
    // switched on (or it's switched on for them) first.
    private static readonly Dictionary<string, List<string>> OffWith = new();

    // Why a mod was switched off for a mod it requires (by its ID): its page says so.
    private static readonly Dictionary<string, string> OffBecause = new();

    /// <summary>Why a mod was switched off for a mod it requires - switched off with it, or that mod wasn't running when
    /// it would have started; null if it wasn't.</summary>
    internal static string? WhyOff(string id) => OffBecause.GetValueOrDefault(id);

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
        var folders = Directory.GetDirectories(modsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        // (In the order to load them: each after those it requires and loads after.)
        var manifests = folders.Select(TryManifest).ToList();
        var order = LoadOrder.Sort(manifests);
        _folders = order.Order.Select(i => folders[i]).ToList();
        _manifests = order.Order.Select(i => manifests[i]).ToList();
        _orderProblems = order.Problems.ToDictionary(p => folders[p.Key], p => p.Value);
        foreach (string warning in order.Warnings)
            Game.Log(warning);
        if (!_folders.SequenceEqual(folders))
            Game.Log("Load order (mod.json \"requires\", \"after\"): " + string.Join(", ", _folders.Select(Path.GetFileName)));
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
            int index = i;
            _compiles[i] = previous.ContinueWith(_ => CompileAtStart(index), TaskScheduler.Default);
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
        // (In the order they were unloaded: a mod's code is held by the mods requiring it until theirs goes - they're
        // unloaded first.)
        for (int i = 0; i < Unloading.Count; i++)
        {
            var (name, context, frames) = Unloading[i];
            if (++frames < 180)
            {
                Unloading[i] = (name, context, frames);
                continue;
            }
            Unloading.RemoveAt(i--);
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
            // (A mod it requires not running - switched off, or it failed: it's switched off too, saying why.)
            if (compiled.Manifest is { } manifest && compiled.Result.Assembly != null && Unmet(manifest) is { } unmet)
            {
                if (Register(folder, manifest))
                    SwitchOffFor(manifest.Id, manifest.Name, unmet);
            }
            else
            {
                var loaded = Instantiate(folder, compiled);
                if (loaded == null)
                    Startup.Failed++;
                else
                    LoadFolder(folder, loaded);
            }
            if (timer.Elapsed.TotalSeconds >= 1)
                Game.Log($"{Path.GetFileName(folder)}: loading took {timer.Elapsed.TotalSeconds:0.0} s - the game waits while a mod's Load runs");
            Startup.Done = ++done;
            return;
        }
        Startup.Finish(Mods.Count, Startup.Failed);
        Game.Log($"{Mods.Count} mod(s) loaded" + (Startup.Failed > 0 ? $", {Startup.Failed} folder(s) not" : ""));
        // (Where mods might conflict - the same calls hooked before they run at the same order: said once they're all in,
        // a line for each pair of mods.)
        var pairs = Hooks.Overlaps()
            .SelectMany(o => o.Mods.SelectMany(m => o.Mods.Where(n => string.CompareOrdinal(m, n) < 0).Select(n => (Pair: (m, n), o.Name))))
            .GroupBy(p => p.Pair);
        foreach (var pair in pairs)
        {
            var names = pair.Select(p => p.Name).Distinct().ToList();
            Game.Log($"Possible hook conflicts: {pair.Key.m} and {pair.Key.n} both hook {names.Count} call(s) before they run, at the same order "
                + $"({string.Join(", ", names.Take(8))}{(names.Count > 8 ? ", ..." : "")}) - a conflict only if both replace one");
        }
    }

    // A folder's mod: its details registered (the Mods window), and loaded if it's switched on.
    private static void LoadFolder(string folder, Loaded loaded)
    {
        if (Register(folder, loaded.Manifest))
            Start(loaded);
        UnloadIfUnused(loaded.Context, Path.GetFileName(folder));
    }

    // A folder's mod's details registered (the Mods window); whether it may start - not if a folder before it has its
    // ID, or it's switched off or not allowed (the log says so).
    private static bool Register(string folder, ModManifest manifest)
    {
        string folderName = Path.GetFileName(folder);
        // (A second folder with an ID already taken isn't loaded.)
        if (ModRegistry.All.FirstOrDefault(m => m.Id == manifest.Id) is { } taken)
        {
            Game.Log($"{folderName}: mod ID \"{manifest.Id}\" is already {Path.GetFileName(taken.Folder)}'s - not loaded");
            ModRegistry.All.Add(Info(folder, manifest, false, $"Not loaded: mod ID \"{manifest.Id}\" is already used by {Path.GetFileName(taken.Folder)}", idSuffix: "@" + folderName));
            return false;
        }
        bool enabled = ModRegistry.MayRun(manifest.Id, manifest.Trusted);
        ModRegistry.All.Add(Info(folder, manifest, enabled));
        if (enabled)
            return true;
        if (manifest.Trusted && !ModRegistry.Disabled.Contains(manifest.Id))
            Game.Log($"{manifest.Name} {manifest.Version} asks for full access (trusted) - not loaded until it's allowed in the Mods window");
        else
            Game.Log($"{manifest.Name} {manifest.Version} is switched off (Mods window) - not loaded");
        return false;
    }

    // The first mod it requires that isn't running, and why (null: they all are).
    private static (string Required, string Why)? Unmet(ModManifest manifest)
    {
        foreach (string id in manifest.Requires)
        {
            if (Mods.Any(m => m.Id == id))
                continue;
            var info = ModRegistry.All.FirstOrDefault(m => m.Id == id);
            return (id, info == null ? Missing(id)
                : $"needs {info.Name} ({id}), which " + (info.Error != null || info.RuntimeError != null ? "didn't load"
                    : info.Trusted && !ModRegistry.Allowed.Contains(id) ? "isn't allowed yet (it asks for full access)"
                    : "is switched off"));
        }
        return null;
    }

    // A mod whose required mod isn't running: switched off (for the next start too), its page saying why - and back on
    // with that mod, if it's there to be switched on.
    private static void SwitchOffFor(string id, string name, (string Required, string Why) unmet)
    {
        bool installed = ModRegistry.All.Any(m => m.Id == unmet.Required);
        Game.Log($"{name}: switched off - {unmet.Why}");
        ModRegistry.SetEnabled(id, false);
        ModRegistry.Update(id, enabled: false);
        OffBecause[id] = "Switched off: it " + unmet.Why + (installed ? $" - it comes back on with {NameOf(unmet.Required)}." : ".");
        if (installed)
            WaitFor(unmet.Required, id);
    }

    // A mod to switch back on with one it requires.
    private static void WaitFor(string required, string id)
    {
        if (!OffWith.TryGetValue(required, out var list))
            OffWith[required] = list = new();
        if (!list.Contains(id))
            list.Add(id);
    }

    private static string NameOf(string id) => ModRegistry.All.FirstOrDefault(m => m.Id == id)?.Name ?? id;

    /// <summary>The mods (their names) running now that require this one: switched off with it.</summary>
    internal static List<string> RequiredBy(string id) => Mods.Where(m => m.Manifest.Requires.Contains(id)).Select(m => m.Name).ToList();

    private static string Missing(string id) => $"needs the mod \"{id}\", which isn't installed (mod.json \"requires\") - put it in the mods folder";

    private static ModInfo Info(string folder, ModManifest manifest, bool enabled, string? error = null, string idSuffix = "")
        => new(manifest.Id + idSuffix, manifest.Name, manifest.Description, manifest.Author, manifest.Version, enabled, folder,
            error, ContainsGml: GmlRuntime.ContainsGml(folder), Trusted: manifest.Trusted, Requires: manifest.Requires);

    private static void SwitchOn(string id) => SwitchOn(id, new HashSet<string> { id });

    // (Visiting: the mods being switched on, so requires in a loop don't go round.)
    private static void SwitchOn(string id, HashSet<string> visiting)
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
        // The mods it requires first: switched on (for the next start too), if they can be.
        if (TryManifest(info.Folder) is { } manifest)
        {
            foreach (string required in manifest.Requires)
                if (!Mods.Any(m => m.Id == required) && ModRegistry.All.Any(m => m.Id == required) && visiting.Add(required))
                {
                    Game.Log($"{info.Name}: requires {required} - switching it on");
                    ModRegistry.SetEnabled(required, true);
                    SwitchOn(required, visiting);
                }
            if (Unmet(manifest) is { } unmet)
            {
                SwitchOffFor(id, info.Name, unmet);
                return;
            }
        }
        var timer = Stopwatch.StartNew();
        var compiled = CompileSource(info.Folder, required => Mods.FirstOrDefault(m => m.Id == required) is { } running
            ? (running.Manifest, running.Image)
            : (ModRegistry.All.FirstOrDefault(m => m.Id == required) is { } other ? TryManifest(other.Folder) : null, null), "isn't loaded");
        if (Instantiate(info.Folder, compiled) is not { } loaded)
            return;
        if (loaded.Manifest.Id != id)
            Game.Log($"{info.Name}: its mod.json's id is now \"{loaded.Manifest.Id}\" - restart the game to load it");
        else
        {
            Start(loaded);
            ModRegistry.Update(id, enabled: true);
            Game.Log($"{info.Name} switched on ({timer.ElapsedMilliseconds} ms)");
        }
        UnloadIfUnused(loaded.Context, info.Name);
        // (Back with it, on for the next start too: the mods that were switched off with it.)
        if (Mods.Any(m => m.Id == id) && OffWith.Remove(id, out var dependents))
            foreach (string dependent in dependents)
                if (visiting.Add(dependent))
                {
                    Game.Log($"{NameOf(dependent)}: switched back on with {info.Name}");
                    ModRegistry.SetEnabled(dependent, true);
                    SwitchOn(dependent, visiting);
                }
    }

    private static void SwitchOff(string id)
    {
        var loaded = Mods.FirstOrDefault(m => m.Id == id);
        ModRegistry.Update(id, enabled: false);
        // (Switched off itself: not waiting to come back with anything.)
        OffBecause.Remove(id);
        foreach (var waiting in OffWith.Values)
            waiting.Remove(id);
        if (loaded == null)
            return;
        // (First the mods that require it: their code uses its - off for the next start too, as the Mods window shows.
        // They're back when it is.)
        foreach (var dependent in Mods.Where(m => m.Manifest.Requires.Contains(id)).ToList())
        {
            Game.Log($"{dependent.Name}: switched off with {loaded.Name} (it requires it)");
            ModRegistry.SetEnabled(dependent.Id, false);
            SwitchOff(dependent.Id);
            OffBecause[dependent.Id] = $"Switched off with {loaded.Name}, which it requires - it comes back on with it.";
            WaitFor(id, dependent.Id);
        }
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
            () => Hooks.RemoveMod(name), () => ModList.RemoveMod(name), () => MainMenu.RemoveMod(name), () => EscMenu.RemoveMod(name), () => GameDialogs.RemoveMod(name), () => Items.RemoveMod(name),
            () => Consumables.RemoveMod(name), () => LootTables.RemoveMod(name), () => Skills.RemoveMod(name), () => Buffs.RemoveMod(name),
            () => UIWindow.ShutMod(name), () => ModSettings.RemoveMod(name), UITextBox.ReleaseFocus,
            () => ModContent.RemoveMod(name), () => Hooks.ResetFault(name)
        };
        foreach (var release in cleanup)
            try { release(); }
            catch (Exception e) { Game.Log($"[{name}] Cleanup failed: {e.Message}"); }
    }

    private static void Start(Loaded loaded)
    {
        var (manifest, folder, mod) = (loaded.Manifest, loaded.Folder, loaded.Mod);
        string id = manifest.Id, name = manifest.Name;
        try
        {
            Hooks.ResetFault(id);
            ModRegistry.SetFault(id, null);
            var modContext = new ModContext(manifest, folder);
            GmlRuntime.Activate(folder, id);
            if (manifest.Trusted) Game.Log($"WARNING: {name}: {ModsWindow.TrustedWarning}");
            if (GmlRuntime.ContainsGml(folder)) Game.Log($"WARNING: {name}: {ModsWindow.GmlWarning}");
            MainMenu.BeginLoad();
            EscMenu.BeginLoad();
            try { mod.Load(modContext); }
            finally
            {
                MainMenu.EndLoad();
                EscMenu.EndLoad();
            }
            Hooks.Mods.Add((id, mod));
            ModList.Add(manifest, mod);
            // (Running again: no longer waiting to come back with what it requires.)
            OffBecause.Remove(id);
            foreach (var waiting in OffWith.Values)
                waiting.Remove(id);
            if (mod is ITickable tickable)
                modContext.AddTickable(tickable);
            Mods.Add(loaded);
            Game.Log($"Loaded {name} ({id}) {manifest.Version}" + (manifest.InDevelopment ? " (development build: \"stoneforge\": \"latest\")" : "")
                + (manifest.Author.Length > 0 ? $" by {manifest.Author}" : "")
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

    // At start: a folder compiled (after those before it) - against the mods it requires, compiled before it.
    private static Compiled CompileAtStart(int index)
    {
        string folder = _folders[index];
        if (_orderProblems.TryGetValue(folder, out string? problem))
            return new Compiled(_manifests[index], new ModCompiler.Result(null, new List<string> { problem }), 0);
        return CompileSource(folder, id =>
        {
            int at = _manifests.FindIndex(m => m?.Id == id);
            return at < 0 ? (null, null)
                : (_manifests[at], at < index && _compiles[at].IsCompleted ? _compiles[at].Result.Result.Assembly : null);
        }, "didn't compile");
    }

    // A folder's source compiled and checked - on any thread: nothing of the game is touched. Required: a mod by its ID
    // - its mod.json and its code as compiled (null: not there; no code: it isn't ready, as notReady says).
    // (Its mod.json first: without a valid one - needing a newer StoneForge, or a mod that isn't there - it isn't compiled.)
    private static Compiled CompileSource(string folder, Func<string, (ModManifest? Manifest, byte[]? Image)> required, string notReady)
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
        // (The game's native build has no GML runner: a mod with GML of its own can't run there.)
        if (Game.IsNative && GmlRuntime.ContainsGml(folder))
            return new Compiled(manifest, new ModCompiler.Result(null, new List<string> {
                "it has GML (its GML folder), which the game's native build can't run - only the VM branch can" }), 0);
        var images = new List<byte[]>();
        foreach (string id in LoadOrder.AllRequired(manifest, id => required(id).Manifest))
        {
            var (other, image) = required(id);
            if (image == null)
                return new Compiled(manifest, new ModCompiler.Result(null, new List<string> {
                    other == null ? Missing(id) : $"needs {other.Name} ({id}), which {notReady}" }), 0);
            images.Add(image);
        }
        ModCompiler.Result result;
        try { result = ModCompiler.Compile(folder, manifest.Trusted, images); }
        catch (Exception e) { result = new ModCompiler.Result(null, new List<string> { e.Message }); }
        return new Compiled(manifest, result, Math.Max(1, timer.ElapsedMilliseconds));
    }

    // A compiled folder loaded into a new context (with the mods it requires, running), with its IStoneMod class made (on
    // the game's thread) - one per folder; the Mods window's entry says why when it didn't load.
    private static Loaded? Instantiate(string folder, Compiled compiled)
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
            return null;
        }
        if (compiled.Result.Assembly == null)
        {
            Failed("it doesn't compile", compiled.Result.Errors);
            return null;
        }
        Game.Log($"{folderName}: compiled and checked in {compiled.Milliseconds} ms");
        var manifest = compiled.Manifest;
        var required = LoadOrder.AllRequired(manifest, id => Mods.FirstOrDefault(m => m.Id == id)?.Manifest)
            .Select(id => Mods.FirstOrDefault(m => m.Id == id)?.Assembly).OfType<Assembly>();
        var context = new ModLoadContext(folderName, manifest.Trusted ? ModCompiler.Libraries(folder) : null, required);
        try
        {
            Assembly assembly = context.LoadFromStream(new MemoryStream(compiled.Result.Assembly));
            var types = assembly.GetExportedTypes().Where(t => !t.IsAbstract && typeof(IStoneMod).IsAssignableFrom(t)).ToList();
            if (types.Count != 1)
            {
                Failed(types.Count == 0 ? "it has no public IStoneMod class" : "it has more than one IStoneMod class ("
                    + string.Join(", ", types.Select(t => t.Name)) + ") - one mod per folder", Array.Empty<string>());
                context.Unload();
                return null;
            }
            var mod = (IStoneMod)Activator.CreateInstance(types[0])!;
            return new Loaded { Id = manifest.Id, Name = manifest.Name, Folder = folder, Mod = mod, Context = context, Manifest = manifest,
                Assembly = assembly, Image = compiled.Result.Assembly };
        }
        catch (Exception e)
        {
            Failed("couldn't load: " + (e is TargetInvocationException { InnerException: { } inner } ? inner.Message : e.Message), Array.Empty<string>());
            context.Unload();
            return null;
        }
    }

    // Whether a folder's mod.json asks for full access (false: it doesn't, or it has no valid one).
    private static bool IsTrusted(string folder) => TryManifest(folder)?.Trusted ?? false;

    // A folder's mod.json; null if it has no valid one.
    private static ModManifest? TryManifest(string folder)
    {
        try { return ModManifest.Read(folder); }
        catch { return null; }
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
