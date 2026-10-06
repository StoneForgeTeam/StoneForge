namespace StoneForge;

// Mods' consumables (Consumable, Items.Add): each made the game's own - its object o_inv_<key> the patcher added
// (a child of the game consumable's, so the game makes, saves, stacks and drops it as its own) - with its row
// of table_items_stats (scr_stonemod_consum_define, once the game has loaded the table: its text loader's step
// 25), its name and description (global.consum_name / consum_desc: step 1), its pictures (an item's s_index as
// it's made, the ground object's sprite) and its OnUse (o_inv_consum's user event 14, the game's use).
internal static class Consumables
{
    private sealed class Entry
    {
        public required ModContext Context;
        public required Consumable Consumable;
        public int Object = -1, LootObject = -1, Sprite = -1, LootSprite = -1;
        public bool Defined;
    }

    private static readonly Dictionary<string, Entry> ByKey = new();
    // By their objects (-1 when the game data has none yet).
    private static readonly Dictionary<int, Entry> ByObject = new(), ByLootObject = new();
    // Keep only keys and object IDs after unload, never mod instances. Scan on subsequent frames so
    // instances restored from a chest/room later in this session are removed too.
    private static readonly Dictionary<int, (string Key, bool OnGround)> Orphans = new();

    internal static void Add(ModContext context, Consumable consumable)
    {
        string gameKey = context.GameKey(consumable.Key);
        if (ByKey.TryGetValue(gameKey, out var existing))
            throw new ArgumentException($"There's already a consumable \"{context.ContentId(consumable.Key)}\" (from {existing.Context.Name})");
        consumable.Attach(context);
        var entry = new Entry { Context = context, Consumable = consumable };
        ByKey[consumable.GameKey] = entry;
        if (Game.Running && TableLoaded && Items.ModsLoaded())
            Define(entry);
    }

    internal static void RemoveMod(string mod)
    {
        foreach (var (key, entry) in ByKey.Where(e => e.Value.Context.Id == mod).ToList())
        {
            ByKey.Remove(key);
            ByObject.Remove(entry.Object);
            ByLootObject.Remove(entry.LootObject);
            if (entry.Object >= 0) Orphans[entry.Object] = (key, false);
            if (entry.LootObject >= 0) Orphans[entry.LootObject] = (key, true);
        }
    }

    internal static void RemoveOrphans()
    {
        if (!Game.Running || !Items.ModsLoaded()) return;
        foreach (var (obj, orphan) in Orphans.ToArray())
        {
            // Registration can precede table definition when a mod is re-enabled.
            if (ByKey.ContainsKey(orphan.Key)) { Orphans.Remove(obj); continue; }
            try
            {
                int count = Game.CallBuiltinTrusted("instance_number", default, default, obj).AsInt;
                // Snapshot IDs before destroying: destruction can reorder the runner's instance list.
                var instances = new List<Instance>();
                for (int i = 0; i < count; i++)
                {
                    Instance found = Game.CallBuiltinTrusted("instance_find", default, default, obj, i);
                    if (!found.IsNone) instances.Add(found.Persist());
                }
                foreach (var item in instances)
                {
                    if (ByKey.ContainsKey(orphan.Key)) break;
                    if (!item.Exists || item.Get("object_index").AsInt != obj) continue;
                    if (orphan.OnGround)
                        Game.CallBuiltinTrusted("instance_destroy", default, default, item.Id);
                    else
                        Game.CallScript("scr_item_destroy", default, item.Id);
                }
            }
            catch (Exception e) { Game.Log($"couldn't remove unloaded consumable {orphan.Key}: {e.Message}"); }
        }
    }

    // (Through Game.Global: undefined - not a number - until the text loader has made it.)
    private static bool TableLoaded => Game.Global["consum_stat_data"].Kind == GmKind.Real;
    private static bool NamesLoaded => Game.Global["consum_name"].Kind == GmKind.Real;

    internal static void Install(ModContext loader)
    {
        // Defined once the game has its table (mods load before), or as they're added after; named once it has
        // its names.
        Events.o_textLoader.Other_25.After(loader, textLoader =>
        {
            int step = textLoader.Instance.Get("number").AsInt;
            if (step == 25)
                DefinePending();
            else if (step == 1)
                foreach (var entry in ByKey.Values.Where(e => e.Defined))
                    SetNames(entry);
        });
        loader.Frame += () =>
        {
            if (ByKey.Count > 0 && Game.Running && TableLoaded && Items.ModsLoaded())
                DefinePending();
            RemoveOrphans();
        };
        // Its inventory picture: the item's, as the game sets it (from its object's sprite) in o_inv_slot's Create.
        loader.OnCode("gml_Object_o_inv_slot_Create_0", after: (self, _) =>
        {
            if (ByObject.Count > 0 && ByObject.TryGetValue(self.Get("object_index").AsInt, out var entry) && entry.Sprite >= 0)
                self.Set("s_index", entry.Sprite);
        });
        // On the ground: the loot object's sprite.
        loader.OnCode("gml_Object_o_consument_loot_Create_0", after: (self, _) =>
        {
            if (ByLootObject.Count > 0 && ByLootObject.TryGetValue(self.Get("object_index").AsInt, out var entry) && entry.LootSprite >= 0)
                self.Set("sprite_index", entry.LootSprite);
        });
        // Used: the mod's OnUse, as the game's use begins.
        loader.OnCode("gml_Object_o_inv_consum_Other_24", before: (self, _) =>
        {
            if (ByObject.Count > 0 && ByObject.TryGetValue(self.Get("object_index").AsInt, out var entry))
            {
                try { entry.Consumable.OnUse(self); }
                catch (Exception e) { entry.Context.Log($"consumable {entry.Consumable.Id}: OnUse threw: {e}"); }
            }
            return false;
        });
    }

