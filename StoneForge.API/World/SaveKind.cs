namespace StoneForge;

/// <summary>What kind of save a <see cref="SaveFile"/> is (the game's save types).</summary>
public enum SaveKind
{
    Unknown = -4,
    /// <summary>Saved at a campfire or an inn ("save_1", "save_2").</summary>
    Manual = 0,
    /// <summary>Saved by the game as you travel ("autosave_1"...).</summary>
    Auto = 1,
    /// <summary>Saved on quitting, removed once loaded ("exitsave_1").</summary>
    Exit = 2,
}
