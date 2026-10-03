using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StoneForge.Gml;
using Microsoft.CodeAnalysis.Text;

namespace StoneForge.Loader;

// Mods are C# source - every .cs file in mods\<mod>\ (and its subfolders) - compiled here with Roslyn when the
// game starts: against a few assemblies only (the core types, collections, LINQ, Regex, and this loader), with
// unsafe code off, and checked by ModSecurity before the result is ever loaded. A mod that doesn't compile or
// isn't allowed isn't loaded; why is logged and shown in the Mods window. A trusted mod (mod.json "trusted": true)
// is compiled against the whole framework and its own DLLs instead, unchecked: it can do anything.
internal static class ModCompiler
{
    internal sealed record Result(byte[]? Assembly, List<string> Errors);

    // What mod source is compiled against. (Anything not referenced can't even be named; ModSecurity then
    // narrows what's in these.)
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Linq",
            "System.Text.RegularExpressions", "System.Memory",
        };
        var refs = new List<MetadataReference>();
        string tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (wanted.Contains(Path.GetFileNameWithoutExtension(path)))
                refs.Add(MetadataReference.CreateFromFile(path));
        // (No platform list: from the runtime's own folder.)
        if (refs.Count == 0)
        {
            string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            foreach (string name in wanted)
            {
                string path = Path.Combine(runtimeDir, name + ".dll");
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
        }
        refs.Add(MetadataReference.CreateFromFile(typeof(IStoneMod).Assembly.Location));
        return refs.ToImmutableArray();
    });

    // A trusted mod's: the whole framework (every platform assembly), and StoneForge.API.
    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedReferences = new(() =>
    {
        string api = typeof(IStoneMod).Assembly.Location;
        var refs = new List<MetadataReference> { MetadataReference.CreateFromFile(api) };
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileNameWithoutExtension(api) };
        string tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        var paths = tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (paths.Count == 0)
            paths = Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll").ToList();
        foreach (string path in paths)
            if (names.Add(Path.GetFileNameWithoutExtension(path)) && IsManaged(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        return refs.ToImmutableArray();
    });

    /// <summary>A trusted mod's own DLLs: every .dll in its folder (bin and obj aside) - managed ones are referenced
    /// when it's compiled and loaded with it (ModLoadContext), native ones loaded when its code asks for them.</summary>
    internal static List<string> Libraries(string folder) => Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories)
        .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

    // Whether a DLL is a .NET assembly (not a native one).
    internal static bool IsManaged(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
            return reader.HasMetadata;
        }
        catch { return false; }
    }

    /// <summary>A mod's .cs files (bin and obj folders aside), in a stable order.</summary>
    internal static List<string> SourceFiles(string folder) => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
        .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

    internal static Result Compile(string folder, bool trusted = false)
    {
        var files = SourceFiles(folder);
        if (files.Count == 0)
            return new Result(null, new List<string> { "no .cs files" });
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.None);
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f)).ToList();
        // (Each mod its own assembly name, so two mods' types never clash.)
        string assemblyName = "StoneMod_" + new string(Path.GetFileName(folder).Where(char.IsLetterOrDigit).ToArray()) + "_" + Guid.NewGuid().ToString("N")[..8];
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: trusted,
            nullableContextOptions: NullableContextOptions.Enable,
            metadataImportOptions: MetadataImportOptions.Public);
        var references = trusted
            ? TrustedReferences.Value.AddRange(Libraries(folder).Where(IsManaged).Select(f => MetadataReference.CreateFromFile(f)))
            : References.Value;
        Compilation compilation = CSharpCompilation.Create(assemblyName, trees, references, options);
        // (Its GML\**\*.gml, for its bindings: <Folder>.Gml.)
        var additional = GmlCatalog.Files(folder).Select(path => (AdditionalText)new GmlText(Path.GetFullPath(path))).ToArray();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new GmlBindingGenerator() }, additional, parse);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out compilation, out var generatorErrors);
        if (generatorErrors.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new Result(null, generatorErrors.Where(d => d.Severity == DiagnosticSeverity.Error).Select(Format).ToList());

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(Format).Take(20).ToList();
        if (errors.Count > 0)
            return new Result(null, errors);
        var problems = trusted ? new List<string>() : ModSecurity.Check((CSharpCompilation)compilation);
        if (problems.Count > 0)
            return new Result(null, problems.Take(20).Prepend("not allowed:").ToList());

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
            return new Result(null, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(Format).Take(20).ToList());
        return new Result(stream.ToArray(), new List<string>());
    }

    private static string Format(Diagnostic d)
    {
        var span = d.Location.GetLineSpan();
        return d.Location.IsInSource ? $"{Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1}): {d.GetMessage()}" : d.GetMessage();
    }

    private sealed class GmlText(string path) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(File.ReadAllText(path));
    }
}
