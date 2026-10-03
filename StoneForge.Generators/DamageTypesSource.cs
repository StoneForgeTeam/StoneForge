using System.Collections.Generic;
using System.Linq;
using System.Text;
using static StoneForge.Generators.Emit;

namespace StoneForge.Generators;

// The game's kinds of damage -> StoneForge.GameDamageTypes: a class each over the API's DamageType, to deal or to
// inherit (class Lightning : Shock { }), and DamageType.<Kind>, a shared one of each.
internal static class DamageTypesSource
{
    public static string Make(string tsv)
    {
        var kinds = Rows(tsv).Where(r => r.Length >= 2).Select(r => (Name: r[0], Resistance: r[1])).ToList();
        var sb = new StringBuilder(Header("the game's kinds of damage, to deal or inherit"));
        sb.AppendLine("namespace StoneForge.GameDamageTypes\n{");
        foreach (var (name, resistance) in kinds)
        {
            string cls = Ident(name);
            sb.AppendLine($"/// <summary>The game's {Xml(name.ToLowerInvariant())} damage (its {Xml(name)}_Damage), resisted by {Xml(resistance)}. Deal it as");
            sb.AppendLine($"/// <c>DamageType.{cls}</c>; inherit it for a kind of damage of your own dealt as {Xml(name.ToLowerInvariant())} - its resistance and");
            sb.AppendLine($"/// effects - with your own name, amounts and reactions: <c>class Lightning : {cls} {{ }}</c>.</summary>");
            sb.AppendLine($"public class {cls} : global::StoneForge.DamageType");
            sb.AppendLine("{");
            sb.AppendLine($"    public {cls}() : base({Literal(name)}, {Literal(resistance)}) {{ }}");
            sb.AppendLine("}\n");
        }
        sb.AppendLine("}\n");
        sb.AppendLine("namespace StoneForge\n{");
        sb.AppendLine("public abstract partial class DamageType\n{");
        foreach (var (name, _) in kinds)
            sb.AppendLine($"    /// <summary>The game's {Xml(name.ToLowerInvariant())} damage.</summary>\n    public static readonly global::StoneForge.GameDamageTypes.{Ident(name)} {Ident(name)} = new();");
        sb.AppendLine("    /// <summary>Every kind of damage the game has, in its order.</summary>");
        sb.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<DamageType> GameTypes { get; } = new DamageType[] { "
            + string.Join(", ", kinds.Select(k => Ident(k.Name))) + " };");
        sb.AppendLine("}\n}");
        return sb.ToString();
    }
}
