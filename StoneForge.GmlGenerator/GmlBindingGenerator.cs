using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace StoneForge.Gml;

// A mod's GML bindings, from its GML\**\*.gml (AdditionalFiles): <ModFolder>.Gml, named after the folder holding
// the GML folder - the same at runtime (StoneForge's loader) and in an IDE.
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
        foreach (var mod in mods)
        {
            string folderName = Path.GetFileName(mod.Key);
            try
            {
                string root = mod.Key + "/";
                var files = mod.Select(f => new KeyValuePair<string, string>(f.Path.Replace('\\', '/').Substring(root.Length), f.GetText(context.CancellationToken)!.ToString()));
                var project = GmlProject.Parse(folderName, files);
                if (context.Compilation.GetTypeByMetadataName(project.Binding) != null)
                    throw new ArgumentException($"{project.Binding} is already a class of the mod's; the GML bindings go there - rename that class.");
                context.AddSource("Gml_" + GmlProject.Hash(project.Name) + ".g.cs", SourceText.From(project.Bindings(), Encoding.UTF8));
            }
            catch (Exception e) { context.ReportDiagnostic(Diagnostic.Create(Error, Location.None, folderName + ": " + e.Message)); }
        }
    }
}
