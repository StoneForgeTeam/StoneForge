namespace StoneForge.Loader;

/// <summary>The order mods load in: each after the mods it requires and the ones it loads after (mod.json "requires",
/// "after") that are there; otherwise by folder name. Mods whose requires go round in a loop can't load (Problems says
/// so); an "after" that makes a loop is ignored (Warnings).</summary>
internal static class LoadOrder
{
    internal sealed record Result(List<int> Order, Dictionary<int, string> Problems, List<string> Warnings);

    /// <summary>The mods (by folder, in folder order - null: no valid mod.json) in the order to load them, as indices.</summary>
    internal static Result Sort(IReadOnlyList<ModManifest?> mods)
    {
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < mods.Count; i++)
            if (mods[i] is { } manifest)
                byId.TryAdd(manifest.Id, i);
        var problems = new Dictionary<int, string>();
        var warnings = new List<string>();
        List<int>[] Edges(bool withAfter) => mods.Select(m => m == null ? new List<int>()
            : m.Requires.Concat(withAfter ? m.After : Enumerable.Empty<string>())
                .Select(id => byId.TryGetValue(id, out int at) ? at : -1).Where(at => at >= 0 && mods[at] != m).Distinct().ToList()).ToArray();

        var (order, left) = Kahn(Edges(withAfter: true));
        if (left.Count > 0)
        {
            var (requiresOrder, requiresLeft) = Kahn(Edges(withAfter: false));
            var looped = left.Except(requiresLeft).Select(i => mods[i]!.Id).ToList();
            if (looped.Count > 0)
                warnings.Add($"Load order: \"after\" goes round in a loop ({string.Join(", ", looped)}) - their \"after\"s are ignored");
            order = requiresOrder;
            foreach (int i in requiresLeft)
                problems[i] = $"its \"requires\" go round in a loop ({string.Join(", ", requiresLeft.Select(j => mods[j]!.Id))}) - none of them can load";
            order.AddRange(requiresLeft);
        }
        return new Result(order, problems, warnings);
    }

    // Kahn's sort, taking the lowest index ready each time (so folder order where nothing says otherwise); the indices
    // left over are in a loop, or after one.
    private static (List<int> Order, List<int> Left) Kahn(List<int>[] after)
    {
        var order = new List<int>();
        var placed = new bool[after.Length];
        bool progress = true;
        while (progress)
        {
            progress = false;
            for (int i = 0; i < after.Length; i++)
                if (!placed[i] && after[i].All(d => placed[d]))
                {
                    placed[i] = true;
                    order.Add(i);
                    progress = true;
                    break;
                }
        }
        return (order, Enumerable.Range(0, after.Length).Where(i => !placed[i]).ToList());
    }

    /// <summary>A mod's required mods, and theirs, and so on (each once, those it needs first first) - everything its
    /// code can refer to.</summary>
    internal static List<string> AllRequired(ModManifest manifest, Func<string, ModManifest?> find)
    {
        var all = new List<string>();
        void Visit(ModManifest m, HashSet<string> seen)
        {
            foreach (string id in m.Requires)
                if (seen.Add(id))
                {
                    if (find(id) is { } required)
                        Visit(required, seen);
                    all.Add(id);
                }
        }
        Visit(manifest, new HashSet<string> { manifest.Id });
        return all;
    }
}
