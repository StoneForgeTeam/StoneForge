namespace StoneForge;

/// <summary>The markers players put on the world map. The game keeps them in its save
/// (global.globalmapUserMarksList: four values each) while the map is closed; while it's open they're the map's own
/// marker instances (o_globalmapMarkUser, in its marksContainer), written back to the save when it closes. Read and
/// set here either way: <see cref="All"/> reads whichever is current, and <see cref="Set"/> changes both - the open
/// map's markers are made again on the spot, as the game places one. <see cref="OnPlaced"/> and <see cref="OnRemoved"/>
/// run as the player places one on the map, or takes one off. Game thread only, with a world map
/// (<see cref="WorldMap.Available"/>).</summary>
/// <example><code>
/// var markers = MapMarkers.All().ToList();
/// markers.Add(new MapMarker(MapMarkers.Sprites[4], 0, new Point(30 * MapMarkers.CellSize, 12 * MapMarkers.CellSize)));
/// MapMarkers.Set(markers);
/// MapMarkers.OnPlaced(context, marker => context.Log($"A marker on {marker.Tile}"));
/// </code></example>
public static class MapMarkers
{
    /// <summary>The world map's pixels to a cell.</summary>
    public const int CellSize = 52;

    /// <summary>The sprites a player chooses a marker from, in the order the map's menu offers them.</summary>
    public static IReadOnlyList<string> Sprites { get; } = new[]
    {
        "s_glmap_mark_user_Grave", "s_glmap_mark_user_Sword", "s_glmap_mark_user_Crown", "s_glmap_mark_user_Bow",
        "s_glmap_mark_user_Flag", "s_glmap_mark_user_Diamond", "s_glmap_mark_user_Arrow", "s_glmap_mark_user_Food",
        "s_glmap_mark_user_Tower", "s_glmap_mark_user_Cross", "s_glmap_mark_user_Chest", "s_glmap_mark_user_Skull",
    };

    /// <summary>Whether the world map is open (its markers are its instances meanwhile).</summary>
    public static bool MapOpen => !Map().IsNone;

    /// <summary>Every marker on the world map; none without one.</summary>
    public static IReadOnlyList<MapMarker> All()
    {
        var markers = new List<MapMarker>();
        Instance map = Map();
        if (!map.IsNone)
        {
            double scale = Game.Global["glmapScale"].AsReal;
            foreach (Instance mark in Placed(map))
                markers.Add(new MapMarker(Game.CallBuiltin("sprite_get_name", mark.Get("sprite_index")).AsString ?? "",
                    mark.Get("image_index").AsInt,
                    new Point(mark.Get("guiLayoutOffsetLeft").AsReal / scale, mark.Get("guiLayoutOffsetTop").AsReal / scale)));
            return markers;
        }
        if (Saved() is not { } list)
            return markers;
        for (int i = 0; i + 3 < list.Count; i += 4)
            markers.Add(new MapMarker(list[i].AsString ?? "", list[i + 1].AsInt, new Point(list[i + 2].AsReal, list[i + 3].AsReal)));
        return markers;
    }

    /// <summary>Makes the world map's markers these (in place of all there were). With the map open, its markers are
    /// made again as the game places one; a marker whose sprite the game doesn't have is left out there.</summary>
    public static void Set(IEnumerable<MapMarker> markers)
    {
        var wanted = markers.ToList();
        if (Saved() is { } list)
        {
            list.Clear();
            foreach (MapMarker marker in wanted)
            {
                list.Add(marker.Sprite);
                list.Add(marker.Image);
                list.Add(marker.Position.X);
                list.Add(marker.Position.Y);
            }
        }
        Instance map = Map();
        if (map.IsNone)
            return;
        // (The map's pointer may hold one we take away: right-click removes the marker it holds.)
        foreach (Instance pointer in Instances.All(GameObjectId.o_globalmapInteractiveNormal))
            if (Instance.Of(pointer.Get("markID")) is { IsNone: false } held && held.Get("object_index").AsInt == (int)GameObjectId.o_globalmapMarkUser)
                pointer["markID"] = -4;
        foreach (Instance mark in Placed(map))
            mark.Destroy();
        double depth = map.Get("depth").AsReal - 10;
        foreach (MapMarker marker in wanted)
        {
            int sprite = Gm.AssetGetIndex(marker.Sprite);
            if (sprite < 0)
                continue;
            WorldTile cell = marker.Tile;
            Instance mark = Instance.Of(Game.CallScript("scr_globalmapMarkCreate", default, (int)GameObjectId.o_globalmapMarkUser, sprite,
                marker.Image, cell.X, cell.Y, marker.Position.X - cell.X * CellSize, marker.Position.Y - cell.Y * CellSize));
            if (mark.IsNone)
                continue;
            mark["percent"] = 100;
            mark["depth"] = depth;
        }
        GmValue container = map.Get("marksContainer");
        Game.CallScript("scr_guiContainerRebuild", default, container);
        Game.CallScript("scr_guiContainerUpdate", default, container);
    }

