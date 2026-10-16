using System.Text.Json;
using StoneForge;

public sealed class EpicLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sf-epic-" + Guid.NewGuid().ToString("N"));
    private string Manifests => Path.Combine(_root, "Epic", "EpicGamesLauncher", "Data", "Manifests");
    public EpicLibraryTests() => Directory.CreateDirectory(Manifests);
    public void Dispose() => Directory.Delete(_root, true);

    private string Game(string name = "Stoneshard")
    {
        string path = Path.Combine(_root, "games", name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "StoneShard.exe"), "fixture");
        File.WriteAllText(Path.Combine(path, "data.win"), "fixture");
        return path;
    }

    [Fact]
    public void Finds_a_manifest_install_without_relying_on_its_display_name()
    {
        string game = Game("石质碎片");
        File.WriteAllText(Path.Combine(Manifests, "game.item"), JsonSerializer.Serialize(new { DisplayName = "石质碎片", InstallLocation = game }));
        Assert.Equal(game, EpicLibrary.FindStoneshard(_root));
    }

    [Fact]
    public void Skips_corrupt_unrelated_and_incomplete_installs()
    {
        File.WriteAllText(Path.Combine(Manifests, "0.item"), "{broken");
        File.WriteAllText(Path.Combine(Manifests, "1.item"), "{\"InstallLocation\":123}");
        File.WriteAllText(Path.Combine(Manifests, "2.item"), "[]");
        string incomplete = Game("incomplete");
        File.Delete(Path.Combine(incomplete, "data.win"));
        File.WriteAllText(Path.Combine(Manifests, "3.item"), JsonSerializer.Serialize(new { InstallLocation = incomplete }));
        Assert.Null(EpicLibrary.FindStoneshard(_root));
        string game = Game();
        File.WriteAllText(Path.Combine(Manifests, "4.item"), JsonSerializer.Serialize(new { InstallLocation = game }));
        Assert.Equal(game, EpicLibrary.FindStoneshard(_root));
    }

    [Fact]
    public void Falls_back_to_the_launcher_installation_list()
    {
        string game = Game();
        string directory = Path.Combine(_root, "Epic", "UnrealEngineLauncher");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "LauncherInstalled.dat"), JsonSerializer.Serialize(new
        {
            InstallationList = new[] { new { InstallLocation = game } }
        }));
        Assert.Equal(game, EpicLibrary.FindStoneshard(_root));
    }

    [Fact]
    public void Discovers_all_copies_across_both_manifest_formats()
    {
        string first = Game("first"), second = Game("second"), third = Game("third");
        File.WriteAllText(Path.Combine(Manifests, "1.item"), JsonSerializer.Serialize(new { InstallLocation = first }));
        File.WriteAllText(Path.Combine(Manifests, "2.item"), JsonSerializer.Serialize(new { InstallLocation = second }));
        string directory = Path.Combine(_root, "Epic", "UnrealEngineLauncher");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "LauncherInstalled.dat"), JsonSerializer.Serialize(new
        {
            InstallationList = new[] { new { InstallLocation = third } }
        }));
        Assert.Equal(new[] { first, second, third }, EpicLibrary.FindAllStoneshard(_root));
    }

    [Fact]
    public void Discovers_every_steam_library_copy()
    {
        string first = Path.Combine(_root, "steam1"), second = Path.Combine(_root, "steam2");
        foreach (string library in new[] { first, second })
        {
            string game = Path.Combine(library, "steamapps", "common", "Stoneshard");
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "StoneShard.exe"), "fixture");
        }
        Assert.Equal(new[] { first, second }.Select(path => Path.Combine(path, "steamapps", "common", "Stoneshard")),
            SteamLibrary.FindAllStoneshard(new[] { first, second }));
    }

    [Fact]
    public void Missing_or_corrupt_launcher_files_do_not_block_manual_installation()
    {
        Assert.Null(EpicLibrary.FindStoneshard(_root));
        string directory = Path.Combine(_root, "Epic", "UnrealEngineLauncher");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "LauncherInstalled.dat"), "null");
        Assert.Null(EpicLibrary.FindStoneshard(_root));
        File.WriteAllText(Path.Combine(directory, "LauncherInstalled.dat"), "broken");
        Assert.Null(EpicLibrary.FindStoneshard(_root));
    }
}
