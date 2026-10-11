namespace StoneForge;

/// <summary>Backups of the player's saves, kept by StoneForge whatever mods there are: every save the game is about to
/// write over or delete is copied first, so a save written after something went wrong - a mod's bug, an error box, a
/// crash mid-save - never takes the last good one with it.</summary>
/// <remarks>The game writes a save into its folder (characters_v1\&lt;character&gt;\&lt;save&gt;\: autosave_1, a manual
/// save...) in scr_slotSaveUpdate, over what was there, and deletes one in scr_slotSaveDelete (its oldest, making room;
/// one deleted in the save menu). Before either, the folder - still the save it was - is copied to
/// <see cref="Folder"/>\&lt;character&gt;\&lt;when&gt;_&lt;save&gt;, the newest <see cref="Kept"/> of each character's
/// kept: outside characters_v1, so neither the save menu nor Steam Cloud sees them. <see cref="Keep"/> keeps a copy of the
/// save being played that's never deleted with the rest - after a mod's error, the last save made before it (SaveGuard).
/// A backup that fails is logged and the game goes on: it must never stop a save. To restore one: copy its folder into
/// characters_v1\&lt;character&gt;\, named as the save it was (less the time).</remarks>
public static class SaveBackups
{
    /// <summary>How many of each character's backups are kept (the newest).</summary>
    public const int Kept = 20;

    /// <summary>Where they are: a folder per character.</summary>
    public static string Folder => Path.Combine(ModFiles.DataFolder, "StoneForge", "save-backups");

    private static string Saves => Path.Combine(ModFiles.DataFolder, "characters_v1");

    // The loader's: the game's saving and deleting, backed up first. (Both are the loader's script hooks.)
    internal static void Install(ModContext loader)
    {
        loader.OnScript("scr_slotSaveUpdate", before: call => { Backup(call); return false; });
        loader.OnScript("scr_slotSaveDelete", before: call => { Backup(call); return false; });
    }

    /// <summary>Keeps a copy of the save being played (the last one loaded or made) that's never deleted with the other
    /// backups, as <c>&lt;label&gt;_&lt;when&gt;_&lt;save&gt;</c>. Where it went; null if there's none to keep.</summary>
    public static string? Keep(string label)
    {
        try
        {
            if (SaveSlots.CurrentSave is not { } save || !SafeName(save.Slot.Name) || !SafeName(save.Name) || !SafeName(label))
                return null;
            string source = Path.Combine(Saves, save.Slot.Name, save.Name);
            if (!Directory.Exists(source))
                return null;
            string target = Path.Combine(Folder, save.Slot.Name, $"{label}_{DateTime.Now:yyyyMMdd-HHmmss}_{save.Name}");
            Copy(source, target);
            return target;
        }
        catch (Exception e)
        {
            Game.Log($"Save backups: couldn't keep a copy of the save being played: {e.Message}");
            return null;
        }
    }

    // (Both scripts take the character's folder, then the save's.)
    private static void Backup(ScriptCall call)
    {
        if (call.Args.Length >= 2 && call.Args[0].Kind == GmKind.String && call.Args[1].Kind == GmKind.String)
            Backup(call.Args[0].AsString, call.Args[1].AsString);
    }

    private static void Backup(string character, string save)
    {
        try
        {
            if (!SafeName(character) || !SafeName(save))
                return;
            string source = Path.Combine(Saves, character, save);
            if (!Directory.Exists(source) || !Directory.EnumerateFiles(source).Any())
                return;
            string mine = Path.Combine(Folder, character);
            Copy(source, Path.Combine(mine, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{save}"));
            // (The newest kept: their names start with when they were made. Kept ones - "before-error_..." - stay.)
            foreach (string old in Directory.GetDirectories(mine).Where(d => char.IsDigit(Path.GetFileName(d)[0]))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(Kept))
                Directory.Delete(old, recursive: true);
        }
        catch (Exception e)
        {
            Game.Log($"Save backups: couldn't back up {character}\\{save} first: {e.Message}");
        }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (string folder in Directory.GetDirectories(from))
            Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    // (A folder name of the game's - "character_1", "autosave_1" - not a path, and not its "N/A" for none.)
    private static bool SafeName(string name)
        => name.Length > 0 && name != "N/A" && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name != "." && name != "..";
}
