namespace StoneForge;

/// <summary>The doors in a room that open and close (the game's o_door_parent and its kinds: a house's, a crypt's...),
/// read and opened or closed as the game does it, and <see cref="OnChanged"/> as anyone opens or closes one. (The ways
/// out of a place are <see cref="Exits"/>.)</summary>
public static class Doors
{
    private static int _doors = -2;

    // (Tests: the game's objects looked up again.)
    internal static void ResetForTests() => _doors = -2;

    private static int DoorObject => _doors == -2 ? _doors = Gm.AssetGetIndex("o_door_parent") : _doors;

    /// <summary>The room's doors - those off screen too with <paramref name="includeCulled"/> (whose own state can't be
    /// read till they're back: <see cref="Instance.IsCulled"/>).</summary>
    public static IReadOnlyList<Instance> All(bool includeCulled = false)
        => DoorObject < 0 ? Array.Empty<Instance>() : Instances.All(DoorObject, includeCulled);

    /// <summary>Whether it's a door (one of o_door_parent's kinds).</summary>
    public static bool IsDoor(Instance instance)
        => DoorObject >= 0 && instance.Exists && instance.Get("object_index").AsInt is var obj
            && (obj == DoorObject || Gm.ObjectIsAncestor(obj, DoorObject));

    /// <summary>Whether it's open, or opening (the game's scr_door_is_closed: by its animation).</summary>
    public static bool IsOpen(Instance door) => !Game.CallScript("scr_door_is_closed", default, door).AsBool;

    /// <summary>Whether it's locked.</summary>
    public static bool IsLocked(Instance door) => door.Get("is_lock").AsBool;

    /// <summary>Opens or closes it as the game does (its user event 3: the animation and its sound - and the noise, which
    /// units nearby hear; its collision follows when the animation's done). Opening a locked door unlocks it, unless
    /// <paramref name="unlock"/> is false (then it stays shut). Nothing if it's that way already.</summary>
    public static void SetOpen(Instance door, bool open, bool unlock = true)
    {
        if (door.IsNone || !door.Exists || IsOpen(door) == open)
            return;
        if (open && IsLocked(door))
        {
            if (!unlock)
                return;
            door["is_lock"] = false;
        }
        Game.CallBuiltinAs("event_user", door, door, 3);
    }

    /// <summary>Runs as a door starts opening or closing - whoever does it: the player, an NPC or an enemy going through,
    /// the game's own scripts, or a mod (<see cref="SetOpen"/>): the door, and whether it's opening.</summary>
    public static void OnChanged(ModContext context, Action<Instance, bool> handler)
    {
        // (A door opens or shuts by its user event 3: o_door_parent's - its kinds' own call it first - or, for a crypt's,
        // which only ever opens, o_cryptdoor_parent's own. Whether it changed: open or not, before and after.)
        var before = new Stack<bool>();
        foreach (string code in ToggleCodes)
            context.OnCode(code,
                before: (door, _) =>
                {
                    before.Push(IsOpen(door));
                    return false;
                },
                after: (door, _) =>
                {
                    if (before.Count == 0)
                        return;
                    bool was = before.Pop();
                    if (!door.IsNone && IsOpen(door) is var open && open != was)
                        handler(door.Persist(), open);
                });
    }

    private static readonly string[] ToggleCodes = { "gml_Object_o_door_parent_Other_13", "gml_Object_o_cryptdoor_parent_Other_13" };
}
