using System.Diagnostics;
using System.IO;

namespace StoneForge.Patcher;

/// <summary>StoneForge's files in the game folder: copied there from a release (its files\ folder, which mirrors
/// the game folder - this patcher sits in its dotnet\patcher\), listed in a manifest (dotnet\stoneforge-files.txt)
/// so an upgrade can drop what a newer release no longer ships and uninstall removes exactly what was installed.
/// Never touched: mods\ and the loader's settings (which mods are off, the mod item and buff records).</summary>
internal static class Payload
{
    // Made by StoneForge as it runs (not shipped): removed on uninstall too.
    private static readonly string[] Generated = { @"dotnet\bridge.log", "aurie.log", "YYToolkit.log" };

    /// <summary>The release's files\ folder, when this patcher is a release's copy (not installed in a game).</summary>
    public static string? ReleaseRoot()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        return File.Exists(Path.Combine(root, "AurieCore.dll")) && Directory.Exists(Path.Combine(root, "aurie")) && !File.Exists(Path.Combine(root, "StoneShard.exe"))
            ? root
            : null;
    }

    /// <summary>The release's files copied into the game (over an older StoneForge), what it no longer ships
    /// removed, the manifest written.</summary>
    public static void Install(string releaseRoot, GameFolder game)
    {
        var files = Directory.GetFiles(releaseRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(releaseRoot, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var earlier = ReadManifest(game);
        foreach (string file in files)
        {
            string target = Path.Combine(game.Dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(releaseRoot, file), target, overwrite: true);
        }
        int dropped = 0;
        foreach (string file in earlier.Except(files, StringComparer.OrdinalIgnoreCase))
            dropped += Delete(Path.Combine(game.Dir, file)) ? 1 : 0;
        Directory.CreateDirectory(game.Mods);
        File.WriteAllLines(game.Manifest, files);
        PatcherConsole.Log($"Copied StoneForge's {files.Count} files into {game.Dir}" + (dropped > 0 ? $" (and removed {dropped} it no longer uses)" : ""));
    }

    /// <summary>Everything StoneForge installed, gone (mods\ and the loader's settings kept). What's in use - this
    /// patcher itself, run from the game - goes once it has exited.</summary>
    public static void Uninstall(GameFolder game)
    {
        var files = ReadManifest(game);
        if (files.Count == 0)
            // (No manifest - deleted, or lost: what StoneForge puts there.)
            files = KnownLayout(game);
        var inUse = new List<string>();
        foreach (string file in files.Concat(Generated).Append(Path.GetRelativePath(game.Dir, game.Manifest)))
        {
            string path = Path.Combine(game.Dir, file);
            try { Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { inUse.Add(path); }
        }
        RemoveEmptyFolders(game);
        if (inUse.Count > 0)
            DeleteAfterExit(inUse, game);
        PatcherConsole.Log("Removed StoneForge's files. Your mods (mods\\) and their settings were kept.");
    }

    private static List<string> ReadManifest(GameFolder game)
        => File.Exists(game.Manifest) ? File.ReadAllLines(game.Manifest).Where(l => l.Length > 0).ToList() : new();

    // An install without a manifest: AurieCore, the Aurie modules, the loader and the patcher.
    private static List<string> KnownLayout(GameFolder game)
    {
        var files = new List<string> { "AurieCore.dll", @"aurie\YYToolkit.dll", @"aurie\StoneForge.Bridge.dll" };
        foreach (string pattern in new[] { "StoneForge.*", "Microsoft.CodeAnalysis*.dll" })
            if (Directory.Exists(game.Dotnet))
                files.AddRange(Directory.GetFiles(game.Dotnet, pattern).Select(f => Path.GetRelativePath(game.Dir, f)));
        if (Directory.Exists(game.PatcherDir))
            files.AddRange(Directory.GetFiles(game.PatcherDir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(game.Dir, f)));
        return files;
    }

    private static bool Delete(string path)
    {
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    // StoneForge's folders, once nothing's left in them (dotnet\ stays while it holds the loader's settings).
    private static void RemoveEmptyFolders(GameFolder game)
    {
        foreach (string dir in new[] { game.PatcherDir, game.Dotnet, Path.Combine(game.Dir, "aurie") })
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(sub).Any())
                    Directory.Delete(sub);
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }

    // Files in use now (this patcher's own): deleted by a small background command a few seconds after we exit.
    private static void DeleteAfterExit(List<string> paths, GameFolder game)
    {
        string cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string deletes = string.Join(" & ", paths.Select(p => $"del /f /q \"{p}\""));
        string folders = string.Join(" & ", new[] { game.PatcherDir, game.Dotnet, Path.Combine(game.Dir, "aurie") }.Select(d => $"rmdir \"{d}\" 2>nul"));
        Process.Start(new ProcessStartInfo(cmd, $"/c ping -n 4 127.0.0.1 >nul & {deletes} & rmdir /s /q \"{game.PatcherDir}\" 2>nul & {folders}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = game.Dir,
        });
    }
}
