namespace StoneForge;

// The game's off-screen culling: o_cullingController deactivates instances it takes off screen (loot, decorations...)
// and keeps them in its deactivatedInstancesList - with a cached count, deactivatedInstancesListSize, that it walks
// to bring them back. A deactivated instance is gone to GameMaker's with, instance_exists and variable_instance_get,
// but not gone from the world. (Units aren't culled.)
//
// What's deactivated comes from the bridge: one walk of the room's inactive instances gives every one's id and object
// - hundreds or thousands after a room change, decorations and all - kept for the frame. The controller's list is only
// searched when one is destroyed (Release).
internal static class Culling
{
    // The room's deactivated instances (id -> object index), for the frame.
    private static Dictionary<int, int>? _inactive;
    private static long _frame = -1;

    private static Dictionary<int, int> Inactive()
    {
        if (_inactive == null || _frame != Hooks.Frame)
            (_inactive, _frame) = (Game.Running ? Game.InactiveInstances() : new Dictionary<int, int>(), Hooks.Frame);
        return _inactive;
    }

    // Forgets what was read (after a change of ours; tests: each its own room, all in one frame).
    internal static void Reset() => (_inactive, _frame) = (null, -1);

    /// <summary>Every culled instance's id.</summary>
    internal static IReadOnlyCollection<int> Ids() => Inactive().Keys;

    /// <summary>Whether this instance is culled - off screen, deactivated.</summary>
    internal static bool Contains(int id) => id >= 0 && Inactive().ContainsKey(id);

    /// <summary>A culled instance's object index (-1: not one of the room's deactivated instances).</summary>
    internal static int ObjectOf(int id) => Inactive().TryGetValue(id, out int obj) ? obj : -1;

    // A list entry's instance id: a reference (GameMaker 2022's ids) or a number.
    private static int IdOf(GmValue value) => value.Kind == GmKind.Instance ? value.AsInstance.Id : value.Kind == GmKind.Real ? value.AsInt : -1;

    /// <summary>Brings a culled instance back: out of its culling controller's list - the cached count with it, or the
    /// controller would walk past the end of its list - and activated. False: it wasn't deactivated.</summary>
    internal static bool Release(int id)
    {
        if (!Contains(id))
            return false;
        int controllerObject = (int)GameObjectId.o_cullingController;
        int controllers = Game.CallBuiltin("instance_number", controllerObject).AsInt;
        for (int c = 0; c < controllers; c++)
        {
            Instance controller = Game.CallBuiltin("instance_find", controllerObject, c);
            GmValue list = controller.IsNone ? GmValue.Undefined : controller.Get("deactivatedInstancesList");
            // (2: ds_type_list.)
            if (list.Kind != GmKind.Real || !Game.CallBuiltin("ds_exists", list, 2).AsBool)
                continue;
            int size = Game.CallBuiltin("ds_list_size", list).AsInt;
            for (int i = 0; i < size; i++)
            {
                if (IdOf(Game.CallBuiltin("ds_list_find_value", list, i)) != id)
                    continue;
                Game.CallBuiltin("ds_list_delete", list, i);
                controller.Set("deactivatedInstancesListSize", size - 1);
                c = controllers;
                break;
            }
        }
        // (Deactivated by something other than the culling: activated all the same.)
        Game.CallBuiltin("instance_activate_object", id);
        Reset();
        return true;
    }
}
