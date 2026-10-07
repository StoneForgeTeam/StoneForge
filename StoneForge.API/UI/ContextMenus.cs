namespace StoneForge;

/// <summary>The game's right-click menus (o_gui_context: what a click on a unit, an object, an item... offers - Talk,
/// Attack, Explore...). A mod can change any menu as it opens - add its own options, remove the game's, rename or grey
/// them out (<see cref="OnOpen"/>) - or add an option to the menus of the instances it picks (<see cref="Add"/>). A mod's
/// option runs its C# when clicked, then the menu closes as for the game's own. Game thread only.</summary>
/// <example><code>
/// ContextMenus.Add(context, "Follow", target => target.Get("object_index").AsInt == myObject, target => Follow(target));
/// ContextMenus.OnOpen(context, menu => menu.Remove("Explore"));
/// </code></example>
public static class ContextMenus
{
    // (The game's: the menu, and its buttons.)
    private static int _menuObject = -2;

    /// <summary>Runs as any right-click menu opens, with its options as the game made them: change them with the
    /// <see cref="ContextMenu"/>'s methods.</summary>
    public static void OnOpen(ModContext context, Action<ContextMenu> handler)
    {
        // (A menu this call made - not one already open, which the game keeps.)
        bool wasOpen = false;
        var actions = new Dictionary<string, Action<Instance>>();
        context.OnScript("scr_create_context_menu",
            before: _ =>
            {
                wasOpen = Open() is { IsNone: false };
                return false;
            },
            after: call =>
            {
                if (wasOpen || Open() is not { IsNone: false } menu || menu.Get("context_name").AsDsList is not { } names
                    || menu.Get("context_desc").AsDsList is not { } descs)
                    return;
                var opened = new ContextMenu(menu, call.Self.Persist(), names, descs, context.Id, actions);
                handler(opened);
                opened.Finish();
            });
        // A click on one of this mod's options: its action, on what the menu's for (the game then closes the menu).
        context.OnCode("gml_Object_o_context_button_Mouse_4", before: (button, _) =>
        {
            if (button.Get("func") is { Kind: GmKind.String } func && actions.TryGetValue(func.AsString, out var action)
                && button.Get("is_activate").AsBool && button.Get("interact_id") is var target && InstanceOf(target) is { IsNone: false } on && on.Exists)
                action(on);
            return false;
        });
    }

    /// <summary>Adds an option to the menus of the instances <paramref name="appliesTo"/> picks (asked as each menu
    /// opens, with what it's for): <paramref name="text"/>, running <paramref name="onClick"/> on that instance when
    /// clicked. With <paramref name="hover"/>, the game's hint shows on it.</summary>
    public static void Add(ModContext context, string text, Func<Instance, bool> appliesTo, Action<Instance> onClick, string? hover = null)
        => OnOpen(context, menu =>
        {
            if (appliesTo(menu.Target))
                menu.Add(text, onClick, hover: hover);
        });

    // The open menu (none: there's none).
    private static Instance Open()
    {
        if (_menuObject == -2)
            _menuObject = Gm.AssetGetIndex("o_gui_context");
        return _menuObject < 0 ? default : InstanceOf(Game.CallBuiltin("instance_find", _menuObject, 0));
    }

    internal static Instance InstanceOf(GmValue value) => value.Kind switch
    {
        GmKind.Instance => value.AsInstance.Persist(),
        GmKind.Real when value.AsInt >= 0 => Instance.FromId(value.AsInt),
        _ => default,
    };
}
