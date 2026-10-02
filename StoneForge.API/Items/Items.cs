namespace StoneForge;

/// <summary>Items: mods' own weapons and armour (<see cref="Add(ModContext, ModItem)"/>), the ones in the game
/// (<see cref="ModItem.InGame"/>), and giving the player items (<see cref="Give(string, ItemQuality, double?)"/>).</summary>
public static class Items
{
    private sealed class Entry
    {
        public required ModContext Context;
        public required ModItem Item;
        public bool Defined;
    }

    private static readonly List<Entry> Added = new();
    private static readonly Dictionary<string, ModItem> ByName = new();
    // The mods' items the loader has seen in the game, and whether each was on last time it looked.
    private static readonly Dictionary<IntPtr, (Item Item, bool Equipped)> Seen = new();

    internal static IEnumerable<Item> Tracked => Seen.Values.Select(s => s.Item);

    /// <summary>Adds a mod's weapon or armour. It becomes an item like the game's own as soon as the game has
    /// loaded its item tables: it can be given, sold, worn, saved, and (<see cref="ModItem.InRandomLoot"/>)
    /// found. Call it from <see cref="IStoneMod.Load"/>.</summary>
    public static void Add(ModContext context, ModItem item)
    {
        string gameKey = context.GameKey(item.Key);
        var existing = Added.FirstOrDefault(e => e.Item.GameKey == gameKey);
        if (existing != null)
            throw new ArgumentException($"There's already an item \"{context.ContentId(item.Key)}\" (from {existing.Context.Name})");
        item.Attach(context);
        if (item is ITickable tickable)
            context.AddTickable(tickable);
        Added.Add(new Entry { Context = context, Item = item });
        ByName[item.GameKey] = item;
        // (Defined by the loader once the game runs and has its tables - see Install.)
        _pending = true;
    }

    /// <summary>Gives the player an item: a weapon or armour (the game's, by its name, or a mod's), or one of the
    /// game's other items by its o_inv_ object's name less "o_inv_" ("wine"). False if there's no player, no such
    /// item, or it didn't fit (a weapon that doesn't fit is dropped at the player's feet, as the game does).</summary>
    public static bool Give(string name, ItemQuality quality = ItemQuality.Rolled, double? durabilityPercent = null)
        => Game.CallScript("scr_stonemod_item_give", default, ModIdentity.ToGameKey(name), (int)quality, durabilityPercent ?? -4).AsBool;

    /// <summary>Adds a mod's consumable (food, a drink, a potion, a scroll - see <see cref="Consumable"/>). Call it from
    /// <see cref="IStoneMod.Load"/>.</summary>
    public static void Add(ModContext context, Consumable consumable) => Consumables.Add(context, consumable);

    /// <summary>Gives the player a mod's consumable (<paramref name="count"/> of it, stacked as the game stacks
    /// it). False if there's no player, or it didn't fit.</summary>
    public static bool Give(Consumable consumable, int count = 1)
    {
        bool given = false;
        for (int i = 0; i < Math.Max(1, count); i++)
            given |= Give(consumable.GameKey);
        return given;
    }

    /// <summary>Gives the player one of a mod's items.</summary>
    public static bool Give(ModItem item, ItemQuality quality = ItemQuality.Rolled, double? durabilityPercent = null)
        => Give(item.GameKey, quality, durabilityPercent);

    /// <summary>Whether the game knows a weapon or armour by this name (its own, or a mod's once added).</summary>
    public static bool Exists(string name)
    {
        Game.CheckRunning("Items.Exists");
        return TablesLoaded && Game.CallBuiltinTrusted("ds_map_exists", default, default, Game.Global["weapons_stat"], ModIdentity.ToGameKey(name)).AsBool;
    }

    /// <summary>A weapon's or armour's value in one of its table's columns (undefined if there's none).</summary>
    public static GmValue Stat(string name, string column)
    {
        Game.CheckRunning("Items.Stat");
        if (!TablesLoaded)
            return GmValue.Undefined;
        GmValue stats = Game.CallBuiltinTrusted("ds_map_find_value", default, default, Game.Global["weapons_stat"], ModIdentity.ToGameKey(name));
        return stats.Kind == GmKind.Real ? Game.CallBuiltinTrusted("ds_map_find_value", default, default, stats, column) : GmValue.Undefined;
    }

