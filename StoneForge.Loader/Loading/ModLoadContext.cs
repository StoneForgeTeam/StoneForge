using System.Reflection;
using System.Runtime.Loader;

namespace StoneForge.Loader;

// A mod's own load context: collectible, so switching the mod off can unload its code. Its StoneForge.API
// reference resolves to ours (one API, shared with the loader); the framework comes from the default context.
// A trusted mod's own DLLs (ModCompiler.Libraries) are loaded into it too - read into memory, so the files stay
// free to be replaced while the game runs - and its native ones from its folder. The mods it requires resolve to their
// code as loaded (in their own contexts), so it uses the very mods that are running.
internal sealed class ModLoadContext : AssemblyLoadContext
{
    private static readonly Assembly Api = typeof(IStoneMod).Assembly;
    private readonly Dictionary<string, string> _libraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Assembly> _mods = new(StringComparer.Ordinal);

    public ModLoadContext(string name, IEnumerable<string>? libraries = null, IEnumerable<Assembly>? mods = null) : base("mod " + name, isCollectible: true)
    {
        foreach (var mod in mods ?? Array.Empty<Assembly>())
            _mods.TryAdd(mod.GetName().Name!, mod);
        foreach (string path in libraries ?? Array.Empty<string>())
            _libraries.TryAdd(Path.GetFileNameWithoutExtension(path), path);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (string.Equals(name.Name, Api.GetName().Name, StringComparison.OrdinalIgnoreCase))
            return Api;
        if (name.Name != null && _mods.TryGetValue(name.Name, out Assembly? mod))
            return mod;
        if (name.Name != null && _libraries.TryGetValue(name.Name, out string? path) && ModCompiler.IsManaged(path))
            using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                return LoadFromStream(stream);
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string name = Path.GetFileNameWithoutExtension(unmanagedDllName);
        return _libraries.TryGetValue(name, out string? path) && !ModCompiler.IsManaged(path) ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
