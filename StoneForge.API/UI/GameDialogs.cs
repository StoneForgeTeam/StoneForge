namespace StoneForge;

/// <summary>The game's own confirmation panel (its Exit's: the question, Yes and No, the screen behind dimmed), asking a
/// mod's question: <paramref name="onYes"/> runs if it's answered Yes, <paramref name="onNo"/> (if any) when it's closed
/// otherwise.
/// <code>
/// GameDialogs.Confirm(context, "Leave the game?", () => Rooms.ToMainMenu());
/// </code></summary>
public static class GameDialogs
{
    private sealed record Open(string Mod, Instance Panel, Action OnYes, Action? OnNo);

    private static readonly List<Open> Panels = new();
    private static int _panelObject = -2;

    /// <summary>Asks <paramref name="text"/> in the game's confirmation panel. False if it couldn't be shown.</summary>
    public static bool Confirm(ModContext context, string text, Action onYes, Action? onNo = null)
    {
        if (_panelObject == -2)
            _panelObject = Gm.AssetGetIndex("o_exit_confirm_panel");
        if (_panelObject < 0)
            return false;
        Instance panel = Instance.Of(Game.CallScript("scr_guiCreateContainer", default, Game.Global["guiBaseContainerVisible"], _panelObject));
        if (panel.IsNone)
            return false;
        // (Its Yes calls its owner's user event: itself here, the one that closes it - and ours runs first, Install.)
        panel["owner"] = panel;
        panel["event"] = 1;
        panel["text"] = text;
        Panels.Add(new Open(context.Id, panel, onYes, onNo));
        return true;
    }

    // The loader: Yes on one of ours runs its action (o_exit_confirm_panel's user event 0) and closes it; one closed
    // without it (No, Escape) runs its OnNo.
    internal static void Install(ModContext context)
    {
        context.OnCode("gml_Object_o_exit_confirm_panel_Other_10", before: (panel, _) =>
        {
            int at = Panels.FindIndex(open => open.Panel.Equals(panel.Persist()));
            if (at < 0)
                return false;
            var open = Panels[at];
            Panels.RemoveAt(at);
            Hooks.Invoke(open.Mod, "dialog Yes", () => { open.OnYes(); return false; });
            if (panel.Exists)
                Game.CallBuiltinAs("event_user", panel, panel, 1);
            return true;
        });
        context.Frame += () =>
        {
            for (int i = Panels.Count - 1; i >= 0; i--)
            {
                if (Panels[i].Panel.Exists)
                    continue;
                var closed = Panels[i];
                Panels.RemoveAt(i);
                if (closed.OnNo is { } onNo)
                    Hooks.Invoke(closed.Mod, "dialog No", () => { onNo(); return false; });
            }
        };
    }

    // A mod switched off: its questions' answers aren't its to run any more.
    internal static void RemoveMod(string mod) => Panels.RemoveAll(open => open.Mod == mod);
}
