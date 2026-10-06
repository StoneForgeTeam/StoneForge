namespace StoneForge;

/// <summary>An event of one of the loader's objects hooked (o_stonemod_buff's Create, o_stonemod_fx's Step...). On the VM
/// build the object has the event itself (event_inherited(), from the patcher): its own code entry is hooked. On the
/// native (YYC) build it's added with no code, and runs its parents' events: the nearest ancestor's code entry for that
/// event is hooked, for this object's instances only - as on the VM build, the ancestor's code runs as its
/// event_inherited() would. The object's ancestors are known once the game runs, so it's hooked then.</summary>
internal static class ObjectEvents
{
    private static readonly List<(ModContext Context, string Object, string Event, Func<Instance, bool>? Before, Action<Instance>? After)> Pending = new();

    internal static void Hook(ModContext context, string obj, string ev, Func<Instance, bool>? before = null, Action<Instance>? after = null)
    {
        if (!Game.IsNative)
        {
            context.OnCode($"gml_Object_{obj}_{ev}", before == null ? null : (self, _) => before(self), after == null ? null : (self, _) => after(self));
            return;
        }
        Pending.Add((context, obj, ev, before, after));
        if (Ready)
            Resolve();
    }

    /// <summary>The loader's: hooks waiting for the game, made once it runs.</summary>
    internal static void Install(ModContext loader)
        => loader.Frame += () =>
        {
            if (Pending.Count > 0 && Ready)
                Resolve();
        };

    // (Not before the game's 10th frame: the first few come while the runner is still starting, and asset_get_index -
    // which finding an object's ancestors needs - crashes it then.)
    private static bool Ready => Game.Running && Hooks.Frame >= 10;

    private static void Resolve()
    {
        var waiting = Pending.ToArray();
        Pending.Clear();
        foreach (var (context, obj, ev, before, after) in waiting)
        {
            int index = Gm.AssetGetIndex(obj);
            if (index < 0)
            {
                context.Log($"{obj}: not in the game data - its {ev} isn't hooked (run StoneForge.Patcher install)");
                continue;
            }
            string? owner = null;
            for (int at = index, depth = 0; at >= 0 && depth < 64; at = Game.CallBuiltinTrusted("object_get_parent", default, default, at).AsInt, depth++)
            {
                string name = Gm.ObjectGetName(at);
                if (Game.HasFunction($"gml_Object_{name}_{ev}"))
                {
                    owner = name;
                    break;
                }
            }
            if (owner == null)
            {
                context.Log($"{obj}: no parent of it has {ev} on the game's native build - it never runs");
                continue;
            }
            bool Ours(Instance self) => !self.IsNone && self.Get("object_index").AsInt == index;
            context.OnCode($"gml_Object_{owner}_{ev}",
                before == null ? null : (self, _) => Ours(self) && before(self),
                after == null ? null : (self, _) =>
                {
                    if (Ours(self))
                        after(self);
                });
        }
    }
}
