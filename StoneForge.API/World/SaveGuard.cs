namespace StoneForge;

// A mod's error, and the player's save. The first time any mod's handler throws in a game (Hooks.Fail - after its error
// box), the save being played - the last made before it - is kept, never deleted with the other backups
// (SaveBackups.Keep), and on the next frame the player is asked whether to leave for the main menu without saving: the
// game may be off now, and a save would keep it. Yes leaves without saving; No plays on, each save still backed up
// first. Once a game: later errors are only their error boxes. (Asked on the frame after: not from inside whatever game
// code it was that failed.)
internal static class SaveGuard
{
    private static string? _failed;
    private static bool _asked;

    internal static void Install(ModContext loader)
        => loader.Frame += () =>
        {
            if (_failed == null || _asked || !Gm.InGame)
                return;
            _asked = true;
            string question = $"{_failed} hit an error, and your game may be off now - saving could keep what went wrong. "
                + "Your last save is backed up. Leave to the main menu without saving?";
            if (!GameDialogs.Confirm(loader, question, () => Rooms.ToMainMenu(save: false)))
                Game.Log("Save guard: couldn't ask about leaving after the error - the game's panel didn't open");
        };

    // A mod's handler has thrown (its first time there).
    internal static void ModFailed(string mod)
    {
        if (_failed != null || !Gm.InGame)
            return;
        _failed = ModContext.Find(mod)?.Name ?? mod;
        string? kept = SaveBackups.Keep("before-error");
        Game.Log($"Save guard: {_failed} hit an error - "
            + (kept != null ? $"the save being played is kept at {kept}" : "there's no save being played to keep"));
    }
}
