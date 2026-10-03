namespace StoneForge;

/// <summary>A mod's own game object: o_&lt;modid&gt;__&lt;key&gt; in the game, a child of one of the game's
/// (<see cref="Parent"/>) - so the game treats its instances as that object's (a child of o_enemy is a unit) - with
/// its events in C#:
/// <code>
/// public class Marker : GameObject
/// {
///     public Marker() : base("marker", "o_invisible_mark") { Sprite = "s_dummy"; }
///     protected override void OnCreate(Instance self) => self["life"] = 60;
///     protected override void OnStep(Instance self) { ... }
/// }
/// </code>
/// Add it with <see cref="ModContext.Objects"/> (context.Objects.Add), then make instances with
/// <see cref="Create"/>. Its key and parent must be string literals in the base(...) call: StoneForge's patcher
/// reads them from the source and adds the object to the game data at the game's next start (a new object, or a
/// changed parent, needs a restart; its C# events reload with the mod).
///
/// Every event runs the parent's first (as <c>event_inherited()</c>), then the C# one - but Destroy and Clean Up run
/// the C# one first, and Draw replaces the parent's when <see cref="ReplacesDraw"/>. With no parent's Draw, Draw
/// draws the instance's sprite (as GameMaker does for an object without one) unless it's replaced.
/// Its instances go when the mod is switched off.</summary>
public abstract class GameObject
{
    protected GameObject(string key, string parent)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("An object's key is letters, digits and _ only", nameof(key));
        ModIdentity.CheckKey(key, "object");
        Key = key;
        Parent = parent ?? "";
    }

    /// <summary>Its key in its mod ("ghost").</summary>
    public string Key { get; }
    /// <summary>Its full ID, "yourmod:ghost" (set when it's added).</summary>
    public string Id { get; private set; } = "";
    /// <summary>The game object it's a child of ("o_enemy"; "" for none).</summary>
    public string Parent { get; }
    /// <summary>Its name in the game: "o_yourmod__ghost".</summary>
    public string ObjectName { get; private set; } = "";
    /// <summary>Its object index in the game (-1 until the game has it: it's added at the game's next start).</summary>
    public int Index { get; internal set; } = -1;

    /// <summary>Its sprite: one of the game's, by name (default: its parent's).</summary>
    public string? Sprite { get; protected set; }
    /// <summary>Whether its instances stay from room to room (default: no).</summary>
    public bool Persistent { get; protected set; }
    /// <summary>Whether its instances are drawn (default: yes).</summary>
    public bool Visible { get; protected set; } = true;
    /// <summary>Whether <see cref="OnDraw"/> draws it in place of its parent's Draw (default: no - after it).</summary>
    protected internal virtual bool ReplacesDraw => false;

    /// <summary>The mod that added it; set by <see cref="GameObjects.Add"/>.</summary>
    protected ModContext Context { get; private set; } = null!;

    /// <summary>A new instance at (x, y), at <paramref name="depth"/> (its Create has run when it's returned).</summary>
    public Instance Create(double x, double y, int depth = 0)
    {
        if (Index < 0)
            throw new InvalidOperationException($"{ObjectName} isn't in the game yet - it's added at the game's next start (restart it)");
        return Game.CallBuiltinTrusted("instance_create_depth", default, default, x, y, depth, Index).AsInstance;
    }

    /// <summary>Its instances now.</summary>
    public IReadOnlyList<Instance> Instances
    {
        get
        {
            var list = new List<Instance>();
            if (Index < 0 || !Game.Running)
                return list;
            int count = Game.CallBuiltinTrusted("instance_number", default, default, Index).AsInt;
            for (int i = 0; i < count; i++)
                if (Game.CallBuiltinTrusted("instance_find", default, default, Index, i).AsInstance is { IsNone: false } instance)
                    list.Add(instance.Persist());
            return list;
        }
    }

    /// <summary>Whether an instance is one of its (not of a child of it).</summary>
    public bool Owns(Instance instance) => Index >= 0 && instance.Exists && instance.Get("object_index").AsInt == Index;

    // ---- its events (self: the instance) ----

    protected internal virtual void OnCreate(Instance self) { }
    protected internal virtual void OnDestroy(Instance self) { }
    protected internal virtual void OnCleanUp(Instance self) { }
    protected internal virtual void OnBeginStep(Instance self) { }
    protected internal virtual void OnStep(Instance self) { }
    protected internal virtual void OnEndStep(Instance self) { }
    protected internal virtual void OnDrawBegin(Instance self) { }
    protected internal virtual void OnDraw(Instance self) { }
    protected internal virtual void OnDrawEnd(Instance self) { }
    protected internal virtual void OnDrawGui(Instance self) { }
    /// <summary>One of its alarms (0-11) went off.</summary>
    protected internal virtual void OnAlarm(Instance self, int alarm) { }
    /// <summary>One of its user events (0-15: event_user(n), the game's Other 10-25) ran.</summary>
    protected internal virtual void OnUserEvent(Instance self, int number) { }
    protected internal virtual void OnLeftPressed(Instance self) { }
    protected internal virtual void OnRightPressed(Instance self) { }
    protected internal virtual void OnMouseEnter(Instance self) { }
    protected internal virtual void OnMouseLeave(Instance self) { }

    internal void Attach(ModContext context)
    {
        Context = context;
        Id = context.ContentId(Key);
        ObjectName = "o_" + context.GameKey(Key);
    }
}
