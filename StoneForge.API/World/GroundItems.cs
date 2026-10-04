using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>Items lying on the ground (the game's o_loot and its children: o_loot_wine, o_weapon_loot...): finding them,
/// an item's saved state in the game's own save format and an item made back from it, new items put down, and an item
/// still in the air - the hop a drop makes onto a neighbouring tile - read and replayed. What a stash, trade or loot mod
/// needs to move items between places, players or games. Game thread only.</summary>
public static class GroundItems
{
    /// <summary>Every item on the ground in the room - those off screen too, unless <paramref name="includeCulled"/> is
    /// false (the game culls ground items it takes off screen: see <see cref="Instance.IsCulled"/>).</summary>
    public static IReadOnlyList<GroundItem> All(bool includeCulled = true)
        => Instances.All(GameObjectId.o_loot, includeCulled).Select(instance => new GroundItem(instance)).ToArray();

    /// <summary>An item made from its saved state (<see cref="GroundItem.ToJson"/>, perhaps another game's) as loading a
    /// location makes it (scr_locationRoomEntityLootInstanceCreate, then scr_locationRoomEntityLootSaveDataSet): where it
    /// lay, with its stack, charge and item data - no hop onto a neighbouring tile. Null if the JSON isn't an item's, or
    /// the game can't make it (an unknown object or weapon).</summary>
    public static GroundItem? Create(string json)
    {
        Game.CheckRunning("GroundItems.Create");
        if (DsMap.FromJson(json) is not { } saved)
            return null;
        try
        {
            return Create(saved);
        }
        finally
        {
            saved.Destroy();
        }
    }

    /// <summary>An item made from its saved state as a map (<see cref="GroundItem.Save"/>); the map stays the
    /// caller's.</summary>
    public static GroundItem? Create(DsMap saved)
    {
        Game.CheckRunning("GroundItems.Create");
        if (saved.Get("object_name", "N/A") is not { Kind: GmKind.String } name || name.AsString == "N/A")
            return null;
        Instance made = InstanceOf(Game.CallScript("scr_locationRoomEntityLootInstanceCreate", default, saved.Id));
        if (made.IsNone || !made.Exists)
            return null;
        Game.CallScript("scr_locationRoomEntityLootSaveDataSet", default, made, saved.Id);
        return new GroundItem(made);
    }

    /// <summary>Puts a new item on the ground, on the cell at (<paramref name="x"/>, <paramref name="y"/>): one of the
    /// game's items by its o_inv_ object's name less "o_inv_" ("wine"), or a weapon or armour by its name (the game's,
    /// or a mod's) at <paramref name="quality"/>. With <paramref name="hop"/> it's tossed onto a free neighbouring tile,
    /// as the game drops loot; without, it lies where it's put. Null if there's no such item.</summary>
    public static GroundItem? Spawn(string name, double x, double y, bool hop = false, ItemQuality quality = ItemQuality.Rolled)
    {
        Game.CheckRunning("GroundItems.Spawn");
        string key = ModIdentity.ToGameKey(name);
        int loot = Game.CallBuiltin("asset_get_index", "o_loot_" + key).AsInt;
        // (100: the drop chance, always.)
        GmValue made = loot >= 0 && Game.CallBuiltin("object_exists", loot).AsBool
            ? Game.CallScript("scr_loot", default, loot, x, y, 100)
            : Game.CallScript("scr_weapon_loot", default, key, x, y, 100, (int)quality);
        Instance instance = InstanceOf(made);
        if (instance.IsNone || !instance.Exists)
            return null;
        var item = new GroundItem(instance);
        if (!hop)
            item.Land();
        return item;
    }

    // A script's instance: a reference, or a number (noone, -4: none); kept by its id.
    internal static Instance InstanceOf(GmValue value) => value.Kind switch
    {
        GmKind.Instance => value.AsInstance.Persist(),
        GmKind.Real when value.AsInt >= 0 => Instance.FromId(value.AsInt),
        _ => default,
    };
}

/// <summary>An item on the ground (<see cref="GroundItems"/>). Off screen (culled) it's still there: what's read or
/// changed of it wakes it for the moment and puts it back.</summary>
public readonly record struct GroundItem(Instance Instance)
{
    /// <summary>Whether it's still on the ground (on screen or off): false once picked up or destroyed.</summary>
    public bool IsOnGround => !Instance.IsGone;

    /// <summary>Where it is (in the air, where it is in its hop).</summary>
    public double X => Instance.Get("x").AsReal;
    public double Y => Instance.Get("y").AsReal;

    /// <summary>Its object's name: "o_loot_wine", or "o_weapon_loot" for every weapon and armour.</summary>
    public string ObjectName => Game.CallBuiltin("object_get_name", Instance.Get("object_index")).AsString;

    /// <summary>Whether it's part of the location itself - placed with it, never saved - rather than dropped or found
    /// there ("dynamic"). The game saves only the dynamic ones, so only they have a saved state.</summary>
    public bool IsStatic
    {
        get
        {
            Instance i = Instance;
            return Awake(() => i.Get("roomEntityType") is { Kind: GmKind.String } type && type.AsString == "static");
        }
    }

    /// <summary>Its saved state as the game keeps it in a location's save (scr_locationRoomEntityLootSaveDataGet): its
    /// object or weapon, where it lies, its stack, charge and item data - JSON; null for a static item, which the game
    /// doesn't save (another game has it already, with the location). <see cref="GroundItems.Create(string)"/> makes it
    /// back.</summary>
    public string? ToJson()
    {
        if (Save() is not { } saved)
            return null;
        try
        {
            return saved.ToJson();
        }
        finally
        {
            saved.Destroy();
        }
    }

    /// <summary>Its saved state as a new map (<see cref="ToJson"/>), or null for a static item: <see cref="DsMap.Destroy"/>
    /// it when done.</summary>
    public DsMap? Save()
    {
        if (IsStatic)
            return null;
        Instance instance = Instance;
        return Awake(() => Game.CallScript("scr_locationRoomEntityLootSaveDataGet", default, instance).AsDsMap);
    }

    /// <summary>Whether it's in the air: the hop a dropped item makes onto a neighbouring tile.</summary>
    public bool InFlight => Instance.Get("speed").AsReal != 0 || Instance.Get("gravity").AsReal != 0;

    /// <summary>Its hop while it's in the air - where it is, the tile it's landing on, its speed, gravity and spin - all a
    /// copy needs to fly the same arc and land in the same place (<see cref="Fly"/>); null once it has landed.</summary>
    public ItemFlight? Flight
    {
        get
        {
            Instance i = Instance;
            return !InFlight ? null : Awake<ItemFlight?>(() =>
            {
                double y = i.Get("y").AsReal;
                // (A weapon draws at yy as it flies.)
                GmValue drawY = i.Get("yy");
                return new ItemFlight(i.Get("x").AsReal, y, i.Get("targ_x").AsReal, i.Get("targ_y").AsReal,
                    i.Get("hspeed").AsReal, i.Get("vspeed").AsReal, i.Get("gravity").AsReal,
                    drawY.Kind == GmKind.Real ? drawY.AsReal : y, i.Get("image_angle").AsReal);
            });
        }
    }

    /// <summary>Puts it in the air as <paramref name="flight"/> has it (another item's <see cref="Flight"/>, perhaps in
    /// another game): the game's own step then flies it along the same arc and lands it on the same tile, with its sound
    /// and dust.</summary>
    public void Fly(ItemFlight flight)
    {
        Instance i = Instance;
        Awake(() =>
        {
            i.Set("x", flight.X);
            i.Set("y", flight.Y);
            i.Set("targ_x", flight.TargetX);
            i.Set("targ_y", flight.TargetY);
            i.Set("hspeed", flight.HSpeed);
            i.Set("vspeed", flight.VSpeed);
            i.Set("gravity", flight.Gravity);
            if (i.Get("yy").Kind == GmKind.Real)
                i.Set("yy", flight.DrawY);
            i.Set("image_angle", flight.Angle);
            return true;
        });
    }

    /// <summary>Lands it where it is now, its hop cut short (its user event 2, as the game lands loot it loads).</summary>
    public void Land()
    {
        Instance i = Instance;
        Awake(() => Game.CallBuiltinAs("event_user", i, i, 2));
    }

    // Runs read with it awake: a culled item's own variables can't be read or set, nor can the game's with() reach it -
    // activated for the moment, then deactivated again (still in its culling controller's list, as it was).
    private T Awake<T>(Func<T> read)
    {
        if (!Instance.IsCulled)
            return read();
        Game.CallBuiltin("instance_activate_object", Instance.Id);
        try
        {
            return read();
        }
        finally
        {
            Game.CallBuiltin("instance_deactivate_object", Instance.Id);
        }
    }
}

/// <summary>A ground item's hop through the air (<see cref="GroundItem.Flight"/>, <see cref="GroundItem.Fly"/>): where it
/// is, the tile it's landing on, its speeds, gravity, the height it's drawn at (a weapon's) and its spin.</summary>
public readonly record struct ItemFlight(double X, double Y, double TargetX, double TargetY, double HSpeed, double VSpeed,
    double Gravity, double DrawY, double Angle)
{
    public string ToJson() => new JsonObject
    {
        ["x"] = X, ["y"] = Y, ["tx"] = TargetX, ["ty"] = TargetY,
        ["hs"] = HSpeed, ["vs"] = VSpeed, ["g"] = Gravity, ["yy"] = DrawY, ["a"] = Angle,
    }.ToJsonString();

    /// <summary>A flight from <see cref="ToJson"/>'s text; null if it isn't one.</summary>
    public static ItemFlight? FromJson(string json)
    {
        if (GmJson.Parse(json) is not JsonObject o)
            return null;
        double? N(string name) => o[name] is JsonValue v && v.TryGetValue(out double d) ? d : null;
        if (N("x") is not { } x || N("y") is not { } y || N("tx") is not { } tx || N("ty") is not { } ty
            || N("hs") is not { } hs || N("vs") is not { } vs || N("g") is not { } g)
            return null;
        return new ItemFlight(x, y, tx, ty, hs, vs, g, N("yy") ?? y, N("a") ?? 0);
    }
}
