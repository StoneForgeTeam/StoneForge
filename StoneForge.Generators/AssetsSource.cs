using System.Collections.Generic;
using System.Linq;
using System.Text;
using static StoneForge.Generators.Emit;

namespace StoneForge.Generators;

// assets.tsv (kind, index, name) -> enums GameObjectId, Sprite, Sound, Room: every asset by name, valued by its
// index.
internal static class AssetsSource
{
    private static readonly (string Kind, string Enum, string Summary)[] Kinds =
    {
        ("object", "GameObjectId", "Every object in the game, by name: its object index."),
        ("sprite", "Sprite", "Every sprite in the game."),
        ("sound", "Sound", "Every sound in the game."),
        ("room", "Room", "Every room in the game."),
    };

    public static string Make(string text)
    {
        var sb = new StringBuilder(Header("asset enums"));
        sb.AppendLine("namespace StoneForge;\n");
        var byKind = Rows(text).Where(r => r.Length >= 3).GroupBy(r => r[0]).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var (kind, enumName, summary) in Kinds)
        {
            sb.AppendLine($"/// <summary>{summary}</summary>");
            sb.AppendLine($"public enum {enumName}\n{{");
            var seen = new HashSet<string>();
            if (byKind.TryGetValue(kind, out var rows))
                foreach (var r in rows)
                {
                    string id = Ident(r[2]);
                    if (seen.Add(id))
                        sb.AppendLine($"    {id} = {r[1]},");
                }
            sb.AppendLine("}\n");
        }
        return sb.ToString();
    }
}
