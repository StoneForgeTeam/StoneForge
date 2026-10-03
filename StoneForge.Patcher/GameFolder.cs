using System.IO;

namespace StoneForge.Patcher;

/// <summary>Stoneshard's folder, and where StoneForge keeps its things in it.</summary>
internal sealed class GameFolder
{
    public GameFolder(string dir) => Dir = Path.GetFullPath(dir);

    public string Dir { get; }
    public string Exe => Path.Combine(Dir, "StoneShard.exe");
    /// <summary>The game's own exe, kept when it's patched.</summary>
    public string ExeBackup => Exe + ".vanilla";
    /// <summary>What the patched exe loads before the game's code.</summary>
    public string AurieCore => Path.Combine(Dir, "AurieCore.dll");
    public string Data => Path.Combine(Dir, "data.win");
    /// <summary>The loader's folder (StoneForge.Loader, StoneForge.API, its settings and log).</summary>
    public string Dotnet => Path.Combine(Dir, "dotnet");
    /// <summary>Where the patcher is installed (with AuriePatcher.exe and the loader's GML).</summary>
    public string PatcherDir => Path.Combine(Dotnet, "patcher");
    /// <summary>The game's own data.win, kept: data.win is rebuilt from it.</summary>
    public string BaseData => Path.Combine(Dotnet, "data_base.win");
    /// <summary>Line 1: data.win as we last wrote it (size|time); line 2: what it was built from.</summary>
    public string DataKey => Path.Combine(Dotnet, "data_stonemod.key");
    // Mods' consumables the game data has had objects for (kept: saves with them must still load).
    public string KnownConsumables => Path.Combine(Dotnet, "stoneforge-consumables.txt");
    // ...and mods' skills.
    public string KnownSkills => Path.Combine(Dotnet, "stoneforge-skills.txt");
    // ...and mods' own objects.
    public string KnownObjects => Path.Combine(Dotnet, "stoneforge-objects.txt");
    /// <summary>Every file StoneForge installed, relative to the game folder (for upgrades and uninstall).</summary>
    public string Manifest => Path.Combine(Dotnet, "stoneforge-files.txt");
    /// <summary>A copy of the game data from when the game was started on one with -game (no longer made).</summary>
    public string OldDataCopy => Path.Combine(Dir, "data_stonemod.win");
    public string Mods => Path.Combine(Dir, "mods");

    /// <summary>Whether this patcher is the one installed in this game folder (not a release's copy).</summary>
    public bool HasThisPatcher => string.Equals(Path.GetFullPath(PatcherDir).TrimEnd('\\'), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Stoneshard's folder: the one given, else the one we're installed in (&lt;game&gt;\dotnet\patcher\),
    /// else found in the Steam libraries, else asked for (when someone's there to answer). Null if none.</summary>
    public static GameFolder? Locate(string? given)
    {
        if (!string.IsNullOrWhiteSpace(given))
            return new GameFolder(given.Trim().Trim('"'));
        string installed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        if (File.Exists(Path.Combine(installed, "StoneShard.exe")))
            return new GameFolder(installed);
        if (SteamLibrary.FindStoneshard() is string steam)
            return new GameFolder(steam);
        if (Console.IsInputRedirected)
            return null;
        PatcherConsole.Show();
        PatcherConsole.Log("Stoneshard wasn't found in your Steam libraries. Its folder (the one with StoneShard.exe):");
        string? typed = Console.ReadLine();
        return string.IsNullOrWhiteSpace(typed) ? null : new GameFolder(typed.Trim().Trim('"'));
    }
}
