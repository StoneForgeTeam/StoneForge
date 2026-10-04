namespace StoneForge;

/// <summary>A character folder's info (<see cref="SaveSlot.Info"/>), its values by the game's names.</summary>
public sealed record SlotInfo(IReadOnlyDictionary<string, GmValue> Values)
{
    /// <summary>A value (a mod's too); undefined if there's none.</summary>
    public GmValue this[string key] => Values.TryGetValue(key, out GmValue value) ? value : GmValue.Undefined;

    /// <summary>The character's name key ("N/A": unknown).</summary>
    public string NameKey => this["nameKey"] is { Kind: GmKind.String } key ? key.AsString : "N/A";

    /// <summary>The character's name, as the game shows it (global.char_name by <see cref="NameKey"/>); null if it has
    /// none.</summary>
    public string? CharacterName => CharacterNames.Of(NameKey);

    public bool IsPermadeath => this["permadeath"].Kind != GmKind.Undefined && this["permadeath"].AsReal is not (-4 or 0);
    public bool IsPrologue => this["prologue"].Kind != GmKind.Undefined && this["prologue"].AsReal is not (-4 or 0);

    /// <summary>When it was last saved, in local time; null if unknown.</summary>
    public DateTime? SavedAt => SaveSlots.Date(this["dateTime"]);
}
