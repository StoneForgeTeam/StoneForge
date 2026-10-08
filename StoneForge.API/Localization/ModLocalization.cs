namespace StoneForge;

/// <summary>A mod's translations in Localization/&lt;language&gt;.json. Each file is an object of text keys and
/// strings with numbered placeholders ({0}, {1}...). Missing translations fall back to en-US, then en, then the key.</summary>
public sealed class ModLocalization
{
    private readonly TextCatalog _catalog;
    private string _lastLanguage = Localization.Language;
    private long _nextFallback;
    private long _dirtyAt;
    private long _retryAt;
    private int _watcherFailed;
    private int _disposed;
    private FileSystemWatcher? _watcher;
    private readonly ModContext _context;
    private readonly ModFiles? _files;
    private static readonly List<(string Mod, ModLocalization Texts)> Owned = new();
    internal bool HasPendingChanges => Interlocked.Read(ref _dirtyAt) != 0;
    internal bool IsWatching => _watcher != null;
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    private readonly List<Func<bool>> _bindings = new();
    internal ModLocalization(ModContext context, ModFiles? files)
    {
        _context = context;
        _files = files;
        _catalog = new TextCatalog(locale =>
        {
            if (files == null) return null;
            string path = Path.Combine("Localization", locale + ".json");
            string resolved = files.Resolve(path, write: false);
            if (!File.Exists(resolved)) return null;
            if (new FileInfo(resolved).Length > 1024 * 1024) throw new InvalidDataException("Catalog exceeds 1 MB.");
            return files.ReadAllText(path);
        }, message => context.Log("Localization: " + message));
        Owned.Add((context.Id, this));
        StartWatcher();
        context.Frame += () => ProcessChanges(Environment.TickCount64);
    }

    private void StartWatcher()
    {
        if (_files == null || IsDisposed) return;
        FileSystemWatcher? watcher = null;
        try
        {
            // Watch the parent too: editors can replace the catalog or its entire folder.
            string root = _files.Resolve(".", write: false);
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += Renamed;
            watcher.Error += (_, _) =>
            {
                if (IsDisposed) return;
                Interlocked.Exchange(ref _watcherFailed, 1);
                MarkDirty();
            };
            _watcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            watcher?.Dispose();
            _watcher = null;
            _context.Log("Localization: file watcher unavailable; using fallback checks. " + e.Message);
        }
    }

    private bool IsCatalogPath(string path)
    {
        if (_files == null) return false;
        string relative = Path.GetRelativePath(_files.ModFolder, path);
        return relative.Equals("Localization", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Localization" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Path.GetExtension(relative).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }
    // Worker-thread callbacks only queue work. Catalog reads, logging and mod callbacks stay on the game thread.
    private void Changed(object sender, FileSystemEventArgs e)
    {
        if (IsCatalogPath(e.FullPath)) MarkDirty();
    }
    private void Renamed(object sender, RenamedEventArgs e)
    {
        if (IsCatalogPath(e.FullPath) || IsCatalogPath(e.OldFullPath)) MarkDirty();
    }
    private void MarkDirty()
    {
        if (!IsDisposed) Interlocked.Exchange(ref _dirtyAt, Environment.TickCount64);
    }

    internal void ProcessChanges(long now)
    {
        if (IsDisposed) return;
        bool languageChanged = _lastLanguage != Localization.Language;
        long dirty = Interlocked.Read(ref _dirtyAt);
        bool notified = dirty != 0 && now - dirty >= 150;
        bool fallback = now >= _nextFallback;
        bool retry = _retryAt != 0 && now >= _retryAt;
        bool filesChanged = false;
        if (notified || fallback || retry)
        {
            if (notified)
            {
                // Don't erase another save notification arriving while this frame is processing.
                Interlocked.CompareExchange(ref _dirtyAt, 0, dirty);
                _retryAt = now + 500; // Retry a save that was still locked or incomplete.
            }
            else if (retry) _retryAt = 0;
            if (fallback)
            {
                _nextFallback = now + 10_000;
                if (_watcher == null || Interlocked.Exchange(ref _watcherFailed, 0) != 0)
                {
                    _watcher?.Dispose();
                    _watcher = null;
                    StartWatcher();
                }
            }
            filesChanged = _catalog.Refresh();
        }
        if (!languageChanged && !filesChanged) return;
        _lastLanguage = Localization.Language;
        _bindings.RemoveAll(refresh => !refresh());
        MainMenu.RefreshLocalizedButtons(_context.Id);
        if (languageChanged) LanguageChanged?.Invoke();
        TranslationsChanged?.Invoke();
    }

    internal static void RemoveMod(string mod)
    {
        foreach (var entry in Owned.Where(entry => entry.Mod == mod).ToArray()) entry.Texts.Release();
        Owned.RemoveAll(entry => entry.Mod == mod);
    }
    private void Release()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _watcher?.Dispose();
        _watcher = null;
        _bindings.Clear();
        LanguageChanged = null;
        TranslationsChanged = null;
    }

    /// <summary>The current game language.</summary>
    public string Language => Localization.Language;
    /// <summary>Raised on the next frame after the game language changes. Refresh translated UI labels here.</summary>
    public event Action? LanguageChanged;
    /// <summary>Raised on the game frame thread after the language or a loaded translation file changes.</summary>
    public event Action? TranslationsChanged;

    /// <summary>Refreshes a target immediately and whenever translations change. Use a callback that does not
    /// capture the target; the weak reference allows discarded controls to be collected.</summary>
    public void Bind<T>(T target, Action<T> refresh) where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(refresh);
        refresh(target);
        var weak = new WeakReference<T>(target);
        _bindings.Add(() =>
        {
            if (!weak.TryGetTarget(out var live)) return false;
            refresh(live);
            return true;
        });
    }

    /// <summary>Gets the mod's text, with English fallback and culture-aware placeholder formatting.
    /// File notifications refresh loaded translations after a short debounce; a slower check covers missed events.</summary>
    public string Get(string key, params object?[] arguments) => _catalog.Get(Language, key, arguments);
}
