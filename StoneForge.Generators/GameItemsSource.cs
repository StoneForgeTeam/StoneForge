using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static StoneForge.Generators.Emit;

namespace StoneForge.Generators;

// The item tables' rows -> StoneForge.GameItems: every weapon, armour and consumable as an abstract class over
// the loader's Weapon / Armor / Consumable that bases a mod's item on it (class MyBlade : DrifterSword {
// public MyBlade() : base("My Blade") { } }), its stats in its summary.
internal static class GameItemsSource
{
    // (Names the loader already uses, which an item's class mustn't take.)
    private static readonly string[] LoaderNames =
    {
        "Armor", "ArmorColumn", "Attack", "AttackResult", "BuffKind", "BuffStat", "Buffs", "CodeEvent", "Draw", "Effect", "Fx",
        "FxOptions", "GameInstance", "Gm", "GmKind", "GmValue", "Instance", "Instances", "Item", "ItemQuality", "Items",
        "Keyboard", "MainMenu", "ModBuff", "ModContext", "ModFiles", "ModInfo", "ModItem", "Mouse", "Script", "ScriptCall",
        "Visual", "Weapon", "WeaponColumn", "Game", "Events", "Scripts", "Sprite", "Sound", "Room", "GameObject", "Objects",
        "Consumable", "ConsumableColumn", "Consumables",
    };

    public static string Make(ItemTable weapons, ItemTable armor, ConsumableTable consumables)
    {
        var sb = new StringBuilder(Header("the game's weapons, armour and consumables, to inherit"));
        sb.AppendLine("namespace StoneForge.GameItems;\n");
        var taken = new HashSet<string>(LoaderNames, StringComparer.Ordinal);
        foreach (var table in new[] { weapons, armor })
            foreach (var item in table.Items)
                AppendItem(sb, table, item, taken);
        foreach (var item in consumables.Items)
            AppendConsumable(sb, consumables, item, taken);
        return sb.ToString();
    }

    // A consumable, by its id ("wine" -> Wine). (StoneForge.Patcher's ModSources finds mods' consumables by these
    // class names too: the id in PascalCase, less a "Consumable" added for a clash.)
    private static void AppendConsumable(StringBuilder sb, ConsumableTable table, string[] f, HashSet<string> taken)
    {
        string id = f[0].Trim();
        string cls = ClassName(id, "Consumable", taken);
        sb.AppendLine($"/// <summary>The game's {Xml(ConsumableSummary(table, f, id))}. Inherit it for a consumable of your own based on it - its");
        sb.AppendLine($"/// stats, sprites, sounds and what it does - and Set what differs: <c>class MyTonic : {cls} {{ public MyTonic() : base(\"my_tonic\") {{ }} }}</c>.</summary>");
        sb.AppendLine($"public abstract class {cls} : global::StoneForge.Consumable");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The game's id for it (what it's based on).</summary>");
        sb.AppendLine($"    public const string GameName = {Literal(id)};");
        sb.AppendLine($"    protected {cls}(string key) : base(key, GameName) {{ }}");
        sb.AppendLine("}\n");
    }

    // "wine: alcohol, tier 2, price 50; Thirst Change -10, ...": what it is and what it does.
    private static string ConsumableSummary(ConsumableTable table, string[] f, string id)
    {
        int price = table.Column("Price"), tier = table.Column("tier"), category = table.Column("Cat"), weight = table.Column("Weight");
        var stats = table.Header.Select((name, index) => (name, index))
            .Where(c => c.name.Length > 0 && c.index > weight && c.name != "tags" && ItemTable.Field(f, c.index) is string v && v.Length > 0 && v != "0")
            .Select(c => $"{c.name.Replace('_', ' ')} {ItemTable.Field(f, c.index)}").Take(6).ToList();
        string cat = ItemTable.Field(f, category), t = ItemTable.Field(f, tier), p = ItemTable.Field(f, price);
        return $"{id}: {(cat.Length > 0 ? cat : "consumable")}{(t.Length > 0 ? ", tier " + t : "")}{(p.Length > 0 ? ", price " + p : "")}"
            + (stats.Count > 0 ? "; " + string.Join(", ", stats) : "");
    }

    private static void AppendItem(StringBuilder sb, ItemTable table, string[] f, HashSet<string> taken)
    {
        string itemName = f[0].Trim();
        string cls = ClassName(itemName, table.BaseClass, taken);
        sb.AppendLine($"/// <summary>The game's {Xml(Summary(table, f, itemName))}. Inherit it for {(table.Kind == "armour" ? "armour" : "a weapon")} of your own based on it - its stats, sprites and");
        sb.AppendLine($"/// sounds - and Set what differs: <c>class MyItem : {cls} {{ public MyItem() : base(\"My Item\") {{ }} }}</c>.</summary>");
        sb.AppendLine($"public abstract class {cls} : global::StoneForge.{table.BaseClass}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The game's name for it (what it's based on).</summary>");
        sb.AppendLine($"    public const string GameName = {Literal(itemName)};");
        sb.AppendLine($"    protected {cls}(string name) : base(name, GameName) {{ }}");
        sb.AppendLine("}\n");
    }

    // Its name in PascalCase ("Drifter Sword" -> DrifterSword); one the loader or an earlier item has gets the
    // base class's name after it, then a number.
    private static string ClassName(string itemName, string baseClass, HashSet<string> taken)
    {
        string plain = string.Concat(Regex.Split(itemName, "[^A-Za-z0-9]+").Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        if (plain.Length == 0 || char.IsDigit(plain[0]))
            plain = "_" + plain;
        string cls = taken.Contains(plain) ? plain + baseClass : plain;
        for (int n = 2; taken.Contains(cls); n++)
            cls = plain + baseClass + n;
        taken.Add(cls);
        return cls;
    }

    // "Drifter Sword: sword, tier 2, Common; Rng 1, Slashing Damage 18": what it is and its main numbers - a
    // weapon's damage, an armour's protection (the columns after its price).
    private static string Summary(ItemTable table, string[] f, string itemName)
    {
        int slotColumn = table.Column("Slot"), tierColumn = table.Column("Tier"), rarityColumn = table.Column("rarity"), priceColumn = table.Column("Price");
        var shown = table.Header.Select((name, index) => (name, index))
            .Where(c => c.name.Length > 0 && (table.BaseClass == "Weapon" ? c.name.EndsWith("_Damage") || c.name == "Rng" : c.index > priceColumn + 2 && c.index < priceColumn + 30));
        var stats = shown.Where(c => ItemTable.Field(f, c.index) is string v && v.Length > 0 && v != "0")
            .Select(c => $"{c.name.Replace('_', ' ')} {ItemTable.Field(f, c.index)}").Take(6).ToList();
        string slot = ItemTable.Field(f, slotColumn), tier = ItemTable.Field(f, tierColumn), rarity = ItemTable.Field(f, rarityColumn);
        return $"{itemName}: {(slot.Length > 0 ? slot : table.Kind)}{(tier.Length > 0 ? ", tier " + tier : "")}{(rarity.Length > 0 ? ", " + rarity : "")}"
            + (stats.Count > 0 ? "; " + string.Join(", ", stats) : "");
    }
}
