using System.Text;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace StoneForge.DataDump;

/// <summary>The dump: plain text files, a record a line (what StoneForge.Generators reads):
/// <list type="bullet">
/// <item>assets.tsv - kind (object / sprite / sound / room), index, name: every asset.</item>
/// <item>scripts.tsv - name, argument count: every GML script.</item>
/// <item>objects.tsv - name, parent (blank: none), the variables its own events assign on itself (comma-separated).</item>
/// <item>events.tsv - object, code entry (gml_Object_&lt;object&gt;_&lt;event&gt;): every object event.</item>
/// <item>skills.tsv - skill object id (o_skill_&lt;id&gt;), its name in the skills table.</item>
/// <item>weapons.txt / armor.txt / consumables.txt / skills_stats.txt - the game's item and skill tables as they are
/// (;-separated rows, the header among them).</item>
/// </list>
/// And source.txt, what it was made from - so it's only made again when that changes.</summary>
internal static class Dump
{
    // (Bump when what's written changes, so existing dumps are made again.)
    private const int Format = 1;
    private const string Stamp = "source.txt";

    private static readonly (string Code, string File)[] Tables =
    {
        ("gml_GlobalScript_table_weapons", "weapons.txt"),
        ("gml_GlobalScript_table_armor", "armor.txt"),
        ("gml_GlobalScript_table_items_stats", "consumables.txt"),
        ("gml_GlobalScript_table_skills_stats", "skills_stats.txt"),
    };