    public static GmValue Stat(string name, WeaponColumn column) => Stat(name, column.ToString());
    public static GmValue Stat(string name, ArmorColumn column) => Stat(name, column.ToString());

    // ---- the loader ----

    private static bool _pending;
    // Items defined this run by a mod (still in the game's tables after it's switched off: switched on again,
    // they're defined anew), and those already added to the random loot tables (only once).
    private static readonly HashSet<string> DefinedThisRun = new();
    private static readonly HashSet<string> InLoot = new();

    // A mod switched off while the game runs: its item classes are let go, and its items are removed from the
    // game - now (every one there is: inventories, chests, shops, the ground) and wherever they turn up later
    // (as at start for a mod that isn't loaded). They stay in the game's tables: a save made before still loads
    // them once the mod is back on.
    internal static void RemoveMod(string mod)
    {
        var gone = Added.Where(e => e.Context.Id == mod).ToList();
        if (gone.Count == 0)
            return;
        var names = new HashSet<string>();
        foreach (var entry in gone)
        {
            Added.Remove(entry);
            ByName.Remove(entry.Item.GameKey);
            Worn.Remove(entry.Item.GameKey);
            if (DefinedThisRun.Contains(entry.Item.GameKey))
                names.Add(entry.Item.GameKey);
        }
        foreach (var (pointer, seen) in Seen.ToList())
            if (gone.Any(e => e.Item == seen.Item.Type))
                Seen.Remove(pointer);
        if (names.Count == 0 || !Game.Running)
            return;
        Orphans.UnionWith(names);
        foreach (var (obj, list) in new[] { (GameObject.o_inv_slot, IdsToRemove), (GameObject.o_weapon_loot, LootIdsToRemove) })
            foreach (var found in Instances.All<GameInstance>(obj))
                if (IdName(found.Instance) is string name && names.Contains(name) && list.Add(found.Instance.Id))
                    Removed.Add(name);
    }

    // Every mod item ever added (dotnet\mod_items.json, beside the loader): so one whose mod is gone (removed or
    // switched off) is still recognised - kept loadable by a stand-in (its old base item) and removed wherever it
    // turns up (a save, a chest, a shop, the ground), instead of breaking the game.
    private sealed record Known(string Mod, string BasedOn, bool Armor);
    private static Dictionary<string, Known>? _known;
    private static readonly HashSet<string> Orphans = new();
    // Their items found in the game: removed next frame (not inside the game's own event that made them).
    private static readonly HashSet<Instance> ToRemove = new();
    private static readonly HashSet<Instance> LootToRemove = new();
    // (Found by a search, not in one of their events: by instance id.)
    private static readonly HashSet<int> IdsToRemove = new();
    private static readonly HashSet<int> LootIdsToRemove = new();
    private static readonly List<string> Removed = new();

    private static string KnownPath => Path.Combine(Path.GetDirectoryName(typeof(Items).Assembly.Location)!, "mod_items.json");

    private static Dictionary<string, Known> KnownItems
    {
        get
        {
            if (_known != null)
                return _known;
            try
            {
                _known = File.Exists(KnownPath)
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Known>>(File.ReadAllText(KnownPath)) ?? new()
                    : new();
            }
            catch (Exception e)
            {
                Game.Log("mod_items.json unreadable, starting a new one: " + e.Message);
                _known = new();
            }
            return _known;
        }
    }

