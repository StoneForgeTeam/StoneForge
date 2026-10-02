using System;
using System.Collections.Generic;
using System.Linq;

namespace StoneForge.Generators;

// The game's consumables table (consumables.txt: table_items_stats as it is - a row a line, ;-separated,
// "id;;Price;EffPrice;tier;Cat;..." the header among them), read for ItemColumnsSource and GameItemsSource:
// only the consumables that are items (with an o_inv_<id> object - objects.tsv), as the loader can base a mod's
// on them.
internal sealed class ConsumableTable
{
    public string[] Header { get; }
    /// <summary>Its items with an inventory object, as fields (the id first).</summary>
    public List<string[]> Items { get; }

    public ConsumableTable(string? text, string? objects)
    {
        var rows = Emit.Lines(text).Select(l => l.Split(';')).ToList();
        Header = rows.FirstOrDefault(f => f.Length > 3 && f[0] == "id") ?? new string[0];
        var objectNames = new HashSet<string>(Emit.Rows(objects ?? "").Select(r => r[0]), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Items = rows.Where(f =>
        {
            string id = f[0].Trim();
            return id.Length > 0 && id != "id" && !id.StartsWith("//") && objectNames.Contains("o_inv_" + id) && seen.Add(id);
        }).ToList();
    }

    public int Column(string name) => Array.IndexOf(Header, name);

    /// <summary>The columns as the game names them, as a consumable's Set takes them: all but the id, and not the
    /// blank separator columns.</summary>
    public IEnumerable<string> Columns => Header.Skip(1).Where(c => c.Length > 0);
}
