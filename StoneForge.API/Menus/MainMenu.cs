using StoneForge.Objects;

namespace StoneForge;

/// <summary>Buttons in the main menu's list (above Exit, the game's own button, fading in with the rest).</summary>
public static class MainMenu
{
    // (A switched-off mod's buttons stay in the list, Removed - buttons on screen hold their index in it.)
    internal sealed record Button(string Mod, string Text, Action OnClick)
    {
        public bool Removed;
    }
    internal static readonly List<Button> Buttons = new();

    /// <summary>Adds a button to the main menu, above Exit; <paramref name="onClick"/> runs when it's clicked.</summary>
    public static void AddButton(ModContext context, string text, Action onClick)
    {
        Buttons.Add(new Button(context.Id, text, onClick));
        Changed();
    }

    // A mod switched off: its buttons go.
    internal static void RemoveMod(string mod)
    {
        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i].Mod != mod || Buttons[i].Removed)
                continue;
            // (Without its click handler: that's the mod's code, which has to be let go to unload.)
            Buttons[i] = new Button(mod, Buttons[i].Text, NoClick) { Removed = true };
            Changed();
        }
    }

    private static void NoClick() { }

    // The main menu's list is made again when a window closes (UIWindow checks this) - if there's a list already:
    // one made later has the buttons anyway (mods add theirs as they load, before the main menu is up).
    private static void Changed()
    {
        if (Game.Running && Gm.InstanceExists(GameObject.o_mainMenuNavContainer))
            Game.Global["stonemod_menu_dirty"] = true;
    }

    // Our buttons carry this variable: their index in Buttons.
    internal const string IndexVar = "stonemod_button";
    private static Instance _pressed;
    private static readonly List<Instance> Made = new();
    private static Instance _exit;

    internal static void Install(ModContext context)
    {
        Events.o_mainMenuNavContainer.Other_10.After(context, AddButtons);
        context.Frame += FollowExit;
        // Clicks: o_button's input event (user event 15, which o_mainMenuButton's runs first) calls
        // event_user(event) on release. Ours have no event; we note the release before and act after.
        Events.o_mainMenuButton.Other_25.Before(context, button =>
        {
            if (button.Instance.Get(IndexVar).Kind == GmKind.Real && button.Instance.Get("guiInteractiveState").AsInt == 4
                && button.Instance.Get("pressed").AsBool && button.Instance.Get("is_activate").AsBool)
                _pressed = button.Instance;
            return false;
        });
        Events.o_mainMenuButton.Other_25.After(context, button =>
        {
            if (_pressed.IsNone || !button.Instance.Equals(_pressed))
                return;
            _pressed = default;
            int index = button.Instance.Get(IndexVar).AsInt;
            if (index < 0 || index >= Buttons.Count || Buttons[index].Removed)
                return;
            Hooks.Invoke(Buttons[index].Mod, "menu " + Buttons[index].Text, () => { Buttons[index].OnClick(); return false; });
        });
    }

    // The main list was (re)built: ours go where Exit is, and Exit below them.
    private static void AddButtons(o_mainMenuNavContainer nav)
    {
        Made.Clear();
        int count = Buttons.Count(b => !b.Removed);
        if (count == 0)
            return;
        GameInstance? exit = Instances.All<GameInstance>(GameObject.o_mainMenuButton)
            .FirstOrDefault(b => b.ObjectIndex == (int)GameObject.o_mainMenuButton && b.Instance.Get("event").AsInt == 3);
        if (exit == null)
            return;
        double top = exit.Instance.Get("guiLayoutOffsetTop");
        double offset = nav.buttonsOffset;
        Scripts.scr_guiLayoutOffsetUpdate.Call(nav, exit, 0, top + offset * count);
        double alpha = exit.ImageAlpha;
        int row = 0;
        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i].Removed)
                continue;
            Instance button = Scripts.scr_guiCreateInteractive.Call(nav, nav.buttonsContainer, GmValue.From(GameObject.o_mainMenuButton), nav.Depth - 1, 0, top + offset * row++);
            if (button.IsNone)
            {
                Game.Log($"Main menu: couldn't make the button \"{Buttons[i].Text}\"");
                continue;
            }
            button.Set("text", Buttons[i].Text);
            // (No event: the click is ours - see Install.)
            button.Set("event", -4);
            button.Set(IndexVar, i);
            button.Set("image_alpha", alpha);
            Made.Add(button);
        }
        _exit = exit.Instance;
        nav.height = nav.logoHeight + nav.logoTextHeight + (double)nav.buttonsOffset * Game.CallBuiltin("variable_instance_get", nav.buttonsContainer, "guiChildrenCount").AsReal;
        // The first build runs inside the container's Create, before its size is set (the game checks for
        // buyGameButton, made at the end of Create, the same way); Create lays it out itself afterwards.
        if (Game.CallBuiltin("variable_instance_exists", nav.Instance, "buyGameButton").AsBool)
            Game.CallBuiltinAs("event_user", nav.Instance, nav.Instance, 15);
    }

    // While the list fades in, ours keep Exit's opacity.
    private static void FollowExit()
    {
        if (Made.Count == 0)
            return;
        if (_exit.IsNone || !_exit.Exists)
        {
            Made.Clear();
            return;
        }
        double alpha = _exit.Get("image_alpha");
        foreach (var button in Made)
            if (button.Exists)
                button.Set("image_alpha", alpha);
        if (alpha >= 1)
            Made.Clear();
    }
}
