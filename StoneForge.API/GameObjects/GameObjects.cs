namespace StoneForge;

/// <summary>Mods' own game objects (<see cref="GameObject"/>), through <see cref="ModContext.Objects"/>: their C#
/// events hooked to the events the patcher gave their objects, and their sprite and flags set once the game runs.</summary>
public sealed class GameObjects
{
    private readonly ModContext _context;
    internal GameObjects(ModContext context) => _context = context;

    // Every mod's, by object name.
    private static readonly Dictionary<string, (ModContext Context, GameObject Object)> All = new();

    // The events every mod object has (the patcher's GameObjects gives them all): code name suffix -> what runs.
    // (before: runs first, and true skips the game's code - the parent's, through event_inherited.)
    private static IEnumerable<(string Event, Func<GameObject, Instance, bool>? Before, Action<GameObject, Instance>? After)> Events()
    {
        yield return ("Create_0", null, (o, self) => o.OnCreate(self));
        yield return ("Destroy_0", (o, self) => { o.OnDestroy(self); return false; }, null);
        yield return ("CleanUp_0", (o, self) => { o.OnCleanUp(self); return false; }, null);
        yield return ("Step_1", null, (o, self) => o.OnBeginStep(self));
        yield return ("Step_0", null, (o, self) => o.OnStep(self));
        yield return ("Step_2", null, (o, self) => o.OnEndStep(self));
        yield return ("Draw_72", null, (o, self) => o.OnDrawBegin(self));
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
        });
        yield return ("Draw_73", null, (o, self) => o.OnDrawEnd(self));
        yield return ("Draw_64", null, (o, self) => o.OnDrawGui(self));
        for (int alarm = 0; alarm < 12; alarm++)
        {
            int n = alarm;
            yield return ($"Alarm_{n}", null, (o, self) => o.OnAlarm(self, n));
        }
        for (int user = 0; user < 16; user++)
        {
            int n = user;
            yield return ($"Other_{10 + n}", null, (o, self) => o.OnUserEvent(self, n));
        }
        yield return ("Mouse_4", null, (o, self) => o.OnLeftPressed(self));
        yield return ("Mouse_5", null, (o, self) => o.OnRightPressed(self));
        yield return ("Mouse_10", null, (o, self) => o.OnMouseEnter(self));
        yield return ("Mouse_11", null, (o, self) => o.OnMouseLeave(self));
    }

    /// <summary>Adds a mod's object: its events run from now on, and it can make instances once the game has it.
    /// Call it from <see cref="IStoneMod.Load"/>.</summary>
    public void Add(GameObject obj)
    {
        obj.Attach(_context);
        if (All.TryGetValue(obj.ObjectName, out var existing))
            throw new ArgumentException($"There's already an object \"{obj.Id}\" (from {existing.Context.Name})");
        All[obj.ObjectName] = (_context, obj);
        foreach (var (name, before, after) in Events())
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
