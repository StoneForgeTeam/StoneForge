namespace StoneForge;

/// <summary>A character folder (<see cref="SaveSlots"/>): its info and its saves.</summary>
public sealed record SaveSlot(string Name)
{
    /// <summary>Its number, as the save menu shows it ("character_3": 3).</summary>
    public int Number => int.TryParse(new string(Name.Where(char.IsDigit).ToArray()), out int n) ? n : 0;

    /// <summary>Whether it's on disk (scr_slotExists).</summary>
    public bool Exists => Game.CallScript("scr_slotExists", default, Name).AsBool;

    /// <summary>Its info (scr_slotMapLoad: the game's character.map) - the character, permadeath, the prologue, when it
    /// was last saved, and any values a mod added (<see cref="SaveSlots.OnInfoSaving"/>); null if it isn't on
    /// disk.</summary>
    public SlotInfo? Info => SaveSlots.Read(Game.CallScript("scr_slotMapLoad", default, Name), map => new SlotInfo(SaveSlots.Values(map)));

    /// <summary>Its saves, newest first (scr_slotSavesGetOrderList).</summary>
    public IReadOnlyList<SaveFile> Saves
        => SaveSlots.Names(Game.CallScript("scr_slotSavesGetOrderList", default, Name)).Select(save => new SaveFile(this, save)).ToArray();
}
