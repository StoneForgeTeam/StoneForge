using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StoneForge;
using StoneForge.Patcher;

public sealed class SmlEnhancedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sf-msle-" + Guid.NewGuid().ToString("N"));
    public SmlEnhancedTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    internal static byte[] Assembly(string name, string source, params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.Concat(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return output.ToArray();
    }

    internal static byte[] Package(byte[] assembly, bool enhanced, string? resource = null, byte[]? bytes = null, int resourceSection = 0)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(Encoding.UTF8.GetBytes("MSLMv0.13.2.0"));
        for (int i = 0; i < (enhanced ? 6 : 4); i++)
        {
            bool hasResource = resource != null && i == resourceSection;
            writer.Write(hasResource ? 1 : 0);
            if (hasResource)
            {
                var name = Encoding.UTF8.GetBytes(resource!);
                writer.Write(name.Length); writer.Write(name); writer.Write(0); writer.Write(bytes!.Length);
            }
        }
        if (resource != null) writer.Write(bytes!);
        writer.Write(assembly.Length); writer.Write(assembly);
        return output.ToArray();
    }

    private string WritePackage(bool enhanced, byte[]? assembly = null)
    {
        string path = Path.Combine(_root, "test.sml");
        File.WriteAllBytes(path, Package(assembly ?? Assembly("TestMod", "public class TestMod {}"), enhanced));
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Format_detection_reads_metadata_without_loading_assemblies(bool enhanced)
    {
        // Its constructor would throw if discovery instantiated the assembly's types.
        var assembly = Assembly("TestMod", "public class TestMod { static TestMod() { throw new System.Exception(); } }");
        var package = SmlPackageInspection.Read(WritePackage(enhanced, assembly));
        Assert.Equal(enhanced, package.EnhancedFormat);
        Assert.Equal(assembly, package.Assembly);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "TestMod");
    }

    [Fact]
    public void Legacy_format_references_to_new_MSL_members_require_Enhanced()
    {
        var baseline = Assembly("ModShardLauncher", "namespace ModShardLauncher { public static class Msl { public static void Existing() {} } }");
        var enhanced = Assembly("ModShardLauncher", "namespace ModShardLauncher { public static class Msl { public static void Existing() {} public static void EnhancedOnly() {} } }");
        string baselinePath = Path.Combine(_root, "baseline.dll");
        File.WriteAllBytes(baselinePath, baseline);
        foreach (bool requires in new[] { false, true })
        {
            var mod = Assembly("TestMod", "public class TestMod { public void Patch() { ModShardLauncher.Msl." + (requires ? "EnhancedOnly" : "Existing") + "(); } }",
                MetadataReference.CreateFromImage(enhanced));
            Assert.Equal(requires, SmlPackageInspection.RequiresEnhanced(SmlPackageInspection.Read(WritePackage(false, mod)), baselinePath));
        }
    }

    [Fact]
    public void Truncated_or_unbounded_packages_fail_closed()
    {
        string path = WritePackage(true);
        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^10]);
        Assert.Throws<InvalidDataException>(() => SmlPackageInspection.Read(path));
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("MSLMv0.13.2.0").Concat(BitConverter.GetBytes(int.MaxValue)).ToArray());
        Assert.Throws<InvalidDataException>(() => SmlPackageInspection.Read(path));
    }

    private (GameFolder Game, List<SmlPackage> Packages) SelectionFixture()
    {
        var game = new GameFolder(_root);
        Directory.CreateDirectory(game.Dotnet);
        string path = WritePackage(true);
        return (game, new() { new(SmlCatalog.Id(path), "test", path, SmlRuntimeSelection.Hash(path)) });
    }

    [Fact]
    public void Missing_runtime_and_forced_standard_are_actionable_errors()
    {
        var (game, packages) = SelectionFixture();
        Assert.Contains("EnhancedDirectory", Assert.Throws<DirectoryNotFoundException>(() => SmlRuntimeSelection.Select(game, packages)).Message);
        File.WriteAllText(Path.Combine(game.Dotnet, SmlRuntimeSelection.ConfigFile), "{\"Mode\":\"standard\"}");
        Assert.Contains("requires MSL Enhanced", Assert.Throws<InvalidOperationException>(() => SmlRuntimeSelection.Select(game, packages)).Message);
    }

    [Fact]
    public void Disabled_packages_are_not_inspected_or_executed()
    {
        var (game, packages) = SelectionFixture();
        File.WriteAllText(packages[0].Path, "broken");
        var choice = SmlRuntimeSelection.Select(game, packages.Select(p => p with { Enabled = false }));
        Assert.False(choice.Enhanced);
    }

    [Fact]
    public void Unsupported_enhanced_binaries_are_rejected_before_loading()
    {
        var (game, packages) = SelectionFixture();
        string runtime = Path.Combine(_root, "runtime");
        Directory.CreateDirectory(runtime);
        File.WriteAllText(Path.Combine(runtime, "ModShardLauncher.dll"), "not a supported build");
        File.WriteAllText(Path.Combine(game.Dotnet, SmlRuntimeSelection.ConfigFile), JsonSerializer.Serialize(new { EnhancedDirectory = runtime }));
        Assert.Throws<InvalidDataException>(() => SmlRuntimeSelection.Select(game, packages));
    }

    [Fact]
    public void Package_order_and_runtime_changes_invalidate_the_key()
    {
        var standard = new SmlRuntimeSelection(false, null, new());
        var ordered = standard with { PackageOrder = new[] { "z.sml", "A.sml" } };
        var packages = new List<SmlPackage> { new("a", "a", "A.sml", "a"), new("z", "z", "z.sml", "z"), new("b", "b", "b.sml", "b") };
        Assert.Equal(new[] { "z", "a", "b" }, ordered.OrderPackages(packages).Select(p => p.Id));
        Assert.NotEqual(standard.Key, ordered.Key);
        Assert.NotEqual(standard.Key, (standard with { Enhanced = true }).Key);
        Assert.NotEqual((standard with { Files = new() { ["dependency.dll"] = "first" } }).Key,
            (standard with { Files = new() { ["dependency.dll"] = "second" } }).Key);
    }

    [Fact]
    public void Changed_runtime_snapshot_is_rejected_before_execution()
    {
        string source = Path.Combine(_root, "ModShardLauncher.dll");
        File.WriteAllText(source, "first");
        var selection = new SmlRuntimeSelection(true, _root, new() { ["ModShardLauncher.dll"] = SmlRuntimeSelection.Hash(source) });
        File.WriteAllText(source, "other");
        string work = Path.Combine(_root, "work"); Directory.CreateDirectory(work);
        Assert.Throws<IOException>(() => selection.Stage(work));
    }
}