    private static void DefinePending()
    {
        foreach (var entry in ByKey.Values.Where(e => !e.Defined).ToList())
            Define(entry);
    }

    private static void Define(Entry entry)
    {
        entry.Defined = true;
        var item = entry.Consumable;
        try
        {
            entry.Object = Gm.AssetGetIndex("o_inv_" + item.GameKey);
            if (entry.Object < 0)
            {
                entry.Context.Log($"consumable \"{item.Id}\": the game has no object for it yet - it's added when the game starts (restart it)");
                return;
            }
            if (!ItemData.DefineConsumable(item.GameKey, item.BasedOn, item.ColumnsText))
            {
                entry.Context.Log($"consumable \"{item.Id}\": the game has no consumable \"{item.BasedOn}\" to base it on");
                return;
            }
            ByObject[entry.Object] = entry;
            entry.LootObject = Gm.AssetGetIndex("o_loot_" + item.GameKey);
            if (entry.LootObject >= 0)
                ByLootObject[entry.LootObject] = entry;
            if (item.InventorySprite != null)
                entry.Sprite = LoadLike(entry, item.InventorySprite, ObjectSprite(entry.Object));
            if (item.LootSprite != null && entry.LootObject >= 0)
                entry.LootSprite = LoadLike(entry, item.LootSprite, ObjectSprite(entry.LootObject));
            if (NamesLoaded)
                SetNames(entry);
            entry.Context.Log($"consumable \"{item.Id}\" added (based on {item.BasedOn})");
        }
        catch (Exception e)
        {
            entry.Context.Log($"consumable \"{item.Id}\" couldn't be added: {e.Message}");
        }
    }

    // Its name and description in the game's text maps, and its tooltip's middle part (consum_mid: the game item's
    // line of what it does - "Causes light Drunkenness").
    private static void SetNames(Entry entry)
    {
        var item = entry.Consumable;
        foreach (var (map, value) in new (string Map, string? Value)[] { ("consum_name", item.DisplayName), ("consum_mid", null), ("consum_desc", item.Description) })
        {
            GmValue texts = Game.Global[map];
            if (texts.Kind != GmKind.Real)
                continue;
            GmValue text = value != null ? value : Game.CallBuiltinTrusted("ds_map_find_value", default, default, texts, item.BasedOn);
            if (text.Kind != GmKind.Undefined)
                Game.CallBuiltinTrusted("ds_map_set", default, default, texts, item.GameKey, text);
        }
    }

    // A picture from the mod's Assets folder made like the game's (like): its frames side by side - said if the
    // size is off - and the same origin.
    private static int LoadLike(Entry entry, string file, int like)
    {
        int frames = like >= 0 ? Math.Max(1, Game.CallBuiltinTrusted("sprite_get_number", default, default, like).AsInt) : 1;
        int sprite = entry.Context.LoadSprite(file, frames);
        if (sprite < 0)
        {
            entry.Context.Log($"consumable \"{entry.Consumable.Id}\": no picture Assets\\{file}");
            return -1;
        }
        if (like >= 0)
        {
            double width = Draw.SpriteWidth(sprite), height = Draw.SpriteHeight(sprite);
            double likeWidth = Draw.SpriteWidth(like), likeHeight = Draw.SpriteHeight(like);
            if (width != likeWidth || height != likeHeight)
                entry.Context.Log($"consumable \"{entry.Consumable.Id}\": {file} has {frames} frame(s) of {width}x{height}; the game item's has {frames} of {likeWidth}x{likeHeight} - it should be {likeWidth * frames}x{likeHeight}");
            Game.CallBuiltinTrusted("sprite_set_offset", default, default, sprite,
                Game.CallBuiltinTrusted("sprite_get_xoffset", default, default, like), Game.CallBuiltinTrusted("sprite_get_yoffset", default, default, like));
        }
        return sprite;
    }

    // The mod a consumable in the game is from, for its tooltip (ModNameTooltip) - null if it isn't a mod's, or its
    // ShowModName is off.
    internal static string? ModNameOf(Instance item)
        => ByObject.Count > 0 && ByObject.TryGetValue(item.Get("object_index").AsInt, out var entry) && entry.Consumable.ShowModName ? entry.Context.Name : null;

    private static int ObjectSprite(int obj) => Game.CallBuiltinTrusted("object_get_sprite", default, default, obj).AsInt;
}
