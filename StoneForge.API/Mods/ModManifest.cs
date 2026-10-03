namespace StoneForge;

/// <summary>A mod's details, from the mod.json in its folder:
/// <code>
/// {
///   "id": "examplemod",
///   "name": "Example Mod",
///   "version": "0.1.0",
///   "author": "you",
///   "description": "What it does, in a sentence or two.",
///   "stoneforge": "0.1.0"
/// }
/// </code>
/// <c>id</c>, <c>name</c> and <c>version</c> are required. A mod is known by its <see cref="Id"/>: its settings,
/// whether it's switched on, and its content - an item keyed "blade" is "examplemod:blade" (see
/// <see cref="ModContext.ContentId"/>) - so don't change it once players have the mod.</summary>
public sealed class ModManifest
{
    internal ModManifest(ManifestData data)
    {
        Id = data.Id;
        Name = data.Name;
        Version = data.Version;
        Author = data.Author;
        Description = data.Description;
        StoneForge = data.StoneForge;
        Trusted = data.Trusted;
    }

    /// <summary>The mod's permanent ID: lowercase letters and digits, single underscores between them.</summary>
    public string Id { get; }
    /// <summary>Its name, as shown (the Mods window, its log lines, "Mod: ..." on its items).</summary>
    public string Name { get; }
    /// <summary>Its version, e.g. "0.1.0".</summary>
    public string Version { get; }
    /// <summary>Who made it ("" if not given).</summary>
    public string Author { get; }
    /// <summary>What it does ("" if not given).</summary>
    public string Description { get; }
    /// <summary>The StoneForge version it needs, at least (null if not given).</summary>
    public string? StoneForge { get; }
    /// <summary>Whether it asks for full access (<c>"trusted": true</c>): its own DLLs (any .dll in its folder, outside
    /// bin and obj - LiteNetLib, say), compiled against the whole of .NET, and none of StoneForge's checks on what it
    /// uses - networking, threads, files, reflection. It runs only once the player has allowed it in the Mods window,
    /// warned that it can do anything a program on their PC can.</summary>
    public bool Trusted { get; }

    /// <summary>A mod's folder's mod.json (throws InvalidDataException saying what's wrong).</summary>
    internal static ModManifest Read(string folder) => new(ModIdentity.ReadManifest(folder));
}
