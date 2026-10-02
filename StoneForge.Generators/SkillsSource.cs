using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static StoneForge.Generators.Emit;

namespace StoneForge.Generators;

// The game's skills -> StoneForge.GameSkills: each as an abstract class over the loader's ModSkill that bases a
// mod's skill on it (class ShockBolt : Jolt { public ShockBolt() : base("my_shock_bolt") { } }), what it is in its
// summary; and SkillColumn, the skills table's columns. (StoneForge.Patcher's SkillObjects finds mods' skills by
// these class names too: the object's id in PascalCase, less a "Skill" added for a clash.)
internal static class SkillsSource
{
    public static string Make(SkillTable table)
    {
        var sb = new StringBuilder(Header("the game's skills, to inherit; the skills table's columns"));
        sb.AppendLine("namespace StoneForge\n{");
        sb.AppendLine("/// <summary>A column of the game's table_skills_stats: a skill's stat or property.</summary>");
        sb.AppendLine("public enum SkillColumn\n{");
        var seen = new HashSet<string>();
        foreach (string column in table.Columns)
            if (seen.Add(Ident(column)))
                sb.AppendLine($"    {Ident(column)},");
        sb.AppendLine("}\n}\n");
        sb.AppendLine("namespace StoneForge.GameSkills\n{");
        var taken = new HashSet<string>(StringComparer.Ordinal) { "ModSkill", "SkillCast", "Skills", "SkillColumn" };
        int target = table.Column("Target"), kd = table.Column("KD"), mp = table.Column("MP"), range = table.Column("Range"), branch = table.Column("Branch");
        foreach (var (id, name, row) in table.Skills)
        {
            string cls = ClassName(id, taken);
            string summary = $"{name.Replace('_', ' ')}: {Field(row, target)}"
                + (Field(row, branch) is string b && b.Length > 0 && b != "none" ? $", {b}" : "")
                + $"; cooldown {Field(row, kd)}, energy {Field(row, mp)}"
                + (Field(row, range) is string r && r.Length > 0 && r != "0" ? $", range {r}" : "");
            sb.AppendLine($"/// <summary>The game's skill {Xml(summary)}. Inherit it for a skill of your own based on it - its targeting, cast,");
            sb.AppendLine($"/// cooldown and energy cost - with your own effect: <c>class MySkill : {cls} {{ public MySkill() : base(\"my_skill\") {{ }} }}</c>.</summary>");
            sb.AppendLine($"public abstract class {cls} : global::StoneForge.ModSkill");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>The game's id for it (its object, o_skill_&lt;id&gt;: what it's based on).</summary>");
            sb.AppendLine($"    public const string GameName = {Literal(id)};");
            sb.AppendLine($"    protected {cls}(string key) : base(key, GameName) {{ }}");
            sb.AppendLine("}\n");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Field(string[] row, int index) => index >= 0 && index < row.Length ? row[index].Trim() : "";

    // Its id in PascalCase ("active_defence" -> ActiveDefence); one taken gets "Skill" after it, then a number.
    private static string ClassName(string id, HashSet<string> taken)
    {
        string plain = string.Concat(Regex.Split(id, "[^A-Za-z0-9]+").Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        if (plain.Length == 0 || char.IsDigit(plain[0]))
            plain = "_" + plain;
        string cls = taken.Contains(plain) ? plain + "Skill" : plain;
        for (int n = 2; taken.Contains(cls); n++)
            cls = plain + "Skill" + n;
        taken.Add(cls);
        return cls;
    }
}
