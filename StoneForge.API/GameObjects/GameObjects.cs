namespace StoneForge;

/// <summary>Mods' own game objects (<see cref="GameObject"/>), through <see cref="ModContext.Objects"/>: their C#
/// events hooked to the events the patcher gave their objects, and their sprite and flags set once the game runs.
/// On the game's native (YYC) build an object can't be given events of its own (no GML can be added): it's added with
/// none, under its parent - or, with none, under <see cref="NativeHost"/>'s host, o_GMLiveDebug - and runs theirs. Each
/// C# event is hooked on the nearest of its ancestors that has that event, for its own instances: as on the VM build,
/// the ancestor's code runs as its event_inherited() would (unless a before handler replaces it) - the host's never
/// does. An event none of them has never runs there: the log says which, once.</summary>
public sealed class GameObjects
{
    private readonly ModContext _context;
    internal GameObjects(ModContext context) => _context = context;

    // Every mod's, by object name.
    private static readonly Dictionary<string, (ModContext Context, GameObject Object)> All = new();

    // The events every mod object has (the patcher's GameObjects gives them all): code name suffix -> what runs, and the
    // GameObject method a mod overrides for it. (before: runs first, and true skips the game's code - the parent's,
    // through event_inherited.)
    private static IEnumerable<(string Event, Func<GameObject, Instance, bool>? Before, Action<GameObject, Instance>? After, string Method)> Events()
    {
        yield return ("Create_0", null, (o, self) => o.OnCreate(self), nameof(GameObject.OnCreate));
        yield return ("Destroy_0", (o, self) => { o.OnDestroy(self); return false; }, null, nameof(GameObject.OnDestroy));
        yield return ("CleanUp_0", (o, self) => { o.OnCleanUp(self); return false; }, null, nameof(GameObject.OnCleanUp));
        yield return ("Step_1", null, (o, self) => o.OnBeginStep(self), nameof(GameObject.OnBeginStep));
        yield return ("Step_0", null, (o, self) => o.OnStep(self), nameof(GameObject.OnStep));
        yield return ("Step_2", null, (o, self) => o.OnEndStep(self), nameof(GameObject.OnEndStep));
        yield return ("Draw_72", null, (o, self) => o.OnDrawBegin(self), nameof(GameObject.OnDrawBegin));
        yield return ("Draw_0", (o, self) =>
        {
            if (!o.ReplacesDraw)
                return false;
            o.OnDraw(self);
            return true;
        }, (o, self) =>
        {
            if (!o.ReplacesDraw)
                o.OnDraw(self);
        }, nameof(GameObject.OnDraw));
        yield return ("Draw_73", null, (o, self) => o.OnDrawEnd(self), nameof(GameObject.OnDrawEnd));
        yield return ("Draw_64", null, (o, self) => o.OnDrawGui(self), nameof(GameObject.OnDrawGui));
        for (int alarm = 0; alarm < 12; alarm++)
        {
            int n = alarm;
            yield return ($"Alarm_{n}", null, (o, self) => o.OnAlarm(self, n), nameof(GameObject.OnAlarm));
        }
        for (int user = 0; user < 16; user++)
        {
            int n = user;
            yield return ($"Other_{10 + n}", null, (o, self) => o.OnUserEvent(self, n), nameof(GameObject.OnUserEvent));
        }
        yield return ("Mouse_4", null, (o, self) => o.OnLeftPressed(self), nameof(GameObject.OnLeftPressed));
        yield return ("Mouse_5", null, (o, self) => o.OnRightPressed(self), nameof(GameObject.OnRightPressed));
        yield return ("Mouse_10", null, (o, self) => o.OnMouseEnter(self), nameof(GameObject.OnMouseEnter));
        yield return ("Mouse_11", null, (o, self) => o.OnMouseLeave(self), nameof(GameObject.OnMouseLeave));
    }

    /// <summary>The native build's host for a mod object with no parent: o_GMLiveDebug - a development tool's object the
    /// game never makes or refers to, with Create, the three Steps and the three world Draws (empty).</summary>
    internal const string NativeHostObject = "o_GMLiveDebug";

    /// <summary>Adds a mod's object: its events run from now on, and it can make instances once the game has it.
    /// Call it from <see cref="IStoneMod.Load"/>.</summary>
    public void Add(GameObject obj)
    {
        obj.Attach(_context);
        if (All.TryGetValue(obj.ObjectName, out var existing))
            throw new ArgumentException($"There's already an object \"{obj.Id}\" (from {existing.Context.Name})");
        All[obj.ObjectName] = (_context, obj);
        // (The native build: hooked once it's defined - on its ancestors' events, which need its index to find.)
        if (!Game.IsNative)
            foreach (var (name, before, after, _) in Events())
                _context.OnCode($"gml_Object_{obj.ObjectName}_{name}",
                    before == null ? null : (self, _) => before(obj, self),
                    after == null ? null : (self, _) => after(obj, self));
        if (Game.Running)
            Define(_context, obj);
    }

