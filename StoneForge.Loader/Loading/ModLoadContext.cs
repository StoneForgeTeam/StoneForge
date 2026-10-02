using System.Reflection;
using System.Runtime.Loader;

namespace StoneForge.Loader;

// A mod's own load context: collectible, so switching the mod off can unload its code. Its StoneForge.API
// reference resolves to ours (one API, shared with the loader); the framework comes from the default context.
internal sealed class ModLoadContext : AssemblyLoadContext
{
    private static readonly Assembly Api = typeof(IStoneMod).Assembly;

    public ModLoadContext(string name) : base("mod " + name, isCollectible: true) { }

    protected override Assembly? Load(AssemblyName name)
        => string.Equals(name.Name, Api.GetName().Name, StringComparison.OrdinalIgnoreCase) ? Api : null;
}
