using StoneForge;
using StoneForge.Patcher;

public sealed class InstallationSelectionTests
{
    private static GameInstallation[] Copies => new[]
    {
        new GameInstallation(@"C:\Games\Steam\Stoneshard", "Steam"),
        new GameInstallation(@"D:\Epic\Stoneshard", "Epic"),
        new GameInstallation(@"E:\GOG\Stoneshard", "GOG")
    };

    [Fact]
    public void Choice_shows_store_and_full_folder_and_reprompts_for_invalid_input()
    {
        var output = new List<string>();
        string? result = InstallationSelection.Choose(Copies, new StringReader("\nabc\n4\n2\n"), output.Add, true);
        Assert.Equal(Copies[1].Path, result);
        foreach (var copy in Copies)
            Assert.Contains(output, line => line.Contains(copy.Store) && line.Contains(copy.Path));
        Assert.Equal(3, output.Count(line => line == "Please enter one of the listed choices."));
    }

    [Theory]
    [InlineData("0\n")]
    [InlineData("")]
    public void Cancel_or_closed_input_stops_before_selecting_a_folder(string answer)
        => Assert.Throws<OperationCanceledException>(() => InstallationSelection.Choose(Copies, new StringReader(answer), _ => { }, true));

    [Fact]
    public void Manual_folder_choice_returns_to_the_folder_prompt()
        => Assert.Null(InstallationSelection.Choose(Copies, new StringReader("m\n"), _ => { }, true));

    [Fact]
    public void Single_installation_needs_no_prompt_including_automated_runs()
    {
        Assert.Equal(Copies[0].Path, InstallationSelection.Choose(Copies[..1], TextReader.Null, _ => throw new Exception("Unexpected prompt"), false));
        Assert.Null(InstallationSelection.Choose(Array.Empty<GameInstallation>(), TextReader.Null, _ => throw new Exception("Unexpected prompt"), false));
    }

    [Fact]
    public void Automated_runs_require_an_explicit_folder_when_ambiguous()
    {
        var error = Assert.Throws<InvalidOperationException>(() => InstallationSelection.Choose(Copies, TextReader.Null, _ => { }, false));
        Assert.Contains("Specify the game folder explicitly", error.Message);
        foreach (var copy in Copies)
            Assert.Contains(copy.Path, error.Message);
    }

    [Fact]
    public void Duplicate_folders_are_merged_across_stores_and_registry_views()
    {
        var results = GameInstallations.Combine(new[]
        {
            Copies[0], Copies[0] with { Path = Copies[0].Path.ToUpperInvariant() + "\\" },
            Copies[0] with { Store = "GOG" }, Copies[1]
        });
        Assert.Equal(2, results.Count);
        Assert.Equal("Steam / GOG", results[0].Store);
        Assert.Equal(Copies[0].Path, results[0].Path);
    }

    [Fact]
    public void Explicit_game_folder_bypasses_discovery()
        => Assert.Equal(Copies[2].Path, GameFolder.Locate(Copies[2].Path)!.Dir);
}