    // The loader's frame: objects added before the game ran, defined once it does.
    internal static void Install(ModContext loader)
        => loader.Frame += () =>
        {
            if (!Game.Running)
                return;
            foreach (var (context, obj) in All.Values)
                if (obj.Index < 0 && !_tried.Contains(obj))
                    Define(context, obj);
        };

    private static readonly HashSet<GameObject> _tried = new();

    // Its object in the game: its index, sprite and flags.
    private static void Define(ModContext context, GameObject obj)
    {
        _tried.Add(obj);
        int index = Gm.AssetGetIndex(obj.ObjectName);
        if (index < 0)
        {
            context.Log($"object \"{obj.Id}\": the game has no {obj.ObjectName} yet - it's added when the game starts (restart it)");
            return;
        }
        obj.Index = index;
        if (Game.IsNative)
            HookNative(context, obj);
        if (obj.Sprite != null)
        {
            int sprite = Gm.AssetGetIndex(obj.Sprite);
            if (sprite >= 0)
                Game.CallBuiltinTrusted("object_set_sprite", default, default, index, sprite);
            else
                context.Log($"object \"{obj.Id}\": the game has no sprite \"{obj.Sprite}\"");
        }
        Game.CallBuiltinTrusted("object_set_persistent", default, default, index, obj.Persistent);
        Game.CallBuiltinTrusted("object_set_visible", default, default, index, obj.Visible);
    }

    // The native build: each C# event hooked on the nearest ancestor that has it, for this object's own instances; one
    // the mod overrides that none has, said once.
    private static void HookNative(ModContext context, GameObject obj)
    {
        var chain = new List<string>();
        for (int parent = Game.CallBuiltinTrusted("object_get_parent", default, default, obj.Index).AsInt; parent >= 0 && chain.Count < 64;
             parent = Game.CallBuiltinTrusted("object_get_parent", default, default, parent).AsInt)
            chain.Add(Gm.ObjectGetName(parent));
        bool hosted = chain.Count == 1 && chain[0] == NativeHostObject;
        var missing = new List<string>();
        foreach (var (name, before, after, method) in Events())
        {
            string? owner = chain.FirstOrDefault(ancestor => Game.HasFunction($"gml_Object_{ancestor}_{name}"));
            if (owner == null)
            {
                if (Overrides(obj, method) && !missing.Contains(method))
                    missing.Add(method);
                continue;
            }
            int index = obj.Index;
            bool Ours(Instance self) => !self.IsNone && self.Get("object_index").AsInt == index;
            if (hosted)
                // (The host's code never runs for ours: our handlers do, before and after, and a Draw that isn't replaced
                // draws the sprite first, as the VM build's does for an object with no Draw of its parent's.)
                context.OnCode($"gml_Object_{owner}_{name}", before: (self, _) =>
                {
                    if (!Ours(self))
                        return false;
                    if (name == "Draw_0" && !obj.ReplacesDraw)
                        Game.CallBuiltinAs("draw_self", self, self);
                    if (before == null || !before(obj, self))
                        after?.Invoke(obj, self);
                    return true;
                });
            else
                context.OnCode($"gml_Object_{owner}_{name}",
                    before == null ? null : (self, _) => Ours(self) && before(obj, self),
                    after == null ? null : (self, _) =>
                    {
                        if (Ours(self))
                            after(obj, self);
                    });
        }
        if (missing.Count > 0)
            context.Log($"object \"{obj.Id}\": on the game's native build {string.Join(", ", missing)} never run{(missing.Count == 1 ? "s" : "")} - "
                + $"no parent of its ({(chain.Count == 0 ? "none" : string.Join(" < ", chain))}) has that event");
    }

    // Whether a mod's object overrides one of GameObject's event methods.
    private static bool Overrides(GameObject obj, string method)
        => obj.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Any(m => m.Name == method && m.DeclaringType != typeof(GameObject) && m.GetBaseDefinition().DeclaringType == typeof(GameObject));

    // A mod switched off: its objects' instances destroyed (their Destroy events run), the objects forgotten.
    internal static void RemoveMod(string mod)
    {
        foreach (var (name, (context, obj)) in All.Where(e => e.Value.Context.Id == mod).ToList())
        {
            if (Game.Running && obj.Index >= 0)
                foreach (var instance in obj.Instances)
                    Game.CallBuiltinTrusted("instance_destroy", default, default, instance.Id);
            All.Remove(name);
            _tried.Remove(obj);
        }
    }
}