    // Built-in instance variables: not an object's own.
    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "x", "y", "xprevious", "yprevious", "xstart", "ystart", "hspeed", "vspeed", "speed", "direction", "friction",
        "gravity", "gravity_direction", "image_index", "image_speed", "image_xscale", "image_yscale", "image_angle",
        "image_alpha", "image_blend", "image_number", "sprite_index", "sprite_width", "sprite_height", "mask_index",
        "depth", "layer", "visible", "solid", "persistent", "object_index", "id", "alarm", "bbox_left", "bbox_right",
        "bbox_top", "bbox_bottom", "path_index", "path_position", "path_speed", "timeline_index", "argument",
        "argument_count", "self", "other", "global",
    };

    /// <summary>Whether <paramref name="output"/> already holds a dump of this very file.</summary>
    public static bool IsCurrent(string source, string output)
    {
        string stamp = Path.Combine(output, Stamp);
        return File.Exists(stamp) && File.ReadAllText(stamp) == StampOf(source);
    }

    public static void Write(string source, string output)
    {
        Directory.CreateDirectory(output);
        // (The stamp goes last: an interrupted dump is made again next time.)
        File.Delete(Path.Combine(output, Stamp));
        UndertaleData data;
        using (var stream = File.OpenRead(source))
            data = UndertaleIO.Read(stream, (_, _) => { }, _ => { });
        using (data)
        {
            if (data.GameObjects.ByName("o_stonemod_gui") != null)
                throw new InvalidDataException($"{source} has StoneForge's patches in it, and there's no unpatched copy beside it "
                    + "(dotnet\\data_base.win) - verify the game's files in Steam, or pass --data with an unpatched data.win.");
            void Save(string file, StringBuilder text) => File.WriteAllText(Path.Combine(output, file), text.ToString());
            Save("assets.tsv", Assets(data));
            Save("scripts.tsv", Scripts(data));
            var (objects, events) = ObjectsAndEvents(data);
            Save("objects.tsv", objects);
            Save("events.tsv", events);
            Save("skills.tsv", Skills(data));
            foreach (var (code, file) in Tables)
                if (Table(data, code) is { } rows)
                    Save(file, rows);
                else
                    Console.WriteLine($"warning SFDD002: the game data has no {code}; {file} not written");
            Console.WriteLine($"StoneForge game data: {data.GameObjects.Count} objects, {data.Sprites.Count} sprites, "
                + $"{data.Sounds.Count} sounds, {data.Rooms.Count} rooms from {source}");
        }
        File.WriteAllText(Path.Combine(output, Stamp), StampOf(source));
    }

    private static string StampOf(string source)
    {
        var file = new FileInfo(source);
        return $"{Format}|{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
    }

    private static StringBuilder Assets(UndertaleData data)
    {
        var text = new StringBuilder();
        void Add<T>(string kind, IList<T> list, Func<T, string?> name)
        {
            for (int i = 0; i < list.Count; i++)
                if (name(list[i]) is { Length: > 0 } n)
                    text.Append(kind).Append('\t').Append(i).Append('\t').Append(n).Append('\n');
        }
        Add("object", data.GameObjects, o => o?.Name?.Content);
        Add("sprite", data.Sprites, s => s?.Name?.Content);
        Add("sound", data.Sounds, s => s?.Name?.Content);
        Add("room", data.Rooms, r => r?.Name?.Content);
        return text;
    }

    private static StringBuilder Scripts(UndertaleData data)
    {
        var text = new StringBuilder();
        foreach (var code in data.Code.Where(c => c.Name.Content.StartsWith("gml_Script_", StringComparison.Ordinal))
                     .OrderBy(c => c.Name.Content, StringComparer.Ordinal))
            text.Append(code.Name.Content["gml_Script_".Length..]).Append('\t').Append(code.ArgumentsCount).Append('\n');
        return text;
    }

    // Each object - its parent and the variables its events set on itself - and each of its events.
    private static (StringBuilder Objects, StringBuilder Events) ObjectsAndEvents(UndertaleData data)
    {
        var objects = new StringBuilder();
        var events = new StringBuilder();
        foreach (var obj in data.GameObjects.Where(o => o?.Name != null))
        {
            var vars = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var code in obj.Events.SelectMany(e => e).SelectMany(e => e.Actions).Select(a => a.CodeId).OfType<UndertaleCode>())
            {
                events.Append(obj.Name.Content).Append('\t').Append(code.Name.Content).Append('\n');
                foreach (var instr in code.Instructions)
                    if (instr.Kind == UndertaleInstruction.Opcode.Pop && instr.ValueVariable is { } target
                        && (instr.TypeInst == UndertaleInstruction.InstanceType.Self || target.InstanceType == UndertaleInstruction.InstanceType.Self)
                        && !Builtins.Contains(target.Name.Content))
                        vars.Add(target.Name.Content);
            }
            objects.Append(obj.Name.Content).Append('\t').Append(obj.ParentId?.Name?.Content ?? "").Append('\t').Append(string.Join(",", vars)).Append('\n');
        }
        return (objects, events);
    }

    // Each o_skill_<id> with an icon object (o_skill_<id>_ico), and its name in the skills table: the string its
    // Create sets skill to.
    private static StringBuilder Skills(UndertaleData data)
    {
        var text = new StringBuilder();
        foreach (var obj in data.GameObjects.Where(o => o?.Name != null && o.Name.Content.StartsWith("o_skill_", StringComparison.Ordinal)
                     && !o.Name.Content.EndsWith("_ico", StringComparison.Ordinal)))
        {
            string name = obj.Name.Content;
            if (data.GameObjects.ByName(name + "_ico") == null || data.Code.ByName($"gml_Object_{name}_Create_0") is not { } create)
                continue;
            string? lastString = null;
            foreach (var instr in create.Instructions)
            {
                if (instr.Kind == UndertaleInstruction.Opcode.Push && instr.ValueString?.Resource is { } pushed)
                    lastString = pushed.Content;
                else if (instr.Kind == UndertaleInstruction.Opcode.Pop && instr.ValueVariable?.Name?.Content == "skill" && lastString != null)
                {
                    text.Append(name["o_skill_".Length..]).Append('\t').Append(lastString).Append('\n');
                    break;
                }
            }
        }
        return text;
    }

    // A table script's rows: the ;-separated strings it pushes, as they are (null: no such script).
    private static StringBuilder? Table(UndertaleData data, string codeName)
    {
        if (data.Code.ByName(codeName) is not { } code)
            return null;
        var rows = new StringBuilder();
        foreach (var instr in code.Instructions)
            if (instr.Kind == UndertaleInstruction.Opcode.Push && instr.ValueString?.Resource is { } s && s.Content.Contains(';'))
                rows.Append(s.Content.Replace("\n", " ")).Append('\n');
        return rows;
    }
}
