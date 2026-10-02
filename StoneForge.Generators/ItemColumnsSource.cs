using System.Collections.Generic;
using System.Text;
using static StoneForge.Generators.Emit;

namespace StoneForge.Generators;

// The item tables' headers -> WeaponColumn / ArmorColumn / ConsumableColumn: the columns an item's Set takes, as
// the game names them.
internal static class ItemColumnsSource
{
    public static string Make(ItemTable weapons, ItemTable armor, ConsumableTable consumables)
    {
        var sb = new StringBuilder(Header("the columns of the game's item tables"));
        sb.AppendLine("namespace StoneForge;\n");
        foreach (var (table, enumName, tableName) in new[] { (weapons, "WeaponColumn", "table_weapons"), (armor, "ArmorColumn", "table_armor") })
        {
            sb.AppendLine($"/// <summary>A column of the game's {tableName}: an item's stat or property.</summary>");
            sb.AppendLine($"public enum {enumName}\n{{");
            var seen = new HashSet<string>();
            foreach (string column in table.Columns)
                if (seen.Add(Ident(column)))
                    sb.AppendLine($"    {Ident(column)},");
            sb.AppendLine("}\n");
        }
        sb.AppendLine("/// <summary>A column of the game's table_items_stats: a consumable's stat or property.</summary>");
        sb.AppendLine("public enum ConsumableColumn\n{");
        var seenColumns = new HashSet<string>();
        foreach (string column in consumables.Columns)
            if (seenColumns.Add(Ident(column)))
                sb.AppendLine($"    {Ident(column)},");
        sb.AppendLine("}\n");
        return sb.ToString();
    }
}
