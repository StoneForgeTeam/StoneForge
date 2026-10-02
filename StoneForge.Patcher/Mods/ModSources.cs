using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StoneForge.Patcher;

/// <summary>The mods in &lt;game&gt;\mods, as source: their files, and the scripts they declare they hook.</summary>
internal static class ModSources
{
    /// <summary>A mod's source files (as the loader compiles them): every .cs in mods\&lt;mod&gt;\, bin and obj
    /// aside.</summary>
    public static IEnumerable<string> Files(string modsDir)
    {
        if (!Directory.Exists(modsDir))
            yield break;
        foreach (string folder in Directory.GetDirectories(modsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                if (!Path.GetRelativePath(folder, file).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
                    yield return file;
    }

    /// <summary>Each mod folder with a valid mod.json - its ID - and its source files (a folder without one isn't
    /// loaded, so it gets nothing in the game's data).</summary>
    public static IEnumerable<(string Folder, string ModId, List<string> Files)> Mods(string modsDir)
    {
        if (!Directory.Exists(modsDir))
            yield break;
        foreach (string folder in Directory.GetDirectories(modsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string id;
            try { id = ModIdentity.ReadManifest(folder).Id; }
            catch (Exception e)
            {
                PatcherConsole.Log($"  {Path.GetFileName(folder)}: {e.Message} - skipped");
                continue;
            }
            yield return (folder, id, Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Where(file => !Path.GetRelativePath(folder, file).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")).ToList());
        }
    }

    /// <summary>The scripts to make hookable: the loader's own (<see cref="ScriptHooks.LoaderHooks"/>) and those
    /// every mod declares - [assembly: HookScript("x")] or [assembly: HookScript(nameof(Scripts.x))] in its
    /// source - sorted, without duplicates. (Read from the syntax only: the loader compiles and checks the mods
    /// themselves.)</summary>
    public static List<string> DeclaredHooks(string modsDir)
    {
        var names = new SortedSet<string>(ScriptHooks.LoaderHooks, StringComparer.Ordinal);
        foreach (string file in Files(modsDir))
        {
            try
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot();
                foreach (var list in root.AttributeLists)
                {
                    if (list.Target == null || !list.Target.Identifier.IsKind(SyntaxKind.AssemblyKeyword))
                        continue;
                    foreach (var attribute in list.Attributes)
                        if (HookedScript(attribute) is string name)
                            names.Add(name);
                }
            }
            catch (Exception e)
            {
                PatcherConsole.Log($"  couldn't read {Path.GetFileName(file)}: {e.Message}");
            }
        }
        return names.ToList();
    }

    // The script a [HookScript(...)] attribute names: a string, or nameof(Scripts.scr_x) - its last name.
    private static string? HookedScript(AttributeSyntax attribute)
    {
        string attributeName = attribute.Name.ToString();
        if (!attributeName.EndsWith("HookScript", StringComparison.Ordinal) && !attributeName.EndsWith("HookScriptAttribute", StringComparison.Ordinal))
            return null;
        string? name = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } call
                => call.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString().Split('.').Last().Trim().TrimStart('@'),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
