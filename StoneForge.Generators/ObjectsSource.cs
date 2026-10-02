using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static StoneForge.Generators.Emit;

namespace StoneForge.Generators;

// objects.tsv (name, parent, own variables) -> StoneForge.Objects: a class per object, following the
// game's object inheritance (o_player : o_unit : ...), with a GmValue property for each variable its own
// events assign on itself - what it inherits comes from its parent's class.
internal static class ObjectsSource
{
    // Names already taken in GameInstance (and object): a variable with one gets a trailing underscore.
    private static readonly HashSet<string> Reserved = new()
    {
        "Instance", "Exists", "X", "Y", "XPrevious", "YPrevious", "Depth", "Visible", "ImageIndex", "ImageSpeed", "ImageXScale",
        "ImageYScale", "ImageAngle", "ImageAlpha", "SpriteIndex", "ObjectIndex", "Id", "IsA", "Destroy", "Get", "Set",
        "ToString", "Equals", "GetHashCode", "GetType", "Wrap",
    };

    public static string Make(string text)
    {
        var rows = Rows(text).ToList();
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownVars = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            parentOf[r[0]] = r.Length > 1 ? r[1] : "";
            ownVars[r[0]] = r.Length > 2 && r[2].Length > 0 ? r[2].Split(',') : new string[0];
        }

        var sb = new StringBuilder(Header("object classes"));
        sb.AppendLine("namespace StoneForge.Objects;\n");
        foreach (var r in rows)
        {
            string name = r[0], cls = Ident(name);
            string parent = parentOf[name].Length > 0 ? Ident(parentOf[name]) : "global::StoneForge.GameInstance";
            var inherited = Inherited(name, parentOf, ownVars);
            sb.AppendLine($"/// <summary>Instances of {name}.</summary>");
            sb.AppendLine($"public partial class {cls} : {parent}\n{{");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string v in ownVars[name])
            {
                if (inherited.Contains(v))
                    continue;
                string prop = Ident(v);
                // (get_/set_ names: C# reserves those for property accessors - a variable "set_x" next to "x" clashes.)
                if (Reserved.Contains(prop.TrimStart('@')) || prop == cls || prop.StartsWith("get_") || prop.StartsWith("set_"))
                    prop += "_";
                if (!used.Add(prop))
                    continue;
                sb.AppendLine($"    public global::StoneForge.GmValue {prop} {{ get => Get({Literal(v)}); set => Set({Literal(v)}, value); }}");
            }
            sb.AppendLine("}\n");
        }
        return sb.ToString();
    }

    // What an object already has from its ancestors (so a child doesn't declare it again).
    private static HashSet<string> Inherited(string obj, Dictionary<string, string> parentOf, Dictionary<string, string[]> ownVars)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (string p = parentOf[obj]; p.Length > 0 && parentOf.ContainsKey(p) && visited.Add(p); p = parentOf[p])
            all.UnionWith(ownVars[p]);
        return all;
    }
}
