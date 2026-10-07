namespace StoneForge.Loader;

/// <summary>A mod the loader found: loaded, switched off by the player, or not loaded (Error: why - no valid mod.json,
/// it didn't compile, or uses something mods aren't allowed). Id: its mod.json id (its folder's name when it has no
/// valid one). Folder: its folder in mods\. Trusted: it asks for full access (mod.json "trusted": true) - it runs only once
/// the player has allowed it. Requires: the mods it needs (mod.json "requires"), by ID.</summary>
public sealed record ModInfo(string Id, string Name, string Description, string Author, string Version, bool Enabled, string Folder, string? Error = null, string? RuntimeError = null, bool ContainsGml = false, bool Trusted = false, IReadOnlyList<string>? Requires = null, bool IsSml = false);
