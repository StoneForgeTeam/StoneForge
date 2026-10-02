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
        PatcherConsole.Log("Done - start Stoneshard as usual (Steam or the exe). Mods go in " + game.Mods);
        PatcherConsole.Log("");
        PatcherConsole.Log("Steam's updates and 'Verify integrity' put the game's own exe back, which turns StoneForge off until");
        PatcherConsole.Log("it's installed again. To have it put back automatically, set Stoneshard's launch options in Steam");
        PatcherConsole.Log("(Properties > General > Launch options) to:");
        PatcherConsole.Log($"    \"{patcher}\" run %command%");
    }
}