    // This run's mods' items go in the record (and stay: their mod may be gone next time).
    private static void Remember()
    {
        bool changed = false;
        foreach (var entry in Added)
        {
            var known = new Known(entry.Context.Id, entry.Item.BasedOn, entry.Item.IsArmor);
            if (!KnownItems.TryGetValue(entry.Item.GameKey, out var old) || old != known)
            {
                KnownItems[entry.Item.GameKey] = known;
                changed = true;
            }
        }
        if (!changed)
            return;
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(KnownPath, System.Text.Json.JsonSerializer.Serialize(KnownItems, options));
        }
        catch (Exception e) { Game.Log("couldn't write mod_items.json: " + e.Message); }
    }

    // Stand-ins for the items of mods that aren't loaded: the game can load a save holding them, and the loader
    // then removes them.
    private static bool _orphansDefined;

    private static void DefineOrphans()
    {
        // (Once, at start: a mod switched off later keeps its items in the game until the next start.)
        if (_orphansDefined)
            return;
        _orphansDefined = true;
        foreach (var (name, known) in KnownItems)
        {
            if (ByName.ContainsKey(name) || Orphans.Contains(name))
                continue;
            Orphans.Add(name);
            try
            {
                if (Game.CallBuiltinTrusted("ds_map_exists", default, default, Game.Global["weapons_stat"], name).AsBool)
                    continue;
                GmValue assets = Game.CallScript("scr_stonemod_item_define", default, known.Armor, name, known.BasedOn, "", false);
                if (assets.Kind != GmKind.Real || assets.AsReal < 0)
                    Game.Log($"\"{name}\" ({known.Mod}, not loaded): no {known.BasedOn} to stand in for it");
            }
            catch (Exception e) { Game.Log($"\"{name}\" ({known.Mod}, not loaded): {e.Message}"); }
        }
        if (Orphans.Count > 0)
            Game.Log($"Items of mods that aren't loaded will be removed where they turn up: {string.Join(", ", Orphans.Select(n => $"{n} ({KnownItems[n].Mod})"))}");
    }

    // Removes what was found of them last frame, as the game removes items.
    private static void RemoveOrphans()
    {
        // (Each checked again first: its mod may have been switched back on since it was found.)
        var removed = new List<string>();
        void Remove(Instance item, bool onGround)
        {
            try
            {
                if (IdName(item) is not string name || !Orphans.Contains(name))
                    return;
                // (By its pointer it runs as the instance; found by id, the id is passed - never as no instance.)
                if (onGround && item.Pointer != IntPtr.Zero)
                    Game.CallBuiltinTrusted("instance_destroy", item, item);
                else if (onGround)
                    Game.CallBuiltinTrusted("instance_destroy", default, default, item.Id);
                else if (item.Pointer != IntPtr.Zero)
                    Game.CallScript("scr_item_destroy", item);
                else
                    Game.CallScript("scr_item_destroy", default, item.Id);
                removed.Add(name);
            }
            catch (Exception e) { Game.Log($"couldn't remove an item{(onGround ? " on the ground" : "")}: {e.Message}"); }
        }
        foreach (var slot in ToRemove)
            Remove(slot, false);
        foreach (var loot in LootToRemove)
            Remove(loot, true);
        foreach (int id in IdsToRemove)
            Remove(Instance.FromId(id), false);
        foreach (int id in LootIdsToRemove)
            Remove(Instance.FromId(id), true);
        ToRemove.Clear();
        LootToRemove.Clear();
        IdsToRemove.Clear();
        LootIdsToRemove.Clear();
        Removed.Clear();
        if (removed.Count > 0)
            Game.Log($"Removed {removed.Count} item(s) of mods that aren't loaded: {string.Join(", ", removed.GroupBy(n => n).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key))}");
    }

    private static string? IdName(Instance instance)
    {
        GmValue data = instance.Get("data");
        if (data.Kind != GmKind.Real)
            return null;
        GmValue name = Game.CallBuiltinTrusted("ds_map_find_value", default, default, data, "idName");
        return name.Kind == GmKind.String ? name.AsString : null;
    }

    // The game's item tables (stats, sprites): loaded once at start, in o_textLoader's step 33. Its names, in
    // step 4: again whenever the language changes (the names map is cleared then).
    // (Through Game.Global - the bridge's own lookup, undefined when missing. variable_global_exists called as
    // a built-in crashes the game on this runtime.)
    private static bool TablesLoaded => Game.Global["weapons_asset_data"].Kind == GmKind.Real;
    private static bool NamesLoaded => Game.Global["weapon_name"].Kind == GmKind.Real;

    // Whether the mods have loaded at start (the loader's): until then the items in the record can't be told
    // apart - a mod still loading from one that's gone - so nothing is defined.
    internal static Func<bool> ModsLoaded = () => true;

    internal static void Install(ModContext loader)
    {
        // Mod items' female and per-character worn pictures, as they're put on.
        loader.OnScript("scr_itemCharSpritesInit", call =>
        {
            PickWorn(call.Self);
            return false;
        });
        // Items added before the tables were loaded (mods load before the game runs): defined once they are.
        loader.Frame += () =>
        {
            if (_pending && TablesLoaded && ModsLoaded())
                DefinePending();
            if (ToRemove.Count > 0 || LootToRemove.Count > 0 || IdsToRemove.Count > 0 || LootIdsToRemove.Count > 0)
                RemoveOrphans();
        };
        // (Mods with items, or items in the record: the tables need defining even with nothing added this run.)
        _pending |= KnownItems.Count > 0;
        Events.o_textLoader.Other_25.After(loader, textLoader =>
        {
            int step = textLoader.Instance.Get("number").AsInt;
            if (step == 33 && ModsLoaded())
                DefinePending();
            else if (step == 4)
                foreach (var entry in Added.Where(e => e.Defined))
                    SetNames(entry);
        });

        // Its items in the game: made (user event 0: a new one), set up (user event 2: loaded from a save, from
        // a chest...), placed (user event 11: maybe put on or taken off), every turn while on (user event 6), gone.
        Events.o_inv_slot.Other_10.After(loader, slot => Look(slot.Instance, created: slot.Instance.Get("is_new").AsBool));
        Events.o_inv_slot.Other_12.After(loader, slot => Look(slot.Instance));
        Events.o_inv_slot.Other_21.After(loader, slot => Look(slot.Instance));
        Events.o_inv_slot.Other_16.After(loader, slot =>
        {
            var item = Look(slot.Instance);
            if (item != null && item.IsEquipped)
                Run(item, "OnEquippedTurn", t => t.OnEquippedTurn(item));
        });
        Events.o_inv_slot.CleanUp_0.Before(loader, slot =>
        {
            Seen.Remove(slot.Instance.Pointer);
            ToRemove.Remove(slot.Instance);
            return false;
        });

        // Weapons and armour on the ground (set up in user event 1, dropped or loaded): those of mods that aren't
        // loaded go too.
        Events.o_weapon_loot.Other_11.After(loader, loot =>
        {
            if (Orphans.Count > 0 && IdName(loot.Instance) is string name && Orphans.Contains(name) && LootToRemove.Add(loot.Instance))
                Removed.Add(name);
        });
        // Attacks (the loader's own script hooks - the patcher's LoaderHooks): the player's with a mod's weapon in
        // hand, and those on the player wearing a mod's armour, reported once the game's own code has run.
        HookAttack(loader, Scripts.scr_attack_result_hit, AttackResult.Hit);
        HookAttack(loader, Scripts.scr_attack_result_block, AttackResult.Block);
        HookAttack(loader, Scripts.scr_attack_result_dodge, AttackResult.Dodge);
        HookAttack(loader, Scripts.scr_attack_result_fumble, AttackResult.Fumble);

        Events.o_loot.CleanUp_0.Before(loader, loot =>
        {
            LootToRemove.Remove(loot.Instance);
            return false;
        });
    }

    // A slot the game just worked on: one of the mods' items? Tracked, and put on / taken off noticed.
    private static Item? Look(Instance slot, bool created = false)
    {
        if ((Added.Count == 0 && Orphans.Count == 0) || slot.Pointer == IntPtr.Zero)
            return null;
        if (!Seen.TryGetValue(slot.Pointer, out var seen) || seen.Item.Instance.Id != slot.Id)
        {
            if (IdName(slot) is not string name)
                return null;
            if (Orphans.Contains(name))
            {
                if (ToRemove.Add(slot))
                    Removed.Add(name);
                return null;
            }
            if (!ByName.TryGetValue(name, out var type))
                return null;
            var item = new Item(slot, type, name);
            seen = (item, false);
            Seen[slot.Pointer] = seen;
            if (created)
                Run(item, "OnCreated", t => t.OnCreated(item));
        }
        bool equipped = slot.Get("equipped").AsBool;
        if (equipped != seen.Equipped)
        {
            Seen[slot.Pointer] = (seen.Item, equipped);
            if (equipped)
                Run(seen.Item, "OnEquip", t => t.OnEquip(seen.Item));
            else
                Run(seen.Item, "OnUnequip", t => t.OnUnequip(seen.Item));
        }
        return seen.Item;
    }

    // scr_attack_result_*(target, ...) runs as the attacker, deals the damage and returns it (hit: argument 6 is
    // whether it's a crit). Only when the player and a mod's item are in it is the call taken over: the game's
    // own version run, then the items told.
    private static void HookAttack(ModContext loader, Script script, AttackResult result)
    {
        loader.OnScript(script.Name, call =>
        {
            if (Seen.Count == 0 || call.Args.Length == 0)
                return false;
            Instance attacker = call.Self, target = call.Args[0];
            bool byPlayer = IsPlayer(attacker), onPlayer = IsPlayer(target);
            if (!byPlayer && !onPlayer)
                return false;
            // (Dual wielding: only the hand attacking has its item "equipped" during the blow.)
            var weapons = byPlayer ? Equipped<Weapon>() : new();
            var armour = onPlayer ? Equipped<Armor>() : new();
            if (weapons.Count == 0 && armour.Count == 0)
                return false;
            GmValue damage = script.CallOriginal(call);
            call.Result = damage;
            var outcome = result == AttackResult.Hit && call.Args.Length > 6 && call.Args[6].AsBool ? AttackResult.Crit : result;
            var attack = new Attack(attacker, target, outcome, damage.AsReal, attacker.Get("shoot_attack").AsBool, byPlayer, onPlayer);
            foreach (var item in weapons)
            {
                Run(item, "OnAttack", t => ((Weapon)t).OnAttack(item, attack));
                if (attack.IsHit)
                    Run(item, "OnHit", t => ((Weapon)t).OnHit(item, attack));
            }
            foreach (var item in armour)
            {
                Run(item, "OnAttacked", t => ((Armor)t).OnAttacked(item, attack));
                if (attack.IsHit)
                    Run(item, "OnHitTaken", t => ((Armor)t).OnHitTaken(item, attack));
            }
            return true;
        });
    }

    // As the game's is_player: o_player or one of its children (each character is its own, o_runaway_wizzard...).
    private static bool IsPlayer(Instance instance)
    {
        if (instance.IsNone)
            return false;
        GmValue index = instance.Get("object_index");
        return index.Kind == GmKind.Real && (index.AsInt == (int)GameObject.o_player
            || Game.CallBuiltinTrusted("object_is_ancestor", default, default, index, (int)GameObject.o_player).AsBool);
    }

    private static List<Item> Equipped<T>() where T : ModItem
        => Seen.Values.Select(s => s.Item).Where(item => item.Type is T && item.IsEquipped).ToList();

    private static string ModName(ModItem type) => Added.First(a => a.Item == type).Context.Name;
    private static string ModOwner(ModItem type) => Added.First(a => a.Item == type).Context.Id;

    // The mod a weapon or armour in the game is from, for its tooltip (ModNameTooltip) - null if it isn't a mod's,
    // or its ShowModName is off.
    internal static string? ModNameOf(Instance item)
        => Added.Count > 0 && IdName(item) is string name && ByName.TryGetValue(name, out var type) && type.ShowModName ? ModName(type) : null;

    private static void Run(Item item, string what, Action<ModItem> action)
    {
        var type = item.Type!;
        Hooks.Invoke(ModOwner(type), type.Id + "." + what, () => { action(type); return false; });
    }

    private static void DefinePending()
    {
        _pending = false;
        foreach (var entry in Added)
            Define(entry);
        Remember();
        DefineOrphans();
    }

    private static void Define(Entry entry)
    {
        if (entry.Defined)
            return;
        entry.Defined = true;
        var item = entry.Item;
        try
        {
            // (Ours from earlier this run - its mod switched off and on - or a stand-in for it: defined anew.)
            // (Both checks always run: switched back on, it must stop being removed whatever else is true.)
            bool wasOrphan = Orphans.Remove(item.GameKey);
            bool ours = DefinedThisRun.Contains(item.GameKey) || wasOrphan;
            if (!ours && Game.CallBuiltinTrusted("ds_map_exists", default, default, Game.Global["weapons_stat"], item.GameKey).AsBool)
            {
                entry.Context.Log($"item \"{item.Id}\": the game already has an item called that");
                return;
            }
            bool toLoot = item.InRandomLoot && InLoot.Add(item.GameKey);
            GmValue assets = Game.CallScript("scr_stonemod_item_define", default, item.IsArmor, item.GameKey, item.BasedOn, item.ColumnsText, toLoot);
            if (assets.Kind != GmKind.Real || assets.AsReal < 0)
            {
                entry.Context.Log($"item \"{item.Id}\": the game has no {(item.IsArmor ? "armour" : "weapon")} called \"{item.BasedOn}\" to base it on");
                return;
            }
            int inventory = -1;
            if (item.InventorySprite != null)
            {
                inventory = entry.Context.LoadSprite(item.InventorySprite, Math.Max(1, item.InventoryFrames));
                if (inventory >= 0)
                    SetAsset(assets, "inv_sprite", inventory);
                else
                    entry.Context.Log($"item \"{item.Id}\": no picture Assets\\{item.InventorySprite}");
            }
            // In its equipment slot: its own, or the inventory picture.
            if (item.EquippedSprite != null)
                ReplaceSprite(entry, assets, "equipped_sprite", item.EquippedSprite);
            else if (inventory >= 0)
                SetAsset(assets, "equipped_sprite", inventory);
            DefineWorn(entry, assets);
            if (item.CorpseSprite != null)
                ReplaceSprite(entry, assets, "corpse_sprite", item.CorpseSprite);
            if (item.LootSprite != null)
            {
                int sprite = entry.Context.LoadSprite(item.LootSprite);
                if (sprite >= 0)
                {
                    Game.CallBuiltinTrusted("sprite_set_offset", default, default, sprite, Draw.SpriteWidth(sprite) / 2, Draw.SpriteHeight(sprite) / 2);
                    SetAsset(assets, "loot_sprite", sprite);
                }
                else
                    entry.Context.Log($"item \"{item.Id}\": no picture Assets\\{item.LootSprite}");
            }
            if (NamesLoaded)
                SetNames(entry);
            DefinedThisRun.Add(item.GameKey);
            entry.Context.Log($"item \"{item.Id}\" added (based on {item.BasedOn})");
        }
        catch (Exception e)
        {
            entry.Context.Log($"item \"{item.Id}\" couldn't be added: {e.Message}");
        }
    }

    // ---- worn pictures ----

    // A mod item's worn pictures for some wearers - female, or (helmets) a character - picked as it's put on.
    private sealed class WornVariants
    {
        public int Female = -1, UpperFemale = -1;
        public readonly Dictionary<string, int> ByCharacter = new(StringComparer.OrdinalIgnoreCase);
    }
    private static readonly Dictionary<string, WornVariants> Worn = new();

    // Its worn pictures (in place of the game item's: as many frames, lined up the same) and their variants.
    private static void DefineWorn(Entry entry, GmValue assets)
    {
        var item = entry.Item;
        int baseWorn = AssetSprite(assets, "char_sprite"), baseUpper = AssetSprite(assets, "char_upper_sprite");
        if (item.WornSprite != null)
            ReplaceSprite(entry, assets, "char_sprite", item.WornSprite);
        if (item.WornUpperSprite != null)
            ReplaceSprite(entry, assets, "char_upper_sprite", item.WornUpperSprite);
        var variants = new WornVariants
        {
            Female = item.WornSpriteFemale == null ? -1 : LoadLike(entry, item.WornSpriteFemale, baseWorn),
            UpperFemale = item.WornUpperSpriteFemale == null ? -1 : LoadLike(entry, item.WornUpperSpriteFemale, baseUpper),
        };
        foreach (var (character, file) in item.WornByCharacter)
        {
            int sprite = LoadLike(entry, file, baseWorn);
            if (sprite >= 0)
                variants.ByCharacter[character] = sprite;
        }
        if (variants.Female >= 0 || variants.UpperFemale >= 0 || variants.ByCharacter.Count > 0)
            Worn[item.GameKey] = variants;
        else
            Worn.Remove(item.GameKey);
    }

    // An item's worn pictures being picked (o_inv_slot's scr_itemCharSpritesInit, before the game's code): a mod
    // item's for this wearer, if it has one. The game's code keeps what it's given (and then looks its
    // variants up by sprite name, which a mod's sprites don't have).
    private static void PickWorn(Instance self)
    {
        if (Worn.Count == 0 || self.Get("idName") is not { Kind: GmKind.String } id || !Worn.TryGetValue(id.AsString, out var variants))
            return;
        int worn = -1, upper = -1;
        if (self.Get("slot").AsString == "Head")
            variants.ByCharacter.TryGetValue(Game.CallScript("scr_atr", self, "nameKey").AsString, out worn);
        else if (Game.CallScript("scr_atr", self, "sexKey").AsString == "Female")
        {
            worn = variants.Female;
            upper = variants.UpperFemale;
        }
        if (worn >= 0)
            self.Set("char_sprite", worn);
        if (upper >= 0)
            self.Set("char_upper_sprite", upper);
    }

    // A picture from the mod's Assets folder in place of the game item's (its asset map's key). -1 if there's none.
    private static int ReplaceSprite(Entry entry, GmValue assets, string key, string file)
    {
        int sprite = LoadLike(entry, file, AssetSprite(assets, key));
        if (sprite >= 0)
            Game.CallScript("scr_stonemod_item_sprite", default, assets, key, sprite);
        return sprite;
    }

    // A picture from the mod's Assets folder made like one of the game's (like): its frames, side by side in the PNG -
    // said if the size is off - and lined up as it. -1 if it can't be loaded.
    private static int LoadLike(Entry entry, string file, int like)
    {
        int frames = like >= 0 ? Math.Max(1, Game.CallBuiltinTrusted("sprite_get_number", default, default, like).AsInt) : 1;
        int sprite = entry.Context.LoadSprite(file, frames);
        if (sprite < 0)
        {
            entry.Context.Log($"item \"{entry.Item.Id}\": no picture Assets\\{file}");
            return -1;
        }
        if (like >= 0)
        {
            double width = Draw.SpriteWidth(sprite), height = Draw.SpriteHeight(sprite);
            double likeWidth = Draw.SpriteWidth(like), likeHeight = Draw.SpriteHeight(like);
            if (width != likeWidth || height != likeHeight)
                entry.Context.Log($"item \"{entry.Item.Id}\": {file} has {frames} frame(s) of {width}x{height}; the game item's picture has {frames} of {likeWidth}x{likeHeight} - it should be {likeWidth * frames}x{likeHeight}");
            Game.CallScript("scr_stonemod_item_sprite_like", default, sprite, like);
        }
        return sprite;
    }

    // A sprite in an item's asset map (-1 for none: the game's is -4).
    private static int AssetSprite(GmValue assets, string key)
    {
        GmValue value = Game.CallBuiltinTrusted("ds_map_find_value", default, default, assets, key);
        int sprite = value.Kind == GmKind.Real ? value.AsInt : -1;
        return sprite >= 0 && Game.CallBuiltinTrusted("sprite_exists", default, default, sprite).AsBool ? sprite : -1;
    }

    private static void SetAsset(GmValue assets, string key, int sprite)
        => Game.CallBuiltinTrusted("ds_map_replace", default, default, assets, key, sprite);

    private static void SetNames(Entry entry)
    {
        var item = entry.Item;
        GmValue names = Game.Global["weapon_name"], descriptions = Game.Global["weapon_desc"];
        Game.CallBuiltinTrusted("ds_map_set", default, default, names, item.GameKey, item.DisplayName ?? item.Key);
        GmValue description = item.Description != null
            ? item.Description
            : Game.CallBuiltinTrusted("ds_map_find_value", default, default, descriptions, item.BasedOn);
        if (!description.IsUndefined)
            Game.CallBuiltinTrusted("ds_map_set", default, default, descriptions, item.GameKey, description);
    }

    // Names and values go into the game's tables as text, split on ";" (and our "|"): those can't be in them.
    internal static void CheckText(string text, string what)
    {
        if (text.IndexOfAny(new[] { ';', '|', '\n' }) >= 0)
            throw new ArgumentException($"\"{text}\" can't have ';', '|' or a line break in it", what);
    }
}
