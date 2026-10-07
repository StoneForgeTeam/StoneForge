using System.Text.Json;
using StoneForge;
using StoneForge.Patcher;
using UndertaleModLib;

public sealed class SmlIntegrationTests
{
    // An actual, trusted .sml is opt-in. Never execute arbitrary packages found on the test machine.
    [SkippableFact]
    public void Real_package_composes_with_StoneForge_caches_and_removes_cleanly()
    {
        string? source = Environment.GetEnvironmentVariable("STONEFORGE_TEST_DATA");
        string? package = Environment.GetEnvironmentVariable("STONEFORGE_TEST_SML");
        Skip.If(!File.Exists(source) || !File.Exists(package), "Set STONEFORGE_TEST_DATA and STONEFORGE_TEST_SML to trusted VM fixtures.");
        string folder = Path.Combine(Path.GetTempPath(), "sf-sml-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        bool passed = false;
        var previousOutput = Console.Out;
        using var console = new StringWriter();
        Console.SetOut(console);
        try
        {
            var game = new GameFolder(folder);
            Directory.CreateDirectory(game.Dotnet);
            Directory.CreateDirectory(game.Mods);
            File.Copy(source!, game.Data);
            string mod = Path.Combine(game.Mods, Path.GetFileName(package!));
            File.Copy(package!, mod);
            string config = Path.Combine(game.Dotnet, "mods.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new { Allowed = new[] { "sml:" + Path.GetFileName(mod).ToLowerInvariant() } }));
            GameDataBuilder.Prepare(game);
            Assert.Contains("MSL: Reading preserved game data...", console.ToString());
            Assert.Contains("MSL: Saving patched game data...", console.ToString());
            Assert.Contains("MSL: Finished.", console.ToString());
            Assert.Contains("MSL: Checking patched game data...", console.ToString());
            Assert.True(File.Exists(game.BaseData), "Base missing after first prepare: " + folder);
            string metadataState = Path.Combine(game.Dotnet, SmlCatalog.StateFile);
            var prepared = JsonSerializer.Deserialize<SmlPrepared>(File.ReadAllText(metadataState))!;
            var details = Assert.Single(prepared.Metadata!);
            Assert.Equal(SmlCatalog.Id(mod), details.Key);
            Assert.Equal(Hash(mod), details.Value.Hash);
            Assert.False(string.IsNullOrWhiteSpace(details.Value.Name));
            using (var stream = File.OpenRead(game.Data))
            using (var data = UndertaleIO.Read(stream))
            {
                Assert.NotNull(data.GameObjects.ByName("o_stonemod_gui"));
                Assert.NotNull(data.GameObjects.ByName("o_msl_log"));
            }
            var stamp = File.GetLastWriteTimeUtc(game.Data);
            GameDataBuilder.Prepare(game);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(game.Data));
            Assert.True(File.Exists(game.BaseData), "Base missing after cached prepare: " + folder);

            // A broken replacement must not corrupt the previously usable game data or its key.
            string dataHash = Hash(game.Data), key = File.ReadAllText(game.DataKey);
            File.WriteAllText(mod, "invalid");
            Assert.ThrowsAny<Exception>(() => GameDataBuilder.Prepare(game));
            Assert.Equal(dataHash, Hash(game.Data));
            Assert.Equal(key, File.ReadAllText(game.DataKey));
            Assert.True(File.Exists(game.BaseData), "Base missing after failed prepare: " + folder);

            // Removing the last package rebuilds cleanly, with no baked-in MSL output used as input.
            File.Delete(mod);
            GameDataBuilder.Prepare(game);
            Assert.True(File.Exists(game.BaseData), "Base missing after removal prepare: " + folder);
            using (var stream = File.OpenRead(game.Data))
            using (var data = UndertaleIO.Read(stream))
            {
                Assert.NotNull(data.GameObjects.ByName("o_stonemod_gui"));
                Assert.Null(data.GameObjects.ByName("o_msl_log"));
            }
            Assert.Equal(Hash(source!), Hash(game.BaseData));
            // Cached details remain available when a package is disabled, without executing it again.
            File.Copy(package!, mod);
            File.WriteAllText(metadataState, JsonSerializer.Serialize(prepared));
            File.WriteAllText(config, JsonSerializer.Serialize(new { Disabled = new[] { SmlCatalog.Id(mod) } }));
            File.Delete(game.DataKey);
            GameDataBuilder.Prepare(game);
            var disabled = JsonSerializer.Deserialize<SmlPrepared>(File.ReadAllText(metadataState))!;
            Assert.Empty(disabled.Applied);
            Assert.Equal(details.Value, disabled.Metadata![details.Key]);
            passed = true;
        }
        finally { Console.SetOut(previousOutput); if (passed) Directory.Delete(folder, true); }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
