using StoneForge.Patcher;
using UndertaleModLib;
using UndertaleModLib.Models;

public sealed class SmlAudioFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sf-msle-audio-" + Guid.NewGuid().ToString("N"));
    private GameFolder Game => new(_root, Path.Combine(_root, "user-data"));
    public SmlAudioFilesTests() => Directory.CreateDirectory(Game.Dotnet);
    public void Dispose() => Directory.Delete(_root, true);

    private static void Group(string path, byte value)
    {
        using var stream = new MemoryStream(Convert.FromBase64String("Rk9STQwAAABBVURPBAAAAAAAAAA="));
        using var data = UndertaleIO.Read(stream, (_, _) => { }, _ => { });
        data.EmbeddedAudio.Add(new UndertaleEmbeddedAudio { Data = new[] { value } });
        using var output = File.Create(path);
        UndertaleIO.Write(output, data);
    }

    private string Stage => Path.Combine(_root, "stage");
    private string Live => Path.Combine(_root, "audiogroup1.dat");
    private string Snapshot()
    {
        Directory.CreateDirectory(Stage);
        return Path.Combine(Stage, "audiogroup1.dat");
    }

    [Fact]
    public void Groups_rebuild_from_original_and_restore_when_mod_is_removed()
    {
        Group(Live, 1);
        string clean = SmlRuntimeSelection.Hash(Live);
        using (var audio = new SmlAudioFiles(Game, true))
        {
            audio.StageTo(CreateStage());
            Group(Path.Combine(Stage, "audiogroup1.dat"), 2);
            audio.Collect(Stage);
            audio.Commit();
        }
        Assert.NotEqual(clean, SmlRuntimeSelection.Hash(Live));
        using (var audio = new SmlAudioFiles(Game, true))
        {
            string other = Path.Combine(_root, "other"); Directory.CreateDirectory(other);
            audio.StageTo(other);
            Assert.Equal(clean, SmlRuntimeSelection.Hash(Path.Combine(other, "audiogroup1.dat")));
        }
        using (var audio = new SmlAudioFiles(Game)) audio.Commit();
        Assert.Equal(clean, SmlRuntimeSelection.Hash(Live));
        Assert.False(File.Exists(Path.Combine(Game.Dotnet, "msl-audio.json")));
    }

    private string CreateStage() { Directory.CreateDirectory(Stage); return Stage; }

    [Fact]
    public void Failed_data_commit_rolls_back_audio_and_state()
    {
        Group(Live, 1);
        string original = SmlRuntimeSelection.Hash(Live);
        using var audio = new SmlAudioFiles(Game, true);
        audio.StageTo(CreateStage());
        Group(Path.Combine(Stage, "audiogroup1.dat"), 2);
        audio.Collect(Stage);
        string data = Path.Combine(_root, "patched.win"); File.WriteAllText(data, "patched data");
        Directory.CreateDirectory(Game.Data); // Forces the final data replacement to fail after audio writes.
        Assert.Throws<UnauthorizedAccessException>(() => audio.Commit(data));
        Assert.Equal(original, SmlRuntimeSelection.Hash(Live));
        Assert.False(File.Exists(Path.Combine(Game.Dotnet, "msl-audio.json")));
        Assert.False(File.Exists(Path.Combine(Game.Dotnet, "msl-audio-base", "audiogroup1.dat")));
    }

    [Fact]
    public void New_audio_groups_are_removed_and_external_updates_are_preserved()
    {
        using (var audio = new SmlAudioFiles(Game, true))
        {
            Group(Snapshot(), 2); audio.Collect(Stage); audio.Commit();
        }
        using (var audio = new SmlAudioFiles(Game)) audio.Commit();
        Assert.False(File.Exists(Live));
        Group(Live, 1);
        using (var audio = new SmlAudioFiles(Game, true))
        {
            Group(Snapshot(), 2); audio.Collect(Stage); audio.Commit();
        }
        Group(Live, 3);
        string external = SmlRuntimeSelection.Hash(Live);
        using (var audio = new SmlAudioFiles(Game)) audio.Commit();
        Assert.Equal(external, SmlRuntimeSelection.Hash(Live));
    }

    [Fact]
    public void Audio_state_cannot_escape_the_game_directory()
    {
        File.WriteAllText(Path.Combine(Game.Dotnet, "msl-audio.json"), "{\"../audiogroup1.dat\":{\"Original\":false,\"Written\":\"x\"}}");
        Assert.Throws<InvalidDataException>(() => new SmlAudioFiles(Game));
    }

    [Fact]
    public void Fonts_and_shader_files_are_staged_then_restored_on_removal()
    {
        string live = Path.Combine(Game.UserData, "fonts", "test.ttf");
        Directory.CreateDirectory(Path.GetDirectoryName(live)!);
        File.WriteAllText(live, "original font");
        string font = Path.Combine(Stage, "resources", "fonts", "test.ttf");
        string shader = Path.Combine(Stage, "resources", "shaders", "test", "Type.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(font)!);
        Directory.CreateDirectory(Path.GetDirectoryName(shader)!);
        File.WriteAllText(font, "patched font"); File.WriteAllText(shader, "GLSL_ES");
        string before = SmlAudioFiles.Fingerprint(Game, true);
        using (var files = new SmlAudioFiles(Game, true))
        {
            files.Collect(Stage);
            Assert.Equal("original font", File.ReadAllText(live));
            files.Commit();
        }
        Assert.Equal("patched font", File.ReadAllText(live));
        Assert.NotEqual(before, SmlAudioFiles.Fingerprint(Game, true));
        using (var files = new SmlAudioFiles(Game)) files.Commit();
        Assert.Equal("original font", File.ReadAllText(live));
        Assert.False(File.Exists(Path.Combine(Game.UserData, "shaders", "test", "Type.txt")));
    }
}