    /// <summary>Puts a marker on the world map (with the others).</summary>
    public static void Add(MapMarker marker) => Set(All().Append(marker));

    /// <summary>Takes the markers equal to this one off the world map; false if there were none.</summary>
    public static bool Remove(MapMarker marker)
    {
        var markers = All().ToList();
        if (markers.RemoveAll(m => m == marker) == 0)
            return false;
        Set(markers);
        return true;
    }

    /// <summary>Runs as the player places a marker on the open map (its menu's choice, where they clicked) - after it's
    /// there. Not for markers a mod sets.</summary>
    public static void OnPlaced(ModContext context, Action<MapMarker> handler)
    {
        // (The menu's choice clicked: its user event 15 in state 2 places the marker - and takes off any it covers.)
        List<MapMarker>? before = null;
        context.OnCode(PlaceCode,
            before: (choice, _) =>
            {
                before = choice.Get("guiInteractiveState").AsInt == 2 ? All().ToList() : null;
                return false;
            },
            after: (_, _) =>
            {
                if (before is not { } was)
                    return;
                before = null;
                foreach (MapMarker placed in Except(All(), was))
                    handler(placed);
            });
    }

    /// <summary>Runs as the player takes a marker off the open map - right-clicking it, or placing another over it -
    /// after it's gone. Not for markers a mod takes off.</summary>
    public static void OnRemoved(ModContext context, Action<MapMarker> handler)
    {
        List<MapMarker>? before = null;
        void Before(bool changing) => before = changing ? All().ToList() : null;
        void After()
        {
            if (before is not { } was)
                return;
            before = null;
            foreach (MapMarker removed in Except(was, All()))
                handler(removed);
        }
        // (A right-click on the map's pointer while it holds a player's marker: its user event 15 in state 5.)
        context.OnCode(RemoveCode,
            before: (pointer, _) =>
            {
                Before(pointer.Get("guiInteractiveState").AsInt == 5 && Instance.Of(pointer.Get("markID")) is { IsNone: false } held
                    && held.Get("object_index").AsInt == (int)GameObjectId.o_globalmapMarkUser);
                return false;
            },
            after: (_, _) => After());
        context.OnCode(PlaceCode,
            before: (choice, _) =>
            {
                Before(choice.Get("guiInteractiveState").AsInt == 2);
                return false;
            },
            after: (_, _) => After());
    }

    private const string PlaceCode = "gml_Object_o_globalmapMarkUserContext_Other_25";
    private const string RemoveCode = "gml_Object_o_globalmapInteractiveNormal_Other_25";

    // The markers in one set and not the other (each as many times as it's in one more than the other).
    private static List<MapMarker> Except(IEnumerable<MapMarker> markers, IEnumerable<MapMarker> without)
    {
        var left = markers.ToList();
        foreach (MapMarker marker in without)
            left.Remove(marker);
        return left;
    }

    private static Instance Map() => Instances.All(GameObjectId.o_globalmap).FirstOrDefault();

    // The save's list of markers (four values each); null without a world map.
    private static DsList? Saved() => DsList.From(Game.Global["globalmapUserMarksList"]) is { Exists: true } list ? list : null;

    // The open map's markers players put there: its marks container's children of o_globalmapMarkUser - what the game
    // writes back to the save as the map closes.
    private static List<Instance> Placed(Instance map)
    {
        var placed = new List<Instance>();
        if (Instance.Of(map.Get("marksContainer")) is not { IsNone: false } container
            || DsList.From(container.Get("guiChildrenList")) is not { Exists: true } children)
            return placed;
        for (int i = 0; i < children.Count; i++)
            if (Instance.Of(children[i]) is { IsNone: false } child && child.Exists
                && child.Get("object_index").AsInt == (int)GameObjectId.o_globalmapMarkUser)
                placed.Add(child);
        return placed;
    }
}
