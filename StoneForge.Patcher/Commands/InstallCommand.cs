using System.IO;
using System.Reflection;

namespace StoneForge.Patcher;

/// <summary>install: StoneForge put into the game. Run from a release (Install StoneForge.cmd) its files are
/// copied in first; then StoneShard.exe is patched (<see cref="ExePatcher"/>) and the game data made current.</summary>
internal static class InstallCommand
{
    public static void Run(GameFolder game)
    {
        string version = typeof(InstallCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";
        PatcherConsole.Log($"Installing StoneForge {version} into {game.Dir}");
        Preflight.GameClosed();
        Preflight.DotNetRuntime();
        if (Payload.ReleaseRoot() is string release)
            Payload.Install(release, game);
        else if (!game.HasThisPatcher || !File.Exists(game.AurieCore))
            throw new InvalidOperationException("StoneForge's files aren't in the game folder: run Install StoneForge.cmd from the release.");

        ExePatcher.Patch(game);
        GameDataBuilder.Prepare(game);

        string patcher = Path.Combine(game.PatcherDir, "StoneForge.Patcher.exe");
        PatcherConsole.Log("");
        PatcherConsole.Log("Done - start Stoneshard through its usual launcher. Mods go in " + game.Mods);
        PatcherConsole.Log("");
        PatcherConsole.Log("Game updates and file verification can restore the game's own exe, which turns StoneForge off until");
        PatcherConsole.Log("it's installed again. For Steam installations only, set Stoneshard's launch options in Steam");
        PatcherConsole.Log("(Properties > General > Launch options) to:");
        PatcherConsole.Log($"    \"{patcher}\" run %command%");
        PatcherConsole.Log("Or, to also update StoneForge to its latest release (0.9.x to a later 0.9.x) as the game starts:");
        PatcherConsole.Log($"    \"{patcher}\" run --update %command%");
    }
}
