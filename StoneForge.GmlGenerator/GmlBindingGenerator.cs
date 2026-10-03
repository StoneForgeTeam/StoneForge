using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace StoneForge.Gml;

// A mod's GML bindings, from its GML\**\*.gml (AdditionalFiles): <ModFolder>.Gml, named after the folder holding
// the GML folder - the same at runtime (StoneForge's loader) and in an IDE. A static partial class: Gml.g.cs, then
// a file per GML file named after it (GML\Add.gml: Add.g.cs). The mod can add to the class with a partial of its own.
[Generator]
public sealed class GmlBindingGenerator : ISourceGenerator
{
    private static readonly DiagnosticDescriptor Error = new DiagnosticDescriptor("SFGML001", "GML binding error", "{0}", "StoneForge", DiagnosticSeverity.Error, true);
    public void Initialize(GeneratorInitializationContext context) { }
    public void Execute(GeneratorExecutionContext context)
    {
        var mods = context.AdditionalFiles
            .Where(f => f.Path.EndsWith(".gml", StringComparison.OrdinalIgnoreCase) && GmlProject.ModFolderOf(f.Path) != null)
            .GroupBy(f => GmlProject.ModFolderOf(f.Path)!, StringComparer.OrdinalIgnoreCase);
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            string folderName = Path.GetFileName(mod.Key);
            try
            {
                string root = mod.Key + "/";
                var files = mod.Select(f => new KeyValuePair<string, string>(f.Path.Replace('\\', '/').Substring(root.Length), f.GetText(context.CancellationToken)!.ToString()));
                var project = GmlProject.Parse(folderName, files);
                if (context.Compilation.GetTypeByMetadataName(project.Binding) is { } existing && !IsPartial(existing))
                    throw new ArgumentException($"{project.Binding} is already a class of the mod's; the GML bindings go there - "
                        + "rename that class, or declare it 'public static partial class " + GmlProject.BindingClass + "' to add to them.");
                Add(context, hints, project, new[] { GmlProject.BindingClass }, project.BindingSource());
                foreach (var function in project.Functions)
                    Add(context, hints, project, PathInGmlFolder(function.Path), project.BindingSource(function));
            }
            catch (Exception e) { context.ReportDiagnostic(Diagnostic.Create(Error, Location.None, folderName + ": " + e.Message)); }
        }
    }

    // Every declaration of the mod's own Gml class is partial (so the bindings' parts join it).
    private static bool IsPartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences.All(r =>
        r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

    // A GML file's path in the GML folder, as names: GML/Fx/Add.gml -> Fx, Add.
    private static string[] PathInGmlFolder(string path)
    {
        var parts = path.Replace('\\', '/').Split('/').Skip(1).ToArray();
        parts[parts.Length - 1] = Path.GetFileNameWithoutExtension(parts[parts.Length - 1]);
        return parts;
    }

    // A part's file, named after its GML file (Add.g.cs); its folders (Fx.Add.g.cs), then the mod's (ExampleMod.Add.g.cs),
    // only when that name is already taken.
    private static void Add(GeneratorExecutionContext context, HashSet<string> hints, GmlProject project, string[] path, string source)
    {
        var names = Enumerable.Range(1, path.Length).Select(n => string.Join(".", path.Skip(path.Length - n)))
            .Append(project.Namespace + "." + string.Join(".", path)).Select(n => Clean(n) + ".g.cs").ToList();
        string? hint = names.FirstOrDefault(n => !hints.Contains(n));
        for (int i = 2; hint == null; i++)
            if (!hints.Contains(names.Last().Replace(".g.cs", "_" + i + ".g.cs")))
                hint = names.Last().Replace(".g.cs", "_" + i + ".g.cs");
        hints.Add(hint);
        context.AddSource(hint, SourceText.From(source, Encoding.UTF8));
    }

    private static string Clean(string name) => Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
}
