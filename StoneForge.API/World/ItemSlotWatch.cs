namespace StoneForge;

// Item slots by owner, compared once a frame (Inventory's and Containers' item events): each owner's slots, and whether
// each is on. An owner seen for the first time starts afresh (its items as they are: not news) - a game loaded, a
// container opened; one gone tells nothing (a container closing isn't its items leaving it). One per registration, on
// the mod's frame handler.
internal sealed class ItemSlotWatch
{
    private Dictionary<Instance, Dictionary<Instance, bool>> _was = new();

    public ItemSlotWatch(ModContext context, Func<IReadOnlyList<Instance>> owners,
        Action<Instance, Dictionary<Instance, bool>, Dictionary<Instance, bool>> changed)
    {
        context.Frame += () =>
        {
            var now = new Dictionary<Instance, Dictionary<Instance, bool>>();
            foreach (Instance owner in owners())
                now[owner] = new();
            if (now.Count > 0)
                foreach (Instance slot in Instances.All(GameObjectId.o_inv_slot))
                    if (Instance.Of(slot.Get("owner")) is { IsNone: false } owner && now.TryGetValue(owner, out var items))
                        items[slot] = slot.Get("equipped").AsBool;
            foreach (var (owner, items) in now)
                if (_was.TryGetValue(owner, out var was))
                    changed(owner, was, items);
            _was = now;
        };
    }
}
