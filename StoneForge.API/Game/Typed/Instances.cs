namespace StoneForge;

/// <summary>Finding instances in the room.</summary>
public static class Instances
{
    /// <summary>Every instance of <paramref name="obj"/> (and its children) in the room, as <typeparamref name="T"/>.
    /// With <paramref name="includeCulled"/>, the ones the game has culled too - off screen, deactivated
    /// (<see cref="Instance.IsCulled"/>): ground loot and the like, which GameMaker's own <c>with</c> skips.</summary>
    public static List<T> All<T>(GameObjectId obj, bool includeCulled = false) where T : GameInstance, new()
        => All((int)obj, includeCulled).ConvertAll(GameInstance.Wrap<T>);

    /// <inheritdoc cref="All{T}(GameObjectId, bool)"/>
    public static List<Instance> All(GameObjectId obj, bool includeCulled = false) => All((int)obj, includeCulled);

    /// <summary>Every instance of a mod's own object (and its children), culled ones too with
    /// <paramref name="includeCulled"/>. None before the game has the object.</summary>
    public static List<Instance> All(GameObject obj, bool includeCulled = false)
        => obj.Index < 0 ? new List<Instance>() : All(obj.Index, includeCulled);

    /// <summary>Every instance of the object with index <paramref name="objectIndex"/> (and its children), culled ones
    /// too with <paramref name="includeCulled"/>. Each is kept by its id (<see cref="Instance.Persist"/>).</summary>
    public static List<Instance> All(int objectIndex, bool includeCulled = false)
    {
        int count = Game.CallBuiltin("instance_number", objectIndex).AsInt;
        var list = new List<Instance>(count);
        for (int i = 0; i < count; i++)
        {
            Instance found = Game.CallBuiltin("instance_find", objectIndex, i);
            if (!found.IsNone)
                list.Add(found.Persist());
        }
        if (!includeCulled)
            return list;
        // (The culled can be thousands - every decoration off screen: their objects all come from one walk of the
        // room's deactivated instances, and each object is asked about only once.)
        var active = new HashSet<int>(list.Select(i => i.Id));
        var isOne = new Dictionary<int, bool>();
        foreach (int id in Culling.Ids())
        {
            // (One the controller has brought back since is among the active ones already.)
            if (active.Contains(id))
                continue;
            // (One not among the room's deactivated instances is left out.)
            int its = Culling.ObjectOf(id);
            if (its < 0)
                continue;
            if (!isOne.TryGetValue(its, out bool one))
                isOne[its] = one = its == objectIndex || Game.CallBuiltin("object_is_ancestor", its, objectIndex).AsBool;
            if (one)
                list.Add(Instance.FromId(id));
        }
        return list;
    }

    /// <summary>The first instance of <paramref name="obj"/>, or null if there's none.</summary>
    public static T? First<T>(GameObjectId obj) where T : GameInstance, new()
    {
        if (!Gm.InstanceExists(obj))
            return null;
        Instance found = Game.CallBuiltin("instance_find", GmValue.From(obj), 0);
        return found.IsNone ? null : GameInstance.Wrap<T>(found);
    }

    /// <summary>The instance of <paramref name="obj"/> nearest to (x, y), or null.</summary>
    public static T? Nearest<T>(double x, double y, GameObjectId obj) where T : GameInstance, new()
    {
        Instance found = Game.CallBuiltin("instance_nearest", x, y, GmValue.From(obj));
        return found.IsNone ? null : GameInstance.Wrap<T>(found);
    }
}
