using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>An item on the ground (<see cref="GroundItems"/>). Off screen (culled) it's still there: what's read or
/// changed of it wakes it for the moment and puts it back.</summary>
public readonly record struct GroundItem(Instance Instance)
{
    /// <summary>A mod's own values on this item as it lies on the ground (its data, which it takes into an inventory and
    /// back) - under keys only that mod uses (<see cref="StoneForge.ModData"/>).</summary>
    public ModData ModData(ModContext context)
    {
        Instance item = Instance;
        return StoneForge.ModData.InMap(context, () => item.Get("data").AsDsMap);
    }

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
