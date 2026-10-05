namespace StoneForge;

/// <summary>The other mods running now (<see cref="ModContext.Mods"/>): one found by its ID, to use what it offers. A mod
/// that names another in <c>"requires"</c> is compiled against it, so it can use that mod's public types - its mod class
/// (<see cref="Get{T}(string)"/>) and anything else public in it; and it loads after it, so it's there in its Load. A mod
/// named only in <c>"after"</c> loads first too, if it's there, but its types can't be used: <see cref="Get(string)"/>
/// gives it as an <see cref="IStoneMod"/>. A mod switched off is gone from here (and the mods requiring it with it).</summary>
/// <example><code>
/// // mod.json: "requires": ["othermod"]
/// var other = context.Mods.Get&lt;OtherMod.OtherMod&gt;("othermod")!;
/// other.Register("from my mod");
/// </code></example>
public sealed class ModList
{
    private static readonly List<(ModManifest Manifest, IStoneMod Mod)> Running = new();

    internal ModList() { }

    /// <summary>A mod running now, by its ID; null if it isn't (not there, switched off, or it failed to load).</summary>
    public IStoneMod? Get(string id) => Running.FirstOrDefault(m => m.Manifest.Id == id).Mod;

    /// <summary>A mod running now by its ID, as its own mod class (<typeparamref name="T"/>, from a mod it
    /// <c>"requires"</c>); null if it isn't running, or isn't a <typeparamref name="T"/>.</summary>
    public T? Get<T>(string id) where T : class => Get(id) as T;

    /// <summary>The mod running now whose mod class is a <typeparamref name="T"/>; null if none is.</summary>
    public T? Get<T>() where T : class => Running.Select(m => m.Mod).OfType<T>().FirstOrDefault();

    /// <summary>Whether a mod (by its ID) is running now.</summary>
    public bool IsLoaded(string id) => Get(id) != null;

    /// <summary>A running mod's mod.json, by its ID; null if it isn't running.</summary>
    public ModManifest? Manifest(string id) => Running.FirstOrDefault(m => m.Manifest.Id == id).Manifest;

    /// <summary>Every mod running now (this one too, once its Load is done), in the order they loaded.</summary>
    public IReadOnlyList<ModManifest> All => Running.Select(m => m.Manifest).ToArray();

    // The loader's: a mod running (its Load done), and gone.
    internal static void Add(ModManifest manifest, IStoneMod mod) => Running.Add((manifest, mod));
    internal static void RemoveMod(string id) => Running.RemoveAll(m => m.Manifest.Id == id);
}
