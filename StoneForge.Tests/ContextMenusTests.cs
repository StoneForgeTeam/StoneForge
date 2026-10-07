using StoneForge;

// Right-click menus (ContextMenus, ContextMenu): a menu changed as the game opens it - options added, removed, renamed,
// greyed out, the menu sized again or closed when none's left - and a mod's option run when clicked, on what the menu's
// for (laid out with FakeGame's room, ds lists and scripts).
public class ContextMenusTests : FakeGame
{
    private const int MenuObject = 300, ButtonObject = 301, Wolf = 302, WolfId = 100_050, MenuId = 100_060, ButtonId = 100_070;
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    private readonly FakeWorld _world = new();
    private readonly ModContext _context = new("menus_test");

    public ContextMenusTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        World = _world;
        _world.ExistsByObject = true;
        _world.LendsIds = true;
        _world.Assets["o_gui_context"] = MenuObject;
        _world.Add(WolfId, Wolf);
        _world.Vars[WolfId] = new();
        // (As the game's: a menu for what was clicked, with the options it asks for that apply - each a key and its text,
        // and whether it's on and its hint.)
        _scripts.Add("scr_create_context_menu", a =>
        {
            var names = DsList.Create();
            var descs = DsList.Create();
            foreach (GmValue option in a)
            {
                names.Add(option);
                names.Add(option.AsString.ToUpperInvariant());
                descs.Add(1);
                descs.Add(0);
            }
            _world.Add(MenuId, MenuObject);
            _world.Vars[MenuId] = new() { ["context_name"] = names.Id, ["context_desc"] = descs.Id, ["width"] = 44, ["interact_id"] = WolfId };
            return GmValue.Undefined;
        });
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    // The wolf right-clicked: the game's menu for it ("Attack", "Explore").
    private ContextMenuItem[] Open()
    {
        Game.CallScript("scr_create_context_menu", Instance.FromId(WolfId), "Attack", "Explore");
        return Items();
    }

    private ContextMenuItem[] Items()
    {
        var names = _world.Vars[MenuId]["context_name"].AsDsList!.Value;
        var descs = _world.Vars[MenuId]["context_desc"].AsDsList!.Value;
        return Enumerable.Range(0, names.Count / 2)
            .Select(i => new ContextMenuItem(names[i * 2].AsString, names[i * 2 + 1].AsString, descs[i * 2].AsBool,
                descs[i * 2 + 1] is { Kind: GmKind.String } h ? h.AsString : ""))
            .ToArray();
    }

    // A click on the menu's button for this option (o_context_button's left press).
    private void Click(string key, bool enabled = true)
    {
        _world.Add(ButtonId, ButtonObject);
        _world.Vars[ButtonId] = new() { ["func"] = key, ["is_activate"] = enabled, ["interact_id"] = WolfId };
        RunBefore("gml_Object_o_context_button_Mouse_4", ButtonId);
    }

    [Fact]
    public void A_mods_option_is_added_to_the_menus_it_picks_and_runs_on_what_was_clicked()
    {
        var clicked = new List<Instance>();
        ContextMenus.Add(_context, "Follow", target => target.Equals(Instance.FromId(WolfId)), clicked.Add, hover: "Walk after them");
        var items = Open();
        Assert.Equal(new[] { "Attack", "Explore", "stoneforge:menus_test:Follow" }, items.Select(i => i.Key));
        Assert.Equal(("Follow", true, "Walk after them"), (items[2].Text, items[2].Enabled, items[2].Hover));

        Click(items[2].Key);
        Assert.Equal(new[] { Instance.FromId(WolfId) }, clicked);
        // (Greyed out: nothing; the game's own options: the game's.)
        Click(items[2].Key, enabled: false);
        Click("Attack");
        Assert.Single(clicked);
    }

    [Fact]
    public void A_menu_for_something_else_is_left_alone()
    {
        ContextMenus.Add(_context, "Follow", _ => false, _ => { });
        Assert.Equal(new[] { "Attack", "Explore" }, Open().Select(i => i.Key));
    }

    [Fact]
    public void The_games_options_are_removed_renamed_greyed_out_and_added_among()
    {
        ContextMenus.OnOpen(_context, menu =>
        {
            Assert.Equal(Instance.FromId(WolfId), menu.Target);
            Assert.True(menu.Has("Attack"));
            Assert.True(menu.Remove("Attack"));
            Assert.False(menu.Remove("Attack"));
            Assert.True(menu.SetText("Explore", "Look at it, a very long way of saying so"));
            Assert.True(menu.SetEnabled("Explore", false));
            menu.Add("Pet", _ => { }, index: 0);
        });
        var items = Open();
        Assert.Equal(new[] { "stoneforge:menus_test:Pet", "Explore" }, items.Select(i => i.Key));
        Assert.False(items[1].Enabled);
        Assert.Equal("Look at it, a very long way of saying so", items[1].Text);
    }

    [Fact]
    public void A_menu_left_with_nothing_is_closed()
    {
        ContextMenus.OnOpen(_context, menu =>
        {
            menu.Remove("Attack");
            menu.Remove("Explore");
        });
        Open();
        Assert.Contains(MenuId, _world.Destroyed);
    }

    [Fact]
    public void A_menu_already_open_isnt_changed()
    {
        int opened = 0;
        ContextMenus.OnOpen(_context, _ => opened++);
        _world.Add(MenuId, MenuObject);
        Game.CallScript("scr_create_context_menu", Instance.FromId(WolfId), "Attack");
        Assert.Equal(0, opened);
    }
}
