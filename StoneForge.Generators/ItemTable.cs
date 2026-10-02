using System;
using System.Collections.Generic;
using System.Linq;

namespace StoneForge.Generators;

// One of the game's item tables (weapons.txt / armor.txt: table_weapons / table_armor as they are - a row a
// line, ;-separated, "name;Tier;id;Slot;..." the header among them), read for ItemColumnsSource and
// GameItemsSource.
internal sealed class ItemTable
{
    /// <summary>The loader's base class for its items ("Weapon" / "Armor"), and what to call one ("weapon" / "armour").</summary>
    public string BaseClass { get; }
    public string Kind { get; }
    public string[] Header { get; }
    /// <summary>Its items (not its header, comments, blank separators or section headings), as fields.</summary>
    public List<string[]> Items { get; }

    public ItemTable(string? text, string baseClass, string kind)
    {
        BaseClass = baseClass;
        Kind = kind;
        var rows = Emit.Lines(text).Select(l => l.Split(';')).ToList();
        Header = rows.FirstOrDefault(f => f.Length > 3 && f[3] == "Slot") ?? new string[0];
        int tier = Column("Tier"), slot = Column("Slot");
        // (Not an item: the header, a // comment, a blank separator, or a section heading - "2H MACES", with
        // neither a tier nor a slot.)
        Items = rows.Where(f =>
        {
            string name = f[0].Trim();
            return name.Length > 0 && !name.StartsWith("//") && !(Header.Length > 0 && name == Header[0])
                && (Field(f, tier).Length > 0 || Field(f, slot).Length > 0);
        }).ToList();
    }

    /// <summary>A column's index by its header name (-1: none).</summary>
    public int Column(string name) => Array.IndexOf(Header, name);

    /// <summary>A row's field, trimmed ("" past its end).</summary>
    public static string Field(string[] row, int index) => index >= 0 && index < row.Length ? row[index].Trim() : "";

    /// <summary>The columns as the game names them, as an item's Set takes them: all but the name and id, and
    /// not the blank separator columns.</summary>
    public IEnumerable<string> Columns => Header.Skip(1).Where((c, i) => c.Length > 0 && !(c == "id" && i == 1));
}
