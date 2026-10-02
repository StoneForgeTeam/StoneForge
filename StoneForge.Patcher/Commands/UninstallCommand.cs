namespace StoneForge.Patcher;

/// <summary>uninstall: the game's own exe and data.win put back, and StoneForge's files removed (mods\ and the
/// mods' settings kept).</summary>
internal static class UninstallCommand
{
    public static void Run(GameFolder game)
    {
        PatcherConsole.Log("Uninstalling StoneForge from " + game.Dir);
        Preflight.GameClosed();
        PatcherConsole.Log(ExePatcher.Restore(game) ? "Restored the original StoneShard.exe." : "StoneShard.exe isn't patched.");
        GameDataBuilder.Restore(game);
        Payload.Uninstall(game);
        PatcherConsole.Log("\nDone - Stoneshard is as it was.");
    }
}
