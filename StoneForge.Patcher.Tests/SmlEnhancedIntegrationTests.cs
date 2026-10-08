using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Buffers.Binary;
using Microsoft.CodeAnalysis;
using StoneForge;
using StoneForge.Patcher;
using UndertaleModLib;

public sealed class SmlEnhancedIntegrationTests
{
    private static byte[] ResourcePackage(string runtime)
    {
        string modSource = "using UndertaleModLib; using ModShardLauncher; public class EnhancedFixture : ModShardLauncher.Mods.Mod { " +
            "public override void PatchMod() { var page = DataLoader.data.Sprites.ByName(\"s_sf_msle_test\").Textures[0].Texture; " +
            "DataLoader.PendingTexturePatches[\"fixture\"] = (DataLoader.data.EmbeddedTextures.IndexOf(page.TexturePage), " +
            "System.Convert.FromBase64String(\"" + Convert.ToBase64String(Png()) + "\"), page.SourceX, page.SourceY, page.SourceWidth, page.SourceHeight); } }";
        var assembly = SmlEnhancedTests.Assembly("EnhancedFixture", modSource,
            MetadataReference.CreateFromFile(Path.Combine(runtime, "ModShardLauncher.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "UndertaleModLib.dll")),
            MetadataReference.CreateFromFile(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.ObjectModel.dll")),
            MetadataReference.CreateFromFile(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")));
        var resources = new[]
        {
            (Section: 0, Name: "EnhancedFixture/Sprites/s_sf_msle_test.png", Bytes: Png()),
            (Section: 0, Name: "EnhancedFixture/Sprites/s_settings_button_down.png", Bytes: Png()),
            (Section: 3, Name: "EnhancedFixture/Sounds/voice/sf_test.wav", Bytes: Wave()),
            (Section: 4, Name: "EnhancedFixture/Fonts/sf_test.ttf", Bytes: Encoding.UTF8.GetBytes("font import fixture")),
            (Section: 5, Name: "EnhancedFixture/Shaders/sf_test/Type.txt", Bytes: Encoding.UTF8.GetBytes("GLSL_ES")),
            (Section: 5, Name: "EnhancedFixture/Shaders/sf_test/GLSL_ES_Fragment.txt", Bytes: Encoding.UTF8.GetBytes("void main() { gl_FragColor = vec4(1.0); }")),
            (Section: 5, Name: "EnhancedFixture/Shaders/sf_test/GLSL_ES_Vertex.txt", Bytes: Encoding.UTF8.GetBytes("void main() { gl_Position = vec4(0.0); }"))
        };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.UTF8.GetBytes("MSLMv0.13.2.0"));
        int offset = 0;
        for (int section = 0; section < 6; section++)
        {
            var chunks = resources.Where(r => r.Section == section).ToArray();
            writer.Write(chunks.Length);
            foreach (var chunk in chunks)
            {
                var name = Encoding.UTF8.GetBytes(chunk.Name);
                writer.Write(name.Length); writer.Write(name); writer.Write(offset); writer.Write(chunk.Bytes.Length);
                offset += chunk.Bytes.Length;
            }
        }
        foreach (var chunk in resources) writer.Write(chunk.Bytes);
        writer.Write(assembly.Length); writer.Write(assembly);
        return stream.ToArray();
    }

    private static byte[] Png()
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] bytes)
        {
            byte[] length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); output.Write(length);
            byte[] tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(bytes);
            uint crc = uint.MaxValue;
            foreach (byte value in tag.Concat(bytes))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); output.Write(length);
        }
        byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, 8); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 8);
        header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (int row = 0; row < 8; row++)
            {
                zlib.WriteByte(0);
                for (int column = 0; column < 8; column++) zlib.Write(new byte[] { 255, 0, 0, 255 });
            }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static byte[] Wave()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(38); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(2); writer.Write((short)0);
        return stream.ToArray();
    }

    [SkippableFact]
    public void Enhanced_resources_auto_select_stage_cache_and_restore()
    {
        string? source = Environment.GetEnvironmentVariable("STONEFORGE_TEST_DATA");
        string? runtime = Environment.GetEnvironmentVariable("STONEFORGE_TEST_MSLE");
        Skip.If(!File.Exists(source) || !Directory.Exists(runtime), "Set trusted STONEFORGE_TEST_DATA and STONEFORGE_TEST_MSLE fixtures.");
        string folder = Path.Combine(Path.GetTempPath(), "sf-msle-resources-" + Guid.NewGuid().ToString("N"));
        var game = new GameFolder(folder, Path.Combine(folder, "user-data"));
        Directory.CreateDirectory(game.Dotnet); Directory.CreateDirectory(game.Mods);
        File.Copy(source!, game.Data);
        File.WriteAllText(Path.Combine(game.Dotnet, SmlRuntimeSelection.ConfigFile), JsonSerializer.Serialize(new { EnhancedDirectory = runtime }));
        string package = Path.Combine(game.Mods, "EnhancedFixture.sml");
        File.WriteAllBytes(package, ResourcePackage(runtime!));
        bool passed = false;
        try
        {
            GameDataBuilder.Prepare(game);
            Assert.Contains("Applied 1 queued texture replacement(s)", File.ReadAllText(Path.Combine(game.Dotnet, "msl-patch.log")));
            using (var stream = File.OpenRead(game.Data))
            using (var data = UndertaleIO.Read(stream))
            {
                Assert.NotNull(data.Shaders.ByName("shd_sf_test"));
                Assert.NotNull(data.Sprites.ByName("s_sf_msle_test"));
                var sound = data.Sounds.ByName("snd_EnhancedFixture_sf_test");
                Assert.NotNull(sound);
                Assert.True(sound.GroupID > 0);
            }
            string audio = Assert.Single(Directory.EnumerateFiles(game.Dir, "audiogroup*.dat"));
            Assert.True(File.Exists(Path.Combine(game.UserData, "fonts", "sf_test.ttf")));
            Assert.True(File.Exists(Path.Combine(game.UserData, "shaders", "sf_test", "Type.txt")));
            var prepared = JsonSerializer.Deserialize<SmlPrepared>(File.ReadAllText(Path.Combine(game.Dotnet, SmlCatalog.StateFile)))!;
            Assert.Equal("MSLE", Assert.Single(prepared.Metadata!).Value.Runtime);
            DateTime stamp = File.GetLastWriteTimeUtc(game.Data);
            GameDataBuilder.Prepare(game);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(game.Data));
            File.Delete(package);
            GameDataBuilder.Prepare(game);
            Assert.False(File.Exists(audio));
            Assert.False(File.Exists(Path.Combine(game.UserData, "fonts", "sf_test.ttf")));
            Assert.False(File.Exists(Path.Combine(game.UserData, "shaders", "sf_test", "Type.txt")));
            passed = true;
        }
        finally { if (passed) Directory.Delete(folder, true); }
    }
}
