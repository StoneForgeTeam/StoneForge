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

    // The loader's: queued edits made as the game loads its tables (o_textLoader's step 38).
    internal static void Install(ModContext loader)
        => loader.OnCode("gml_Object_o_textLoader_Other_25", after: (textLoader, _) =>
        {
            if (Pending.Count == 0 || textLoader.IsNone || textLoader.Get("number").AsInt != 38 || !Loaded)
                return;
            var edits = Pending.ToArray();
            Pending.Clear();
            foreach (var (mod, which, edit) in edits)
                Apply(mod, which, edit);
        });

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
