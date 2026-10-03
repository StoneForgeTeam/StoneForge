namespace StoneForge;

/// <summary>Finding instances in the room.</summary>
public static class Instances
{
    /// <summary>Every instance of <paramref name="obj"/> (and its children) in the room, as <typeparamref name="T"/>.</summary>
    public static List<T> All<T>(GameObjectId obj) where T : GameInstance, new()
    {
        int count = Gm.InstanceNumber(obj);
        var list = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            Instance found = Game.CallBuiltin("instance_find", GmValue.From(obj), i);
            if (!found.IsNone)
                list.Add(GameInstance.Wrap<T>(found));
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
