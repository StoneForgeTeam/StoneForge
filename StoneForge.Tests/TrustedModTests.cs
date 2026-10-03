using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StoneForge.Loader;

// Trusted mods (mod.json "trusted": true): compiled against the whole framework and their own DLLs, unchecked, and
// loaded with those DLLs - what the sandbox refuses, they may use.
public class TrustedModTests
{
    private const string Source = """
        using StoneForge;

        namespace TrustedMod;

        public class M : IStoneMod
        {
            public void Load(ModContext context) { }
            public void Unload() { }
            // A socket (networking - refused in the sandbox) and its own library.
            public static string Run()
            {
                using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
                return TestLib.Greeter.Hi();
            }
        }
        """;

    [Fact]
    public void A_trusted_mod_uses_networking_and_its_own_DLL()
    {
        string folder = Mod();
        try
        {
            var result = ModCompiler.Compile(folder, trusted: true);
            Assert.True(result.Assembly != null, string.Join("; ", result.Errors));
            var context = new ModLoadContext("trusted", ModCompiler.Libraries(folder));
            try
            {
                var assembly = context.LoadFromStream(new MemoryStream(result.Assembly!));
                var run = assembly.GetType("TrustedMod.M")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
                Assert.Equal("hi from lib", run.Invoke(null, null));
            }
            finally { context.Unload(); }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void The_same_mod_untrusted_is_refused()
    {
        string folder = Mod();
        try { Assert.Null(ModCompiler.Compile(folder).Assembly); }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Build_output_is_not_a_library()
    {
        string folder = Mod();
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "bin"));
            File.Copy(Path.Combine(folder, "lib", "TestLib.dll"), Path.Combine(folder, "bin", "Other.dll"));
            Assert.Equal(new[] { "TestLib.dll" }, ModCompiler.Libraries(folder).Select(Path.GetFileName));
        }
        finally { Directory.Delete(folder, true); }
    }

    // A mod folder: Mod.cs, and lib\TestLib.dll (compiled here).
    private static string Mod()
    {
        string folder = Path.Combine(Path.GetTempPath(), "StoneForgeTrusted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "lib"));
        File.WriteAllText(Path.Combine(folder, "Mod.cs"), Source);
        var lib = CSharpCompilation.Create("TestLib",
            new[] { CSharpSyntaxTree.ParseText("namespace TestLib; public static class Greeter { public static string Hi() => \"hi from lib\"; }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emit = lib.Emit(Path.Combine(folder, "lib", "TestLib.dll"));
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics));
        return folder;
    }
}
