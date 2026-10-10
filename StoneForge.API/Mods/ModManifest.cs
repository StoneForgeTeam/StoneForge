namespace StoneForge;

/// <summary>A mod's details, from the mod.json in its folder:
/// <code>
/// {
///   "id": "examplemod",
///   "name": "Example Mod",
///   "version": "0.1.0",
///   "author": "you",
///   "github": "YourAccount/YourMod",
///   "contributors": ["76561197960278073"],
///   "description": "What it does, in a sentence or two.",
///   "stoneforge": "0.1.0",
///   "requires": ["othermod"],
///   "after": ["thirdmod"]
/// }
/// </code>
/// <c>id</c>, <c>name</c> and <c>version</c> are required; <c>requires</c> and <c>after</c> are other mods, by ID
/// (<see cref="Requires"/>, <see cref="After"/>). A mod is known by its <see cref="Id"/>: its settings,
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
        Requires = data.Requires ?? Array.Empty<string>();
        After = data.After ?? Array.Empty<string>();
        Contributors = Array.AsReadOnly(data.Contributors?.ToArray() ?? Array.Empty<string>());
        Github = data.Github;
    }

    /// <summary>The mod's permanent ID: lowercase letters and digits, single underscores between them.</summary>
    public string Id { get; }
    /// <summary>Its name, as shown (the Mods window, its log lines, "Mod: ..." on its items).</summary>
    public string Name { get; }
    /// <summary>Its version, e.g. "0.1.0".</summary>
    public string Version { get; }
    /// <summary>Who made it ("" if not given).</summary>
    public string Author { get; }
    /// <summary>Optional GitHub repository (owner/repo), used by the Escape-menu bug reporter.
    /// mod.json accepts owner/repo or an HTTPS GitHub repository URL. The repository must accept BugDrop reports.</summary>
    public string? Github { get; }
    /// <summary>What it does ("" if not given).</summary>
    public string Description { get; }
    /// <summary>The StoneForge version it needs, at least (null if not given) - or "latest": a mod in development, built
    /// against StoneForge as it is now, which any StoneForge loads (see <see cref="InDevelopment"/>).</summary>
    public string? StoneForge { get; }
    /// <summary>Whether it's a development build: its "stoneforge" is "latest".</summary>
    public bool InDevelopment => StoneForge != null && ModIdentity.IsLatest(StoneForge);
    /// <summary>Whether it asks for full access (<c>"trusted": true</c>): its own DLLs (any .dll in its folder, outside
    /// bin and obj - LiteNetLib, say), compiled against the whole of .NET, and none of StoneForge's checks on what it
    /// uses - networking, threads, files, reflection. It runs only once the player has allowed it in the Mods window,
    /// warned that it can do anything a program on their PC can.</summary>
    public bool Trusted { get; }
    /// <summary>The mods it needs (<c>"requires": ["othermod"]</c>), by ID: it loads after them, and only if they're all
    /// there and loaded - else the Mods window and the log say which is missing. It's compiled against them, so it can use
    /// their public types (<see cref="ModList.Get{T}(string)"/>); switching one off switches it off too, and switching it
    /// on switches them on.</summary>
    public IReadOnlyList<string> Requires { get; }
    /// <summary>The mods it loads after if they're there (<c>"after": ["othermod"]</c>), by ID - each is optional: one
    /// that isn't there is skipped. (Only <see cref="Requires"/> lets it use another mod's types.)</summary>
    public IReadOnlyList<string> After { get; }
    /// <summary>Steam account IDs or SteamID64 strings allowed to use this mod's development editor.
    /// An absent or empty contributors array grants no editor access.</summary>
    public IReadOnlyList<string> Contributors { get; }
    internal bool IsContributor(uint accountId) => accountId != 0 && Contributors.Any(id =>
        ModIdentity.TryContributorAccount(id, out uint contributor) && contributor == accountId);

    /// <summary>A mod's folder's mod.json (throws InvalidDataException saying what's wrong).</summary>
    internal static ModManifest Read(string folder) => new(ModIdentity.ReadManifest(folder));
}
