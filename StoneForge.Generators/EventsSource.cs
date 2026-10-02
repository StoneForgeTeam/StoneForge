using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static StoneForge.Generators.Emit;

namespace StoneForge.Generators;

// events.tsv (object, code entry) -> Events.<object>.<event>: every object event as a CodeEvent of its
// object's class, named by its code entry (gml_Object_<object>_<event>).
internal static class EventsSource
{
    public static string Make(string text)
    {
        var sb = new StringBuilder(Header("object events"));
        sb.AppendLine("namespace StoneForge;\n");
        sb.AppendLine("/// <summary>Every object event in the game: <c>Events.o_player.Step_0.After(context, player => ...)</c>.</summary>");
        sb.AppendLine("public static class Events\n{");
        // (In the dump's order: an object's events together.)
        foreach (var group in Rows(text).Where(r => r.Length >= 2).GroupBy(r => r[0]))
        {
            string name = group.Key, cls = Ident(name), prefix = "gml_Object_" + name + "_";
            sb.AppendLine($"    public static class {cls}\n    {{");
            var seen = new HashSet<string>();
            foreach (var r in group)
            {
                string codeName = r[1];
                string field = Ident(codeName.StartsWith(prefix, StringComparison.Ordinal) ? codeName.Substring(prefix.Length) : codeName);
                if (field == cls || !seen.Add(field))
                    continue;
                sb.AppendLine($"        public static readonly CodeEvent<global::StoneForge.Objects.{cls}> {field} = new({Literal(codeName)});");
            }
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }
}
