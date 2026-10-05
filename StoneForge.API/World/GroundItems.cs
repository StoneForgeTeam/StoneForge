using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>Items lying on the ground (the game's o_loot and its children: o_loot_wine, o_weapon_loot...): finding them,
/// an item's saved state in the game's own save format and an item made back from it, new items put down, and an item
/// still in the air - the hop a drop makes onto a neighbouring tile - read and replayed; and an item coming onto the
/// ground or leaving it (<see cref="OnAdded"/>, <see cref="OnRemoved"/>). What a stash, trade or loot mod needs to move
/// items between places, players or games. Game thread only.</summary>
public static class GroundItems
{
    /// <summary>Every item on the ground in the room - those off screen too, unless <paramref name="includeCulled"/> is
    /// false (the game culls ground items it takes off screen: see <see cref="Instance.IsCulled"/>).</summary>
    public static IReadOnlyList<GroundItem> All(bool includeCulled = true)
        => Instances.All(GameObjectId.o_loot, includeCulled).Select(instance => new GroundItem(instance)).ToArray();

    /// <summary>Runs as an item comes onto the ground in play - dropped or thrown by anyone, left by a kill, an arrow's
    /// ammo, or made by a mod (<see cref="Create(string)"/>, <see cref="Spawn"/>) - on the frame after it's made, its data
    /// set (one gone again by then: nothing). Not the items a place has as it loads, nor those a room is built with.</summary>
    public static void OnAdded(ModContext context, Action<GroundItem> handler)
    {
        var loading = new PlaceLoading(context);
        var added = new List<Instance>();
        context.OnCode("gml_Object_o_loot_Create_0", after: (item, _) =>
        {
            if (!loading.Now && !item.IsNone)
                added.Add(item.Persist());
        });
        context.Frame += () =>
        {
            loading.Frame();
            if (added.Count == 0)
                return;
            var made = added.ToArray();
            added.Clear();
            foreach (Instance item in made)
                if (!item.IsGone)
                    handler(new GroundItem(item));
        };
    }

    /// <summary>Runs as an item leaves the ground in play - picked up, destroyed (burnt...), or taken away by a mod -
    /// while it's still there to read (its Destroy event). Not the items left behind as the room is left.</summary>
    public static void OnRemoved(ModContext context, Action<GroundItem> handler)
        => context.OnCode("gml_Object_o_loot_Destroy_0", before: (item, _) =>
        {
            if (!item.IsNone)
                handler(new GroundItem(item.Persist()));
            return false;
        });

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
