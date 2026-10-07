namespace StoneForge;

/// <summary>The game's loot tables (its global.drop_table, from its table_drops data): what containers in the world roll
/// as they're first opened. A container names a table by its loot key, and the place's tier picks the row - "cryptTomb"
/// in a tier 3 place rolls "cryptTomb3" (or "cryptTomb", if there's no such row). The game loads them as it starts:
/// <see cref="Edit"/> from a mod's Load waits till they're there. Changed, they're so for the rest of the game's run.
/// A container's own table: <see cref="Containers.SetLootTable"/>. Game thread only.</summary>
/// <example><code>
/// // Wine in every tier 3 crypt tomb, a third of the time; and no bones in any of them.
/// LootTables.Edit(context, "cryptTomb3", table => table.Add("wine", 33, 1, 2));
/// LootTables.EditAll(context, name => name.StartsWith("cryptTomb"), table =>
/// {
///     foreach (var slot in table.Slots.Where(s => s.Items.Any(i => i.Contains("bone"))))
///         slot.Clear();
/// });
/// </code></example>
public static class LootTables
{
    private static readonly List<(string Mod, Func<string, bool> Which, Action<LootTable> Edit)> Pending = new();

    /// <summary>Whether the game has loaded its loot tables.</summary>
    public static bool Loaded => Game.Global["drop_table"].AsDsMap is { } tables && tables.Exists;

    /// <summary>Every table's name; none before they're loaded.</summary>
    public static IReadOnlyList<string> Names
        => Game.Global["drop_table"].AsDsMap is { Exists: true } tables
            ? tables.Keys.Where(key => key.Kind == GmKind.String && key.AsString.Length > 0 && tables.IsMap(key)).Select(key => key.AsString).ToArray()
            : Array.Empty<string>();

    /// <summary>A table by its name ("cryptTomb3"); null if there's none, or they aren't loaded yet.</summary>
    public static LootTable? Get(string name)
        => Game.Global["drop_table"].AsDsMap is { Exists: true } tables && tables.GetMap(name) is { } row ? new LootTable(name, row) : null;

    /// <summary>Changes a table: now, if the game has its tables, or as soon as it loads them (from a mod's Load: it
    /// starts before them). Nothing if there's no table by that name.</summary>
    public static void Edit(ModContext context, string name, Action<LootTable> edit) => EditAll(context, n => n == name, edit);

    /// <summary>Changes every table <paramref name="which"/> picks by name (now, or as soon as they're loaded).</summary>
    public static void EditAll(ModContext context, Func<string, bool> which, Action<LootTable> edit)
    {
        if (Loaded)
            Apply(context.Id, which, edit);
        else
            Pending.Add((context.Id, which, edit));
    }

    // The loader's: queued edits made as the game loads its tables (o_textLoader's step 38); and the slots beyond the
    // game's nine rolled after its own roll (scr_loot_from_tables).
    internal static void Install(ModContext loader)
    {
        loader.OnCode("gml_Object_o_textLoader_Other_25", after: (textLoader, _) =>
        {
            if (Pending.Count == 0 || textLoader.IsNone || textLoader.Get("number").AsInt != 38 || !Loaded)
                return;
            var edits = Pending.ToArray();
            Pending.Clear();
            foreach (var (mod, which, edit) in edits)
                Apply(mod, which, edit);
        });
        loader.OnScript(LootScript, after: RollExtras);
    }

    private const string LootScript = "scr_loot_from_tables";
    // (A table of the extra slots, made for a roll: no table of the game's is called this.)
    private const string ExtrasKey = "__stoneforge_extra_slots";

    // scr_loot_from_tables(key, tier, drop) done - the game's roll of a table: its slots beyond the nine, rolled by the
    // same script, as the same container, nine at a time - each time from a copy of the table (its tier range) with
    // those in its nine slots and no equipment.
    private static void RollExtras(ScriptCall call)
    {
        if (call.Args.Length < 1 || call.Args[0].Kind != GmKind.String || call.Args[0].AsString is not { Length: > 0 } key
            || Game.Global["drop_table"].AsDsMap is not { Exists: true } tables)
            return;
        double tier = call.Args.Length > 1 && call.Args[1].Kind == GmKind.Real ? call.Args[1].AsReal : 0;
        GmValue drop = call.Args.Length > 2 ? call.Args[2] : false;
        if (tier == 0)
            tier = PlaceTier();
        // (The row the game rolled: the tier's, else the key's own.)
        string tierText = tier == Math.Floor(tier) ? ((long)tier).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : tier.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if ((tables.GetMap(key + tierText) ?? tables.GetMap(key)) is not { } row)
            return;
        int count = LootTable.SlotCount(row);
        var extras = Enumerable.Range(LootTable.GameSlots + 1, Math.Max(0, count - LootTable.GameSlots))
            .Where(n => !new LootSlot(row, n).IsEmpty).ToList();
        for (int start = 0; start < extras.Count; start += LootTable.GameSlots)
            RollSlots(tables, row, extras.Skip(start).Take(LootTable.GameSlots).ToList(), call, tier, drop);
    }

    private static void RollSlots(DsMap tables, DsMap row, List<int> slots, ScriptCall call, double tier, GmValue drop)
    {
        var copy = DsMap.Create();
        try
        {
            Game.CallBuiltin("ds_map_copy", copy.Id, row.Id);
            for (int n = 1; n <= LootTable.GameSlots; n++)
                foreach (string suffix in Suffixes)
                    copy[$"slot{n}{suffix}"] = n <= slots.Count && row[$"slot{slots[n - 1]}{suffix}"] is { IsUndefined: false } value ? value : "";
            for (int n = 1; n <= 5; n++)
                copy[$"eq{n}_chance"] = "0";
            tables[ExtrasKey] = copy.Id;
            Hooks.CallOriginal(LootScript, call.Self, call.Other, new GmValue[] { ExtrasKey, tier, drop });
        }
        finally
        {
            tables.Remove(ExtrasKey);
            copy.Destroy();
        }
    }

    private static readonly string[] Suffixes = { "", "_chance", "_count", "_tags" };

    // The place's tier, as the game's roll works it out for a table called with none: the world-map cell's, or its
    // dungeon's.
    private static double PlaceTier()
    {
        GmValue x = Game.Global["playerGridX"], y = Game.Global["playerGridY"];
        return Game.Global["floor_counter"].AsReal == 0
            ? Game.CallScript("scr_globaltile_generation_get", default, "spawnTier", x, y, 1).AsReal
            : Game.CallScript("scr_globaltile_dungeon_get", default, "dungeon_tier", x, y, 1).AsReal;
    }

    internal static void RemoveMod(string mod) => Pending.RemoveAll(p => p.Mod == mod);

    private static void Apply(string mod, Func<string, bool> which, Action<LootTable> edit)
    {
        foreach (string name in Names)
            if (which(name) && Get(name) is { } table)
                Hooks.Invoke(mod, $"loot table {name}", () =>
                {
                    edit(table);
                    return false;
                });
    }
}
