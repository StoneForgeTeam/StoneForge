namespace StoneForge;

/// <summary>The native (YYC) build's objects of the loader's own (o_stonemod_gui, o_stonemod_hud, o_stonemod_modal): added
/// to the game data with no code - the native build can't add any - under a parent of the game's whose events they then
/// have (an object without an event of its own runs its parent's). Each of the parent's events is hooked: for one of ours,
/// the parent's code is skipped, and our handler for that event (if any) runs instead. A parent with few events and few
/// instances of its own is picked, so the hooks cost little.</summary>
internal static class NativeHost
{
    /// <summary>Hosts <paramref name="obj"/>'s events on <paramref name="parent"/>'s (<paramref name="events"/>: all of the
    /// parent's, as "Create_0", "Draw_64"...): <paramref name="handlers"/> run for ours, by event.</summary>
    internal static void Install(ModContext loader, string obj, string parent, string[] events, IReadOnlyDictionary<string, Action<Instance>> handlers)
    {
        int index = -2;
        foreach (string ev in events)
        {
            handlers.TryGetValue(ev, out var handler);
            loader.OnCode($"gml_Object_{parent}_{ev}", before: (self, _) =>
            {
                if (index == -2)
                    index = Gm.AssetGetIndex(obj);
                if (self.IsNone || index < 0 || self.Get("object_index").AsInt != index)
                    return false;
                handler?.Invoke(self);
                return true;
            });
        }
    }
}
