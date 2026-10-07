using System.Reflection;
using StoneForge;
using StoneForge.Loader;

// Mods that need other mods (mod.json "requires", "after"): read, put in order, and compiled and loaded against the
// mods they require - whose public types they then use as their own.
public class ModDependencyTests
{
    private static ModManifest Manifest(string id, string extra = "")
        => new(ModIdentity.ParseManifest($$"""{ "id": "{{id}}", "name": "{{id}}", "version": "1"{{extra}} }"""));

    [Fact]
    public void Requires_and_after_are_lists_of_mod_ids()
    {
        var manifest = ModIdentity.ParseManifest("""{ "id": "m", "name": "M", "version": "1", "requires": ["core", "core"], "after": ["extra"] }""");
        Assert.Equal(new[] { "core" }, manifest.Requires);
        Assert.Equal(new[] { "extra" }, manifest.After);
        Assert.Empty(Manifest("m").Requires);
        Assert.Empty(Manifest("m").After);
    }

    [Theory]
    [InlineData("""{ "id": "m", "name": "M", "version": "1", "requires": "core" }""")] // not a list
    [InlineData("""{ "id": "m", "name": "M", "version": "1", "requires": ["Core Mod"] }""")] // not an ID
    [InlineData("""{ "id": "m", "name": "M", "version": "1", "after": [1] }""")] // not a string
    [InlineData("""{ "id": "m", "name": "M", "version": "1", "requires": ["m"] }""")] // itself
    public void Bad_dependencies_are_refused(string json)
        => Assert.Throws<InvalidDataException>(() => ModIdentity.ParseManifest(json));

    [Fact]
    public void Mods_load_after_what_they_require_and_load_after_else_in_folder_order()
    {
        var mods = new ModManifest?[]
        {
            Manifest("a", """, "requires": ["c"]"""),
            Manifest("b"),
            Manifest("c", """, "after": ["d", "notthere"]"""),
            null,
            Manifest("d"),
        };
        var result = LoadOrder.Sort(mods);
        Assert.Equal(new[] { 1, 3, 4, 2, 0 }, result.Order);
        Assert.Empty(result.Problems);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Requires_in_a_loop_cant_load_and_an_after_loop_is_ignored()
    {
        var looped = LoadOrder.Sort(new ModManifest?[]
        {
            Manifest("a", """, "requires": ["b"]"""),
            Manifest("b", """, "requires": ["a"]"""),
            Manifest("c"),
        });
        Assert.Equal(new[] { 2, 0, 1 }, looped.Order);
        Assert.Equal(new[] { 0, 1 }, looped.Problems.Keys.Order());

        var after = LoadOrder.Sort(new ModManifest?[]
        {
            Manifest("a", """, "after": ["b"]"""),
            Manifest("b", """, "after": ["a"]"""),
        });
        Assert.Equal(new[] { 0, 1 }, after.Order);
        Assert.Empty(after.Problems);
        Assert.Single(after.Warnings);
    }

    [Fact]
    public void Everything_a_mod_requires_comes_with_it_first()
    {
        var mods = new[] { Manifest("top", """, "requires": ["mid"]"""), Manifest("mid", """, "requires": ["base"]"""), Manifest("base") };
        Assert.Equal(new[] { "base", "mid" }, LoadOrder.AllRequired(mods[0], id => mods.FirstOrDefault(m => m.Id == id)));
    }

    private const string CoreSource = """
        using System.Collections.Generic;
        using StoneForge;

        namespace CoreMod;

        public class Core : IStoneMod
        {
            public readonly List<string> Names = new();
            public void Load(ModContext context) { }
            public void Unload() { }
            public void Register(string name) => Names.Add(name);
        }
        """;

    private const string AddonSource = """
        using StoneForge;

        namespace AddonMod;

        public class Addon : IStoneMod
        {
            public void Load(ModContext context) { }
            public void Unload() { }
            public static int Use(object core)
            {
                var typed = (CoreMod.Core)core;
                typed.Register("addon");
                return typed.Names.Count;
            }
        }
        """;

    [Fact]
    public void A_mod_uses_the_types_of_one_it_requires_as_loaded()
    {
        string core = Folder(CoreSource), addon = Folder(AddonSource);
        var coreContext = new ModLoadContext("core");
        ModLoadContext? addonContext = null;
        try
        {
            var coreBuild = ModCompiler.Compile(core);
            Assert.True(coreBuild.Assembly != null, string.Join("; ", coreBuild.Errors));
            // (Without it, its types can't even be named.)
            Assert.Null(ModCompiler.Compile(addon).Assembly);
            var addonBuild = ModCompiler.Compile(addon, required: new[] { coreBuild.Assembly! });
            Assert.True(addonBuild.Assembly != null, string.Join("; ", addonBuild.Errors));

            var coreAssembly = coreContext.LoadFromStream(new MemoryStream(coreBuild.Assembly!));
            var coreMod = Activator.CreateInstance(coreAssembly.GetType("CoreMod.Core")!)!;
            addonContext = new ModLoadContext("addon", mods: new[] { coreAssembly });
            var addonAssembly = addonContext.LoadFromStream(new MemoryStream(addonBuild.Assembly!));
            var use = addonAssembly.GetType("AddonMod.Addon")!.GetMethod("Use", BindingFlags.Public | BindingFlags.Static)!;
            // (The very mod running: what it registered is in the core mod's own list.)
            Assert.Equal(1, use.Invoke(null, new[] { coreMod }));
        }
        finally
        {
            addonContext?.Unload();
            coreContext.Unload();
            Directory.Delete(core, true);
            Directory.Delete(addon, true);
        }
    }

    [Fact]
    public void A_mod_finds_another_running_by_its_id()
    {
        var mods = new ModList();
        var other = new Other();
        ModList.Add(Manifest("deps_other"), other);
        try
        {
            Assert.Same(other, mods.Get("deps_other"));
            Assert.Same(other, mods.Get<Other>("deps_other"));
            Assert.Same(other, mods.Get<Other>());
            Assert.True(mods.IsLoaded("deps_other"));
            Assert.Equal("deps_other", mods.Manifest("deps_other")!.Id);
            Assert.Null(mods.Get("deps_missing"));
        }
        finally { ModList.RemoveMod("deps_other"); }
        Assert.False(mods.IsLoaded("deps_other"));
    }

    private sealed class Other : IStoneMod
    {
        public void Load(ModContext context) { }
        public void Unload() { }
    }

    private static string Folder(string source)
    {
        string folder = Path.Combine(Path.GetTempPath(), "StoneForgeDeps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Mod.cs"), source);
        return folder;
    }
}
