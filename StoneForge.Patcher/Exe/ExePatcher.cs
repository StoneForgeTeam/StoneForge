using System.Diagnostics;
using System.IO;

namespace StoneForge.Patcher;

/// <summary>StoneShard.exe patched with Aurie's patcher (AuriePatcher.exe, installed beside us): a section that
/// loads AurieCore.dll before the game's own code. The game's own exe is kept as StoneShard.exe.vanilla.</summary>
internal static class ExePatcher
{
    public static bool IsPatched(GameFolder game) => PeFile.HasSection(game.Exe, ".aurie");

    public static void Patch(GameFolder game)
    {
        if (!File.Exists(game.AurieCore))
            throw new FileNotFoundException("AurieCore.dll not found beside the game: " + game.AurieCore);
        // The game's own exe kept (refreshed when it isn't ours - after a game update, say).
        if (!IsPatched(game))
        {
            File.Copy(game.Exe, game.ExeBackup, overwrite: true);
            PatcherConsole.Log($"Backed up {Path.GetFileName(game.Exe)} -> {Path.GetFileName(game.ExeBackup)}");
        }
        else
        {
            // The .aurie section already loads AurieCore.dll by its stable path. Re-running AuriePatcher on an
            // executable that has that section fails (and is unnecessary); only an unpatched exe, such as one
            // restored by Steam, needs patching again.
            PatcherConsole.Log(File.Exists(game.ExeBackup)
                ? "StoneShard.exe is already patched."
                : "StoneShard.exe is already patched, and there's no backup of the original (Steam's 'Verify integrity' restores it).");
            return;
        }

        string aurie = Path.Combine(game.PatcherDir, "AuriePatcher.exe");
        if (!File.Exists(aurie))
            throw new FileNotFoundException("AuriePatcher.exe missing: " + aurie);
        var patcher = Process.Start(new ProcessStartInfo(aurie)
        {
            ArgumentList = { game.Exe, game.AurieCore, "install" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        })!;
        string output = patcher.StandardOutput.ReadToEnd();
        patcher.WaitForExit();
        if (patcher.ExitCode != 0 || !IsPatched(game))
            throw new InvalidOperationException("Patching the exe failed: " + output.Trim());
        PatcherConsole.Log("Patched StoneShard.exe: StoneForge now starts with the game.");
    }

    /// <summary>The game's own exe back, and its backup gone. False if it wasn't patched.</summary>
    public static bool Restore(GameFolder game)
    {
        if (!IsPatched(game))
        {
            // (A backup left beside the game's own exe - Steam already put it back - is no longer needed.)
            if (File.Exists(game.ExeBackup))
                File.Delete(game.ExeBackup);
            return false;
        }
        if (!File.Exists(game.ExeBackup))
            throw new FileNotFoundException("No backup of the original exe (" + game.ExeBackup + ") - use Steam's 'Verify integrity of game files' instead.");
        File.Copy(game.ExeBackup, game.Exe, overwrite: true);
        File.Delete(game.ExeBackup);
        return true;
    }
}
