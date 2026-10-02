using System.Diagnostics;
using System.IO;

namespace StoneForge.Patcher;

/// <summary>What has to hold before StoneForge's files can change: the game closed, and .NET 10 there for the
/// loader to run on.</summary>
internal static class Preflight
{
    /// <summary>Throws if Stoneshard is running (its files are in use).</summary>
    public static void GameClosed()
    {
        if (Process.GetProcessesByName("StoneShard").Length > 0)
            throw new InvalidOperationException("Stoneshard is running - close it and try again.");
    }

    /// <summary>Warns if .NET 10 isn't installed where the game will look for it (the loader runs on .NET 10;
    /// StoneForge.Bridge starts it from the installed .NET, like the dotnet command does - which may not be the
    /// .NET this patcher found, with DOTNET_ROOT set).</summary>
    public static void DotNetRuntime()
    {
        string root = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        string shared = Path.Combine(root, "shared", "Microsoft.NETCore.App");
        bool found = Directory.Exists(shared) && Directory.GetDirectories(shared, "10.*").Length > 0;
        if (found)
            return;
        PatcherConsole.Log("");
        PatcherConsole.Log("WARNING: .NET 10 isn't installed, and the mods need it. Install the .NET 10 Runtime (x64)");
        PatcherConsole.Log("from https://dotnet.microsoft.com/download/dotnet/10.0 - then the mods load when you start the game.");
        PatcherConsole.Log("");
    }
}
