using System.Collections.Generic;
using System.Linq;
using System.Text;
using static StoneForge.Generators.Emit;

namespace StoneForge.Generators;

// scripts.tsv (name, argument count) -> Scripts: every GML script as a Script, to hook or call.
internal static class ScriptsSource
{
    public static string Make(string text)
    {
        var sb = new StringBuilder(Header("scripts"));
        sb.AppendLine("namespace StoneForge;\n");
        sb.AppendLine("/// <summary>Every GML script in the game. Hook one with <c>Scripts.x.Before(...)</c> (and declare it:");
        sb.AppendLine("/// <c>[assembly: HookScript(nameof(Scripts.x))]</c>), call one with <c>Scripts.x.Call(...)</c>.</summary>");
        sb.AppendLine("public static partial class Scripts\n{");
        var seen = new HashSet<string>();
        foreach (var r in Rows(text).Where(r => r.Length >= 2))
        {
            string name = r[0];
            // (Not anonymous functions or constructors - the compiler's names for them.)
            if (name.Contains("anon") || name.Contains("@") || !seen.Add(name))
                continue;
            sb.AppendLine($"    public static readonly Script {Ident(name)} = new({Literal(name)}, {r[1]});");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }
}
