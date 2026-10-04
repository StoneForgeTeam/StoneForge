namespace StoneForge;

/// <summary>A save's info (<see cref="SaveFile.Info"/>), its values by the game's names.</summary>
public sealed record SaveInfo(IReadOnlyDictionary<string, GmValue> Values)
{
    public GmValue this[string key] => Values.TryGetValue(key, out GmValue value) ? value : GmValue.Undefined;

    /// <summary>Whether the game can load it (false: its data was missing or broken).</summary>
    public bool IsValid => this["valid"].AsBool;

    public string NameKey => this["nameKey"] is { Kind: GmKind.String } key ? key.AsString : "N/A";
    public string? CharacterName => CharacterNames.Of(NameKey);

    /// <summary>The location's title key, where the character was ("N/A": none).</summary>
    public string LocationTitleKey => this["locationTitleKey"] is { Kind: GmKind.String } key ? key.AsString : "N/A";

    /// <summary>The character's portrait sprite's name.</summary>
    public string? Avatar => this["avatar"] is { Kind: GmKind.String } avatar ? avatar.AsString : null;

    public bool IsPermadeath => this["permadeath"].Kind != GmKind.Undefined && this["permadeath"].AsReal is not (-4 or 0);
    public bool IsPrologue => this["prologue"].Kind != GmKind.Undefined && this["prologue"].AsReal is not (-4 or 0);
    public DateTime? SavedAt => SaveSlots.Date(this["dateTime"]);
}
