using System;
using System.Collections.Generic;
using System.Linq;

namespace StoneForge.Generators;

// The game's skills (skills.tsv: each o_skill_<id> with an icon object, and its name in the skills table) and their
// table (skills_stats.txt: table_skills_stats as it is - ";Object;Target;Range;KD;MP;..." its header), read for
// SkillsSource: the skills with a row - the ones a mod's can be based on.
internal sealed class SkillTable
{
    public string[] Header { get; }
    /// <summary>Each skill: its object's id ("active_defence"), its name in the table ("Active_Defence"), its row.</summary>
    public List<(string Id, string Name, string[] Row)> Skills { get; }

    public SkillTable(string? skills, string? stats)
    {
        var rows = Emit.Lines(stats).Select(l => l.Split(';')).ToList();
        Header = rows.FirstOrDefault(f => f.Length > 2 && f[1] == "Object") ?? new string[0];
        var byName = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var row in rows)
            if (row.Length > 2 && row[0].Length > 0 && !row[0].StartsWith("//") && row[1] != "Object" && !byName.ContainsKey(row[0]))
                byName[row[0]] = row;
        Skills = Emit.Rows(skills ?? "").Where(r => r.Length >= 2 && byName.ContainsKey(r[1]))
            .Select(r => (r[0], r[1], byName[r[1]])).ToList();
    }

    public int Column(string name) => Array.IndexOf(Header, name);

    /// <summary>The columns as the game names them, as a skill's Set takes them: all but the name (the first,
    /// unnamed), and not blank ones.</summary>
    public IEnumerable<string> Columns => Header.Skip(1).Where(c => c.Length > 0);
}
