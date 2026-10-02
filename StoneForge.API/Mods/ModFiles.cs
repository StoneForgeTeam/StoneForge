namespace StoneForge;

/// <summary>A mod's files - the only file access mods have (System.IO itself isn't available to them).
/// A relative path is in the mod's own folder (mods\&lt;mod&gt;\). Reading: anywhere in the game's folder or
/// Stoneshard's data folder (%LOCALAPPDATA%\StoneShard - saves, settings). Writing: only in the data folder
/// or the mod's own folder - never the game's files, the loader or other mods. A path that leaves those
/// (.., a junction or link on the way, a device or stream name) is refused with
/// <see cref="UnauthorizedAccessException"/>.</summary>
public sealed class ModFiles
{
    // Most a single write may be: a mod filling the disk is stopped early.
    private const long MaxWriteBytes = 16 * 1024 * 1024;

    internal ModFiles(string modFolder)
    {
        ModFolder = Path.GetFullPath(modFolder);
    }

    /// <summary>The mod's own folder.</summary>
    public string ModFolder { get; }

    // Its assets: pictures (ModContext.LoadSprite, items', buffs') and its icon, kept apart from its code.
    internal const string AssetsFolderName = "Assets";

    /// <summary>The mod's assets folder, mods\&lt;mod&gt;\Assets\: its pictures are loaded from here
    /// (<see cref="ModContext.LoadSprite"/>, its items' and buffs' pictures), and its icon.png for the Mods
    /// window.</summary>
    public string AssetsFolder => Path.Combine(ModFolder, AssetsFolderName);

    /// <summary>The path for a file in the assets folder, for the methods here: <c>ReadAllText(AssetPath("names.txt"))</c>.</summary>
    public string AssetPath(string relativePath) => Path.Combine(AssetsFolderName, relativePath);

    /// <summary>The game's folder (where StoneShard.exe is).</summary>
    public static string GameFolder { get; } = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(ModFiles).Assembly.Location)!, ".."));

    /// <summary>Stoneshard's data folder: %LOCALAPPDATA%\StoneShard.</summary>
    public static string DataFolder { get; } = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoneShard"));

    public bool Exists(string path) => File.Exists(Resolve(path, write: false));
    public bool DirectoryExists(string path) => Directory.Exists(Resolve(path, write: false));
    public string ReadAllText(string path) => File.ReadAllText(Resolve(path, write: false));
    public string[] ReadAllLines(string path) => File.ReadAllLines(Resolve(path, write: false));
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(Resolve(path, write: false));

    /// <summary>The files in a folder (names relative to nothing: full paths), matching a pattern ("*.json").</summary>
    public string[] ListFiles(string folder, string pattern = "*")
    {
        if (pattern.Contains("..") || pattern.Contains('/') || pattern.Contains('\\'))
            throw new UnauthorizedAccessException("A pattern can't name other folders");
        string dir = Resolve(folder, write: false);
        return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly) : Array.Empty<string>();
    }

    public void WriteAllText(string path, string text)
    {
        if (text.Length * 3L > MaxWriteBytes)
            throw new IOException("Too much to write at once");
        File.WriteAllText(ResolveForWrite(path), text);
    }

    public void AppendAllText(string path, string text)
    {
        string full = ResolveForWrite(path);
        if ((File.Exists(full) ? new FileInfo(full).Length : 0) + text.Length * 3L > MaxWriteBytes)
            throw new IOException("Too much to write");
        File.AppendAllText(full, text);
    }

    public void WriteAllBytes(string path, byte[] bytes)
    {
        if (bytes.LongLength > MaxWriteBytes)
            throw new IOException("Too much to write at once");
        File.WriteAllBytes(ResolveForWrite(path), bytes);
    }

    public void Delete(string path)
    {
        string full = Resolve(path, write: true);
        if (File.Exists(full))
            File.Delete(full);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(Resolve(path, write: true));

    // (A file being written: its folder made if need be - inside the same rules.)
    private string ResolveForWrite(string path)
    {
        string full = Resolve(path, write: true);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    /// <summary>The full path for <paramref name="path"/>, if the mod may read (or write) it.</summary>
    internal string Resolve(string path, bool write)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new UnauthorizedAccessException("No path given");
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Not allowed: {path} (network and device paths)");
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ModFolder, path));
        // (A colon past the drive: an alternate data stream.)
        if (full.IndexOf(':', 2) >= 0)
            throw new UnauthorizedAccessException($"Not allowed: {path}");
        var roots = write ? new[] { DataFolder, ModFolder } : new[] { GameFolder, DataFolder };
        string? root = roots.FirstOrDefault(r => Inside(full, r));
        if (root == null)
            throw new UnauthorizedAccessException(write
                ? $"Mods can only write in their own folder or {DataFolder}: {full}"
                : $"Mods can only read in the game's folder or {DataFolder}: {full}");
        // Nothing on the way may be a junction or link (one could point anywhere).
        string current = root;
        foreach (string part in Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new UnauthorizedAccessException($"Not allowed: {full} (goes through a link)");
        }
        if (Directory.Exists(root) && File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new UnauthorizedAccessException($"Not allowed: {full} (goes through a link)");
        return full;
    }

    private static bool Inside(string full, string root)
    {
        string r = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(r, StringComparison.OrdinalIgnoreCase) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
    }
}
