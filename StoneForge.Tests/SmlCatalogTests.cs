using System.Text.Json;
using StoneForge;

public sealed class SmlCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sf-sml-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_root, "mods.json");
    public SmlCatalogTests() => Directory.CreateDirectory(_root);
    private void Package(string name, string contents = "MSLM-test") => File.WriteAllText(Path.Combine(_root, name), contents);
    private void Settings(params string[] disabled) => File.WriteAllText(Config, JsonSerializer.Serialize(new { Disabled = disabled }));

    [Fact]
    public void Discovery_never_executes_packages_and_defaults_to_enabled()
    {
        Package("SomeMod.SML", "This is deliberately not executable or a valid package");
        var mod = Assert.Single(SmlCatalog.Read(_root, Config));
        Assert.Equal("sml:somemod.sml", mod.Id);
        Assert.True(mod.Enabled);
    }

    [Fact]
    public void Only_top_level_packages_are_discovered_in_stable_order()
    {
        Package("z.sml"); Package("A.sml"); Package("other.dll");
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(_root, "nested", "ignored.sml"), "ignored");
        Assert.Equal(new[] { "A", "z" }, SmlCatalog.Read(_root, Config).Select(p => p.Name));
    }

    [Fact]
    public void Disabled_switches_a_package_off_case_insensitively()
    {
        Package("a.sml"); Package("b.sml");
        Settings("SML:A.SML");
        var mods = SmlCatalog.Read(_root, Config);
        Assert.False(mods[0].Enabled);
        Assert.True(mods[1].Enabled);
        Settings();
        Assert.All(SmlCatalog.Read(_root, Config), p => Assert.True(p.Enabled));
    }

    [Fact]
    public void Same_length_same_timestamp_edit_invalidates_patch_fingerprint()
    {
        Package("a.sml", "aaa");
        var time = File.GetLastWriteTimeUtc(Path.Combine(_root, "a.sml"));
        string before = SmlCatalog.Fingerprint(SmlCatalog.Read(_root, Config));
        Package("a.sml", "bbb"); File.SetLastWriteTimeUtc(Path.Combine(_root, "a.sml"), time);
        Assert.NotEqual(before, SmlCatalog.Fingerprint(SmlCatalog.Read(_root, Config)));
    }

    [Fact]
    public void Disabling_or_removing_last_package_invalidates_patch_fingerprint()
    {
        Package("a.sml");
        string before = SmlCatalog.Fingerprint(SmlCatalog.Read(_root, Config));
        Settings("sml:a.sml");
        string off = SmlCatalog.Fingerprint(SmlCatalog.Read(_root, Config));
        Assert.NotEqual(before, off);
        File.Delete(Path.Combine(_root, "a.sml"));
        Assert.Equal(off, SmlCatalog.Fingerprint(SmlCatalog.Read(_root, Config)));
    }

    [Fact]
    public void Invalid_settings_file_fails()
    {
        Package("a.sml"); File.WriteAllText(Config, "broken");
        Assert.ThrowsAny<JsonException>(() => SmlCatalog.Read(_root, Config));
    }

    [Fact]
    public void No_packages_does_not_change_existing_config_error_handling()
    {
        File.WriteAllText(Config, "broken");
        Assert.Empty(SmlCatalog.Read(_root, Config));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
