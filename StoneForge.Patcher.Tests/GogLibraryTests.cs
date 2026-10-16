using StoneForge;

public sealed class GogLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sf-gog-" + Guid.NewGuid().ToString("N"));
    public GogLibraryTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private string Game(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "StoneShard.exe"), "fixture");
        File.WriteAllText(Path.Combine(path, "data.win"), "fixture");
        return path;
    }

    [Fact]
    public void Finds_offline_installs_without_galaxy_or_a_fixed_folder_name()
    {
        string game = Game("石质碎片 GOG");
        Assert.Equal(game, GogLibrary.FindStoneshard(new[] { game }));
    }

    [Fact]
    public void Stale_invalid_and_incomplete_entries_do_not_hide_a_valid_install()
    {
        string incomplete = Game("incomplete");
        File.Delete(Path.Combine(incomplete, "data.win"));
        string other = Game("unrelated");
        File.Delete(Path.Combine(other, "StoneShard.exe"));
        string game = Game("Stoneshard");
        Assert.Equal(game, GogLibrary.FindStoneshard(new[] { null, "", "relative/path", "C:\\bad\0path", _root, incomplete, other, game }));
    }

    [Fact]
    public void No_matching_installs_returns_null_for_manual_folder_selection()
        => Assert.Null(GogLibrary.FindStoneshard(new[] { _root, null, "" }));

    [Fact]
    public void Preserves_discovery_order_when_multiple_installs_exist()
    {
        string first = Game("first"), second = Game("second");
        Assert.Equal(first, GogLibrary.FindStoneshard(new[] { first, second }));
        Assert.Equal(new[] { first, second }, GogLibrary.FindAllStoneshard(new[] { first, second }));
    }
}
