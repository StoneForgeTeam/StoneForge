namespace StoneForge;

// The game's item tables, as mods' items are added to them and given (Items, Consumables) - what were the patcher's GML
// scripts scr_stonemod_item_define, _consum_define, _item_give, _item_sprite and _item_sprite_like; C# on both builds
// (the native one can't run GML). The tables are the game's own: global.weapons_csv / armor_csv (a row per weapon or
// armour, the first its header) with their stats in global.weapons_stat and their pictures and sounds in
// global.weapons_asset_data; global.consum_csv (its header the row whose id is "id") with its stats in
// global.consum_stat_data. A row's stats are made as the game makes its own: scr_array2d_to_map.
internal static class ItemData
{
    /// <summary>A mod's weapon or armour: <paramref name="basedOn"/>'s row copied under <paramref name="name"/>, with
    /// some columns changed (<paramref name="columns"/>: "column=value" pairs joined by "|"), and its pictures and sounds
    /// copied from it. In the random loot tables too with <paramref name="toLoot"/>. Its asset map (its pictures, for the
    /// loader to change); null if there's no such game item.</summary>
    internal static DsMap? DefineItem(bool armor, string name, string basedOn, string columns, bool toLoot)
    {
        using GmArray? csv = Global(armor ? "armor_csv" : "weapons_csv").AsArray;
        if (csv == null || csv.Length < 1)
            return null;
        using GmArray? header = csv[0].AsArray;
        if (header == null)
            return null;
        GmArray? baseRow = null;
        for (int i = 1; i < csv.Length && baseRow == null; i++)
        {
            GmArray? row = csv[i].AsArray;
            if (row != null && row[0].AsString == basedOn)
                baseRow = row;
            else
                row?.Dispose();
        }
        if (baseRow == null)
            return null;
        GmArray made;
        using (baseRow)
            made = Copied(baseRow, header.Length, name, header, columns);
        using (made)
        {
            if (Global("weapons_stat").AsDsMap is { } stats)
            {
                if (stats.Has(name))
                    stats.Remove(name);
                using GmArray table = GmArray.From(new GmValue[] { header, made });
                Game.CallScript("scr_array2d_to_map", default, table, stats.Id, Global("weapon_string_attribute"));
            }
            if (toLoot)
                csv.Push(made);
        }
        // (Its pictures and sounds: the game item's.)
        var assets = DsMap.Create();
        if (Global("weapons_asset_data").AsDsMap is { } all)
        {
            if (all.GetMap(basedOn) is { } baseAssets)
                Builtin("ds_map_copy", assets.Id, baseAssets.Id);
            if (all.Has(name))
                all.Remove(name);
            all.AddMap(name, assets);
        }
        return assets;
    }

    /// <summary>A mod's consumable: <paramref name="basedOn"/>'s row of table_items_stats copied under <paramref name="key"/>
    /// (or put in place of its own, defined again), with some columns changed, and its stats made. Its object, o_inv_key,
    /// the patcher added. False if there's no such row.</summary>
    internal static bool DefineConsumable(string key, string basedOn, string columns)
    {
        using GmArray? csv = Global("consum_csv").AsArray;
        if (csv == null)
            return false;
        int headerAt = -1, baseAt = -1, existing = -1;
        for (int i = 0; i < csv.Length; i++)
        {
            using GmArray? row = csv[i].AsArray;
            string? id = row?[0].AsString;
            if (id == "id")
                headerAt = i;
            else if (id == basedOn)
                baseAt = i;
            else if (id == key)
                existing = i;
        }
        if (headerAt < 0 || baseAt < 0)
            return false;
        using GmArray header = csv[headerAt].AsArray!;
        GmArray made;
        using (GmArray baseRow = csv[baseAt].AsArray!)
            made = Copied(baseRow, header.Length, key, header, columns);
        using (made)
        {
            if (existing >= 0)
                csv[existing] = made;
            else
                csv.Push(made);
            if (Global("consum_stat_data").AsDsMap is { } stats)
            {
                if (stats.Has(key))
                    stats.Remove(key);
                using GmArray table = GmArray.From(new GmValue[] { header, made });
                Game.CallScript("scr_array2d_to_map", default, table, stats.Id, Global("consum_string_attribute"));
            }
        }
        return true;
    }

    /// <summary>Gives the player an item, as the game's own console command does: a weapon or armour by its name, with a
    /// quality (-4: rolled) and durability in % (null: as rolled); or a consumable by its o_inv_ object's name (less the
    /// prefix). Whether it went into the inventory (a weapon that doesn't fit is dropped at the player's feet).</summary>
    internal static bool Give(string name, int quality, double? durabilityPercent)
    {
        Instance inventory = Instances.All(GameObjectId.o_inventory).FirstOrDefault();
        if (inventory.IsNone)
            return false;
        int obj = Gm.AssetGetIndex("o_inv_" + name);
        if (obj >= 0 && Builtin("object_exists", obj).AsBool)
            return !IsNone(Game.CallScript("scr_inventory_add_item", inventory, obj));
        GmValue item = Game.CallScript("scr_inventory_add_weapon", inventory, name, quality);
        if (IsNone(item))
            return false;
        if (durabilityPercent is { } durability && Instance.Of(item) is { IsNone: false } slot
            && slot.Get("data").AsDsMap is { } data)
            data["Duration"] = durability;
        return true;
    }

    /// <summary>One of a mod's item's pictures: <paramref name="sprite"/> put in its asset map as
    /// <paramref name="key"/> ("char_sprite"...), lined up as the game item's picture it replaces.</summary>
    internal static void SetSprite(GmValue assets, string key, int sprite)
    {
        if (DsMap.From(assets) is not { } map)
            return;
        SpriteLike(sprite, map[key]);
        map[key] = sprite;
    }

    /// <summary>A mod's item picture lined up as one of the game's: the same origin, and for a worn picture the same anchor
    /// on the body (global.customizationAnchors - the game sets a worn picture's origin from it each time it's put on).</summary>
    internal static void SpriteLike(int sprite, GmValue like)
    {
        if (like.Kind != GmKind.Real || like.AsReal < 0 || !Builtin("sprite_exists", like).AsBool)
            return;
        Builtin("sprite_set_offset", sprite, Builtin("sprite_get_xoffset", like), Builtin("sprite_get_yoffset", like));
        if (Global("customizationAnchors").AsDsMap is { } anchors && anchors[like] is { IsUndefined: false } anchor)
            anchors[sprite] = anchor;
    }

    // A row: the base's, as wide as the header, under a new id, with the columns given ("column=value|...") set.
    private static GmArray Copied(GmArray baseRow, int width, string id, GmArray header, string columns)
    {
        var values = new GmValue[width];
        int baseWidth = baseRow.Length;
        for (int i = 0; i < width; i++)
            values[i] = i < baseWidth ? baseRow[i] : "";
        values[0] = id;
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < width; i++)
            if (header[i].AsString is { } column)
                names.TryAdd(column, i);
        foreach (string pair in columns.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0 && names.TryGetValue(pair[..equals], out int at))
                values[at] = pair[(equals + 1)..];
        }
        return GmArray.From(values);
    }

    private static bool IsNone(GmValue value) => value.Kind == GmKind.Undefined || (value.Kind == GmKind.Real && value.AsReal == -4);

    private static GmValue Global(string name) => Game.Global[name];

    private static GmValue Builtin(string name, params GmValue[] args) => Game.CallBuiltinTrusted(name, default, default, args);
}
