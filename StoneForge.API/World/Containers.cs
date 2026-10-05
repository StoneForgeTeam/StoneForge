using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>Containers - in the world (c_container's kinds: chests, barrels, tombs...), and the bags the player has (a
/// backpack, a casket, a quiver: items). A container holds its items three ways, as the game keeps them:
/// - Never opened (a container in the world): nothing yet - its loot is rolled as it's first opened, from a seed of its
///   place in the world (<see cref="HasBeenOpened"/>). Items a mod puts in one (<see cref="AddItem"/>) wait in it,
///   and join its loot as it's first opened; it stays unopened till then.
/// - Open: a window (<see cref="OpenContainer"/>) whose items are ordinary item slots, as the player's inventory's are
///   (<see cref="Open"/>, <see cref="OnOpened"/>, <see cref="OnItemAdded"/>, <see cref="OnItemRemoved"/>).
/// - Closed: saved in it - a world container's loot_list, a bag's data's lootList - one entry an item, in the game's own
///   save format (<see cref="ContentsJson"/>, <see cref="SetContents"/>, <see cref="OnClosed"/>).
/// Game thread only, in a game.</summary>
/// <example><code>
/// Containers.OnClosed(context, chest => context.Log($"Closed: {Containers.ContentsJson(chest)}"));
/// </code></example>
public static class Containers
{
    /// <summary>The containers open now (their windows).</summary>
    public static IReadOnlyList<OpenContainer> Open()
        => Instances.All(GameObjectId.o_container_parent).Select(window => new OpenContainer(window)).ToArray();

    /// <summary>Whether a container is open now (a window of its is).</summary>
    public static bool IsOpen(Instance container) => !container.IsNone && Open().Any(open => open.Container.Equals(container));

    /// <summary>Whether a container in the world has been opened - its loot rolled, then kept in it. One that hasn't
    /// rolls its loot as it's first opened (unless <see cref="SetContents"/> gave it some). A bag: always.</summary>
    public static bool HasBeenOpened(Instance container) => !IsWorldContainer(container) || container.Get("is_execute").AsBool;

    /// <summary>A closed container's items, as JSON - an array, one entry an item in the game's own save format (its
    /// name, its data, where it lay, its look, charge, stack, whether it was on...); for one never opened, the items
    /// waiting to join its loot (<see cref="AddItem"/>). Null if it's open (its window's slots are its items then:
    /// <see cref="OpenContainer.Items"/>) or it isn't a container.</summary>
    public static string? ContentsJson(Instance container)
    {
        if (IsOpen(container) || Saved(container) is not { } list)
            return null;
        var items = new JsonArray();
        // (Each item is a list of its own, held in the container's by id - not nested: read one by one.)
        for (int i = 0; i < list.Count; i++)
            items.Add(DsList.From(list[i]) is { Exists: true } item ? item.ToJsonNode() : null);
        return items.ToJsonString();
    }

    /// <summary>Makes a closed container's items these (<see cref="ContentsJson"/>, perhaps another game's), as the
    /// game's own save of it does; a container in the world that was never opened counts as opened then - its loot is
    /// these, not rolled. False, with nothing changed, if it's open, isn't a container, or the JSON isn't items.</summary>
    public static bool SetContents(Instance container, string json)
    {
        if (IsOpen(container) || Saved(container) is not { } list)
            return false;
        if (JsonNode.Parse(json) is not JsonArray entries)
            return false;
        var made = new List<DsList>();
        foreach (JsonNode? entry in entries)
        {
            if (entry is not JsonArray || DsList.FromJson(entry.ToJsonString()) is not { } item)
            {
                made.ForEach(m => m.Destroy());
                return false;
            }
            made.Add(item);
        }
        // (As the game's save does: the list cleared, each item's own list added to it by id.)
        list.Clear();
        foreach (DsList item in made)
            list.Add(item.Id);
        if (IsWorldContainer(container))
            container["is_execute"] = true;
        return true;
    }

    /// <summary>Puts an item in a container (<see cref="Inventory.Add"/>'s names), open or closed: an open one's in its
    /// first free cell, a closed one's saved in it as the game's save does (in the first free cell as it opens). A
    /// container in the world never opened stays so: its loot is rolled as it's first opened, and the item joins it. False if
    /// there's no such item, it isn't a container, or an open one has no room (the game drops it on the ground then). With
    /// <paramref name="setup"/>, the item's own values set as it's made - for a closed one, before it's saved.</summary>
    public static bool AddItem(Instance container, string name, int stack = 1, ItemQuality quality = ItemQuality.Rolled, Action<InventoryItem>? setup = null)
    {
        if (Open().FirstOrDefault(open => open.Container.Equals(container)) is { Window.IsNone: false } open)
            return open.Add(name, stack, quality, setup) != null;
        if (Saved(container) is not { } list)
            return false;
        // (Made as the game makes one, in no cell; saved as its save does; gone again.)
        Instance slot = ItemSlots.Make(container, name, stack, quality, place: false, setup);
        if (slot.IsNone)
            return false;
        list.Add(ItemSlots.Save(slot));
        Game.CallScript("scr_item_destroy", default, slot, false);
        return true;
    }

    /// <summary>Puts a mod's weapon or armour in a container, open or closed - the one of type <typeparamref name="T"/>
    /// the mod added.</summary>
    public static bool AddItem<T>(Instance container, ItemQuality quality = ItemQuality.Rolled, Action<InventoryItem>? setup = null) where T : ModItem
        => AddItem(container, ItemSlots.NameOf(Items.Get<T>()), 1, quality, setup);

    /// <summary>Puts a mod's consumable in a container, open or closed (<paramref name="stack"/> of it, for one that
    /// stacks).</summary>
    public static bool AddItem(Instance container, Consumable consumable, int stack = 1, Action<InventoryItem>? setup = null)
        => AddItem(container, ItemSlots.NameOf(consumable), stack, ItemQuality.Rolled, setup);

    /// <summary>Takes up to <paramref name="count"/> of a mod's weapon or armour out of a container, open or closed - the
    /// one of type <typeparamref name="T"/>: how many were taken.</summary>
    public static int RemoveItem<T>(Instance container, int count = 1) where T : ModItem
        => RemoveItem(container, ItemSlots.NameOf(Items.Get<T>()), count);

    /// <summary>Takes up to <paramref name="count"/> of a mod's consumable out of a container, open or closed: how many
    /// were taken.</summary>
    public static int RemoveItem(Instance container, Consumable consumable, int count = 1)
        => RemoveItem(container, ItemSlots.NameOf(consumable), count);

    /// <summary>Takes up to <paramref name="count"/> of an item out of a container by name, open or closed - from its
    /// stacks, then whole: how many were taken.</summary>
    public static int RemoveItem(Instance container, string name, int count = 1)
    {
        if (Open().FirstOrDefault(open => open.Container.Equals(container)) is { Window.IsNone: false } open)
            return open.Remove(name, count);
        if (Saved(container) is not { } list)
            return 0;
        string key = ModIdentity.ToGameKey(name);
        int removed = 0;
        for (int i = 0; i < list.Count && removed < count; i++)
        {
            if (DsList.From(list[i]) is not { Exists: true } item || !ItemSlots.Matches(SavedName(item), key))
                continue;
            int stack = Math.Max(1, item[6].AsInt), take = Math.Min(stack, count - removed);
            removed += take;
            if (take < stack)
            {
                item[6] = stack - take;
                continue;
            }
            list.RemoveAt(i);
            item.Destroy();
            i--;
        }
        return removed;
    }

    // A saved item's name: its data's idName; else its object's name less "o_inv_".
    private static string SavedName(DsList item)
    {
        if (item.GetMap(1) is { } data && data["idName"] is { Kind: GmKind.String } idName)
            return idName.AsString;
        string obj = item[0].AsString ?? "";
        return obj.StartsWith("o_inv_", StringComparison.Ordinal) ? obj.Substring(6) : obj;
    }

    /// <summary>Runs as a container has been opened - on the next frame, its items in its window (a chest's first time:
    /// its loot rolled).</summary>
    public static void OnOpened(ModContext context, Action<OpenContainer> handler)
    {
        var opened = new List<Instance>();
        context.OnCode("gml_Object_o_container_parent_Create_0", after: (window, _) =>
        {
            if (!window.IsNone)
                opened.Add(window.Persist());
        });
        context.Frame += () =>
        {
            if (opened.Count == 0)
                return;
            var windows = opened.ToArray();
            opened.Clear();
            foreach (Instance window in windows)
                if (window.Exists)
                    handler(new OpenContainer(window));
        };
    }

    /// <summary>Runs as a container has been closed (its window's alarm 0): its items are saved in it now
    /// (<see cref="ContentsJson"/>) - the container.</summary>
    public static void OnClosed(ModContext context, Action<Instance> handler)
    {
        var closing = new Stack<Instance>();
        context.OnCode("gml_Object_o_container_parent_Alarm_0",
            before: (window, _) =>
            {
                closing.Push(window.IsNone ? default : Instance.Of(window.Get("parent")));
                return false;
            },
            after: (_, _) =>
            {
                if (closing.Count > 0 && closing.Pop() is { IsNone: false } container && container.Exists)
                    handler(container);
            });
    }

    /// <summary>Runs as an item comes into an open container - put in by the player - once a frame: the container, and
    /// the item. Not the items it opens with.</summary>
    public static void OnItemAdded(ModContext context, Action<OpenContainer, InventoryItem> handler)
        => Watch(context, (window, was, now) =>
        {
            foreach (var (slot, _) in now)
                if (!was.ContainsKey(slot))
                    handler(new OpenContainer(window), new InventoryItem(slot));
        });

    /// <summary>Runs as an item leaves an open container - taken, or used up - once a frame: the container, and the
    /// item. Not its items as it closes.</summary>
    public static void OnItemRemoved(ModContext context, Action<OpenContainer, InventoryItem> handler)
        => Watch(context, (window, was, now) =>
        {
            foreach (var (slot, _) in was)
                if (!now.ContainsKey(slot))
                    handler(new OpenContainer(window), new InventoryItem(slot));
        });

    // The loader's: items put in a container never opened join its loot as it's first opened. The game makes its window
    // in the open event's roll branch, before it counts the container opened (is_execute); the loot rolled into it, the
    // items waiting in its list (which the game leaves alone till it's opened) go in, each in its first free cell, and the
    // list's left empty (its window saves everything into it as it closes). Kept in the container itself, not here:
    // "never opened, with items" is only ever a mod's doing, and it lasts through leaving the place and saving.
    internal static void Install(ModContext loader)
    {
        // (The container is the window's creator - its Create runs as the container makes it - since the window's parent
        // is only set after: scr_container_create.)
        var waiting = new List<(Instance Window, Instance Container)>();
        loader.OnCode("gml_Object_o_container_parent_Create_0", after: (window, creator) =>
        {
            if (window.IsNone || creator.IsNone || !creator.Exists || !IsWorldContainer(creator)
                || creator.Get("is_execute").AsBool || Saved(creator) is not { Count: > 0 })
                return;
            waiting.Add((window.Persist(), creator.Persist()));
        });
        loader.Frame += () =>
        {
            if (waiting.Count == 0)
                return;
            var opened = waiting.ToArray();
            waiting.Clear();
            foreach (var (window, container) in opened)
            {
                if (!window.Exists || Saved(container) is not { } list)
                    continue;
                Game.CallScript("scr_loadContainerContent", default, list.Id, window.Get("object_index"), -4, window, false, false);
                list.Clear();
            }
        };
    }

    private static void Watch(ModContext context, Action<Instance, Dictionary<Instance, bool>, Dictionary<Instance, bool>> changed)
        => new ItemSlotWatch(context, () => Instances.All(GameObjectId.o_container_parent), changed);

    // A container in the world (its own loot_list), rather than a bag (an item: its data's lootList).
    private static bool IsWorldContainer(Instance container) => container.Get("loot_list").Kind == GmKind.Real;

    // A closed container's saved items: a world container's loot_list, a bag's data's lootList.
    private static DsList? Saved(Instance container)
    {
        if (container.IsNone || !container.Exists)
            return null;
        if (IsWorldContainer(container))
            return DsList.From(container.Get("loot_list"));
        return container.Get("data").AsDsMap is { } data ? DsList.From(data["lootList"]) : null;
    }
}
