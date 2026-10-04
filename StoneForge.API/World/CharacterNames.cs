namespace StoneForge;

// The game's character names by name key (global.char_name).
internal static class CharacterNames
{
    internal static string? Of(string nameKey)
        => nameKey != "N/A" && Game.Global["char_name"].AsDsMap is { } names && names.Get(nameKey, "N/A") is { Kind: GmKind.String } name
            && name.AsString != "N/A" ? name.AsString : null;
}
