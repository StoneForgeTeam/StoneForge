namespace StoneForge;

/// <summary>One save in a character folder (<see cref="SaveSlot.Saves"/>).</summary>
public sealed record SaveFile(SaveSlot Slot, string Name)
{
    /// <summary>Its kind, by its name.</summary>
    public SaveKind Kind => Name[..Math.Max(0, Name.IndexOf('_'))] switch
    {
        "save" => SaveKind.Manual,
        "autosave" => SaveKind.Auto,
        "exitsave" => SaveKind.Exit,
        _ => SaveKind.Unknown,
    };

    /// <summary>Its info (scr_slotSaveMapLoad: the game's save.map) - the character, where they were, when; null if it
    /// isn't on disk.</summary>
    public SaveInfo? Info => SaveSlots.Read(Game.CallScript("scr_slotSaveMapLoad", default, Slot.Name, Name), map => new SaveInfo(SaveSlots.Values(map)));
}
