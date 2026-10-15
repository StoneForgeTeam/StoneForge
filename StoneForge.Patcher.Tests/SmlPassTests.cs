using StoneForge;
using StoneForge.Patcher;

// MSL patching in passes: packages needing MSL Enhanced first, then those standard MSL runs, on its output - a
// mix no longer puts every package on MSL Enhanced, where a standard package's types don't exist.
public sealed class SmlPassTests
{
    private static SmlPackage Package(string name, bool enabled = true) => new(name, name, @"C:\mods\" + name + ".sml", "hash", enabled);

    [Fact]
    public void A_mix_runs_enhanced_packages_first_then_standard_ones()
    {
        var katana = Package("katana");
        var other = Package("chinese");
        var off = Package("off", enabled: false);
        var selection = new SmlRuntimeSelection(true, @"C:\msle", new(), NeedsEnhanced: new(StringComparer.OrdinalIgnoreCase) { other.Path });

        var passes = selection.Passes(new[] { katana, other, off });

        Assert.Equal(2, passes.Count);
        Assert.True(passes[0].Runtime.Enhanced);
        Assert.Equal(new[] { other }, passes[0].Packages);
        Assert.False(passes[1].Runtime.Enhanced);
        Assert.Equal(new[] { katana }, passes[1].Packages);
    }

    [Fact]
    public void One_runtime_for_all_is_one_pass()
    {
        var a = Package("a");
        var b = Package("b");
        Assert.Single(new SmlRuntimeSelection(false, null, new()).Passes(new[] { a, b }));
        var allEnhanced = new SmlRuntimeSelection(true, @"C:\msle", new(), NeedsEnhanced: new() { a.Path, b.Path });
        Assert.Single(allEnhanced.Passes(new[] { a, b }));
        // (msl-runtime.json Mode "enhanced": everything on it.)
        var forced = new SmlRuntimeSelection(true, @"C:\msle", new(), NeedsEnhanced: new() { a.Path }, Forced: true);
        var pass = Assert.Single(forced.Passes(new[] { a, b }));
        Assert.Equal(2, pass.Packages.Count);
    }
}
