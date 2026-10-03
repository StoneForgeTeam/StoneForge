namespace StoneForge;

/// <summary>Base of the typed instance classes (the generated <c>Objects.o_player</c> and the rest, which
/// follow the game's object inheritance). Gives every instance GameMaker's built-in variables as properties;
/// the generated classes add each object's own variables.</summary>
public class GameInstance
{
    /// <summary>The untyped instance underneath (variables by name, for anything not generated).</summary>
    public Instance Instance { get; private set; }

    internal static T Wrap<T>(Instance instance) where T : GameInstance, new() => new T { Instance = instance };

    protected GmValue Get(string name) => Instance.Get(name);
    protected void Set(string name, GmValue value) => Instance.Set(name, value);

    /// <summary>Whether it's still there.</summary>
    public bool Exists => Instance.Exists;

    /// <summary>Its alarms (<c>alarm[0]</c> to <c>alarm[11]</c>, see <see cref="StoneForge.Alarms"/>).</summary>
    public Alarms Alarm => Instance.Alarm;

    public double X { get => Get("x"); set => Set("x", value); }
    public double Y { get => Get("y"); set => Set("y", value); }
    public double XPrevious => Get("xprevious");
    public double YPrevious => Get("yprevious");
    public double Depth { get => Get("depth"); set => Set("depth", value); }
    public bool Visible { get => Get("visible"); set => Set("visible", value); }
    public double ImageIndex { get => Get("image_index"); set => Set("image_index", value); }
    public double ImageSpeed { get => Get("image_speed"); set => Set("image_speed", value); }
    public double ImageXScale { get => Get("image_xscale"); set => Set("image_xscale", value); }
    public double ImageYScale { get => Get("image_yscale"); set => Set("image_yscale", value); }
    public double ImageAngle { get => Get("image_angle"); set => Set("image_angle", value); }
    public double ImageAlpha { get => Get("image_alpha"); set => Set("image_alpha", value); }
    public double SpriteIndex { get => Get("sprite_index"); set => Set("sprite_index", value); }
    /// <summary>The object it's an instance of (compare with the generated GameObjectId enum).</summary>
    public int ObjectIndex => Get("object_index");
    /// <summary>Its GameMaker instance id.</summary>
    public int Id => Get("id");

    /// <summary>Whether it's an instance of <paramref name="obj"/> or one of its children.</summary>
    public bool IsA(int obj) => Gm.ObjectIsAncestor(ObjectIndex, obj) || ObjectIndex == obj;

    /// <summary>Removes it from the room (its Destroy event runs).</summary>
    public void Destroy() => Game.CallBuiltinAs("instance_destroy", Instance, Instance);

    public override string ToString() => $"{GetType().Name} {Instance}";
}
