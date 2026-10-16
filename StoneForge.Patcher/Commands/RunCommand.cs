using System.Diagnostics;
using System.IO;

namespace StoneForge.Patcher;

/// <summary>run: the game started through us - Steam's launch option <c>"...\StoneForge.Patcher.exe" run %command%</c>
/// (Steam puts the game's own command after "run"). If a game update or 'Verify integrity' has put the game's
/// own exe back, it's patched again first; then the game runs, and we wait for it, so Steam still sees it
/// playing. With <c>run --update %command%</c>, a newer StoneForge release is installed first (<see cref="Updater"/>).</summary>
internal static class RunCommand
{
    public static int Run(string[] command)
    {
        bool update = command.Length > 0 && string.Equals(command[0], "--update", StringComparison.OrdinalIgnoreCase);
        if (update)
            command = command[1..];
        if (command.Length == 0)
            throw new ArgumentException("usage: StoneForge.Patcher run [--update] <the game's exe> [its arguments] (as Steam's launch option: run %command%)");
        var game = new GameFolder(Path.GetDirectoryName(Path.GetFullPath(command[0]))!);
        Updater.RemoveLeftovers(game);
        if (update && File.Exists(game.AurieCore))
            Updater.Run(game);
        if (File.Exists(game.AurieCore) && File.Exists(game.Exe) && !ExePatcher.IsPatched(game))
        {
            PatcherConsole.Show();
            PatcherConsole.Log("The game's own StoneShard.exe was restored (an update or file verification): patching it again...");
            // (Failing that, the game still starts - just without the mods.)
            try { ExePatcher.Patch(game); }
            catch (Exception e)
            {
                PatcherConsole.Log("Couldn't patch it: " + e.Message + "\nThe game starts without mods. (Press a key.)");
                PatcherConsole.WaitForKey();
            }
        }
        var start = new ProcessStartInfo(command[0]) { UseShellExecute = false, WorkingDirectory = game.Dir };
        foreach (string argument in command.Skip(1))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
