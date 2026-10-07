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
        string root = DotNetRoot;
        if (!HasRuntime(root, "Microsoft.NETCore.App"))
            PatcherConsole.Log("WARNING: .NET 10 is missing. Install the .NET 10 Windows Desktop Runtime (x64) from https://dotnet.microsoft.com/download/dotnet/10.0.");
        else if (!HasRuntime(root, "Microsoft.WindowsDesktop.App"))
            PatcherConsole.Log("WARNING: MSL packages need the .NET 10 Windows Desktop Runtime (x64). The base .NET Runtime alone is not enough. Install it from https://dotnet.microsoft.com/download/dotnet/10.0. Regular C# mods can still run.");
    }

    // Match the x64 apphost's environment overrides; otherwise use the installed runtime hosting this patcher.
    private static string DotNetRoot => Environment.GetEnvironmentVariable("DOTNET_ROOT_X64")
        ?? Environment.GetEnvironmentVariable("DOTNET_ROOT")
        ?? Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));

    internal static bool HasRuntime(string root, string framework)
    {
        string shared = Path.Combine(root, "shared", framework);
        return Directory.Exists(shared) && Directory.EnumerateDirectories(shared).Any(path =>
            Version.TryParse(Path.GetFileName(path), out var version) && version.Major == 10);
    }

    internal static void RequireMslRuntime(string? root = null)
    {
        root ??= DotNetRoot;
        if (!HasRuntime(root, "Microsoft.NETCore.App") || !HasRuntime(root, "Microsoft.WindowsDesktop.App"))
            throw new InvalidOperationException("MSL packages require the .NET 10 Windows Desktop Runtime (x64). Install it from https://dotnet.microsoft.com/download/dotnet/10.0, then restart Stoneshard. The base .NET Runtime alone is not enough.");
    }
}
