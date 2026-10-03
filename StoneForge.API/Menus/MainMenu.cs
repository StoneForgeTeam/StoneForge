using StoneForge.Objects;

namespace StoneForge;

/// <summary>The game's own main menu buttons - the main list's and its play screens' - by what they do (their text is
/// the game's, in its language). Each does in the menu what it does in the game's own, greyed out (or, Continue
/// without a last save, left out) as the game's is.</summary>
public enum VanillaButton
{
    /// <summary>Opens the play screen (Continue, New Game, Load Game). "Start" names it too.</summary>
    Play = 0,
    Settings = 1,
    Credits = 2,
    Exit = 3,
    /// <summary>Loads the last save (greyed out when it can't be; left out with none).</summary>
    Continue = 4,
    /// <summary>Opens the new game screen (Prologue, Adventure, permadeath).</summary>
    NewGame = 5,
    /// <summary>Opens the saves (greyed out with none).</summary>
    LoadGame = 6,
    /// <summary>Starts a new game with the prologue.</summary>
    Prologue = 7,
    /// <summary>Starts a new game in the adventure (no prologue, permadeath off).</summary>
    Adventure = 8,
    /// <summary>The game's Back: back to the list before, as it goes back a screen from its play screens - the last
    /// <see cref="MainMenu.ClearButtons"/> (and everything since) undone, and the list made again. At the menu as it
    /// started, it stays.</summary>
    Back = 10,
}

/// <summary>The main menu's list of buttons. Mods add their own above Exit (<see cref="AddButton(ModContext, string, Action)"/>),
/// or before or after any button (<see cref="AddBefore(ModContext, string, string, Action)"/>,
/// <see cref="AddAfter(ModContext, string, string, Action)"/>); the game's own buttons are added the same way, by
/// <see cref="VanillaButton"/>. <see cref="ClearButtons"/> empties the menu, and <see cref="RestoreButtons"/> puts it
/// back as it was when the game started:
/// <code>
/// MainMenu.ClearButtons(context);
/// MainMenu.AddButton(context, VanillaButton.Play);
/// MainMenu.AddButton(context, "Multiplayer", window.Open);
/// MainMenu.AddButton(context, VanillaButton.Settings);
/// MainMenu.AddButton(context, VanillaButton.Exit);
/// </code>
/// A button is named (as an anchor) by its <see cref="VanillaButton"/> name ("Play" or "Start", "Settings", "Credits",
/// "Exit"), by the text shown on it (in the game's language), or by the text of a button a mod added; case doesn't
/// matter. The menu is laid out from every loaded mod's calls, in the order they were made (mods load in folder order),
/// and shows a change at once. A named button that isn't there puts the new one above Exit (or last). A mod switched
/// off takes what it did with it: its buttons go, and what it cleared comes back.</summary>
public static class MainMenu
{
    // What a mod did to the menu, in order: replayed each time the game builds it (Layout).
    // (FromLoad: made while its mod was loading - what the menu is "at startup", RestoreButtons.)
    private abstract record Op(string Mod)
    {
        public bool FromLoad { get; init; } = _loading > 0;
    }
    // (Waits: its anchor was named, so it waits for it to be there; AddButton's "above Exit" doesn't.)
    private sealed record AddOp(string Mod, Button Button, string? Anchor, bool After, bool Waits) : Op(Mod);
    private sealed record ClearOp(string Mod) : Op(Mod);
    private sealed record VanillaOp(string Mod, VanillaButton Which, string? Anchor, bool After, bool Waits) : Op(Mod);

    // A mod's button: its Id (on the instance, IndexVar) finds it when it's clicked.
    internal sealed record Button(int Id, string Mod, string Text, Action OnClick);

    // An entry in the laid-out list: one of the game's buttons, or a mod's.
    internal readonly record struct Entry(VanillaButton? Vanilla, Button? Mod);

    private static readonly List<Op> Ops = new();
    private static readonly Dictionary<int, Button> ById = new();
    private static int _nextId;
    private static int _loading;

    // The loader, around a mod's Load: what it does to the menu then is the menu "at startup".
    internal static void BeginLoad() => _loading++;
    internal static void EndLoad() => _loading = Math.Max(0, _loading - 1);

    /// <summary>Adds a button above Exit (or last, if Exit isn't there); <paramref name="onClick"/> runs when it's
    /// clicked.</summary>
    public static void AddButton(ModContext context, string text, Action onClick)
        => Add(context, text, onClick, nameof(VanillaButton.Exit), after: false, waits: false);

    /// <summary>Adds a button just above <paramref name="anchor"/> (a game button's name, or a button's text).</summary>
    public static void AddBefore(ModContext context, string anchor, string text, Action onClick)
        => Add(context, text, onClick, anchor, after: false);

    /// <inheritdoc cref="AddBefore(ModContext, string, string, Action)"/>
    public static void AddBefore(ModContext context, VanillaButton anchor, string text, Action onClick)
        => Add(context, text, onClick, anchor.ToString(), after: false);

    /// <summary>Adds a button just below <paramref name="anchor"/> (a game button's name, or a button's text).</summary>
    public static void AddAfter(ModContext context, string anchor, string text, Action onClick)
        => Add(context, text, onClick, anchor, after: true);

    /// <inheritdoc cref="AddAfter(ModContext, string, string, Action)"/>
    public static void AddAfter(ModContext context, VanillaButton anchor, string text, Action onClick)
        => Add(context, text, onClick, anchor.ToString(), after: true);

    /// <summary>Adds one of the game's buttons (back) above Exit (or last). One that's already in the menu is moved.</summary>
    public static void AddButton(ModContext context, VanillaButton button)
        => Vanilla(context, button, nameof(VanillaButton.Exit), after: false, waits: false);

    /// <summary>Adds one of the game's buttons (back) just above <paramref name="anchor"/>.</summary>
    public static void AddBefore(ModContext context, string anchor, VanillaButton button) => Vanilla(context, button, anchor, after: false);

    /// <inheritdoc cref="AddBefore(ModContext, string, VanillaButton)"/>
    public static void AddBefore(ModContext context, VanillaButton anchor, VanillaButton button) => Vanilla(context, button, anchor.ToString(), after: false);

    /// <summary>Adds one of the game's buttons (back) just below <paramref name="anchor"/>.</summary>
    public static void AddAfter(ModContext context, string anchor, VanillaButton button) => Vanilla(context, button, anchor, after: true);

    /// <inheritdoc cref="AddAfter(ModContext, string, VanillaButton)"/>
    public static void AddAfter(ModContext context, VanillaButton anchor, VanillaButton button) => Vanilla(context, button, anchor.ToString(), after: true);

    /// <summary>Empties the menu: the game's buttons and every mod's added so far. Add back what's wanted.</summary>
    public static void ClearButtons(ModContext context)
    {
        Ops.Add(new ClearOp(context.Id));
        Changed();
    }

    /// <summary>The menu back as it was when the game started: the game's buttons and what every mod did to the menu
    /// while it loaded - whatever's been cleared, added or moved since is undone.</summary>
    public static void RestoreButtons(ModContext context)
    {
        UndoSinceStartup();
        Changed();
    }

    // One menu back: the last ClearButtons since the mods loaded, and everything since, undone (false: none).
    internal static bool UndoLastClear()
    {
        int last = Ops.FindLastIndex(op => op is ClearOp && !op.FromLoad);
        if (last < 0)
            return false;
        // (By place: the ops are records, equal by value - Remove would take the first one alike.)
        for (int i = Ops.Count - 1; i >= last; i--)
            if (!Ops[i].FromLoad)
                Forget(i);
        return true;
    }

    // What's been done to the menu since the mods loaded, undone.
    private static void UndoSinceStartup()
    {
        for (int i = Ops.Count - 1; i >= 0; i--)
            if (!Ops[i].FromLoad)
                Forget(i);
    }

    // An op gone, with its button.
    private static void Forget(int index)
    {
        if (Ops[index] is AddOp add)
            ById.Remove(add.Button.Id);
        Ops.RemoveAt(index);
    }

    private static void Vanilla(ModContext context, VanillaButton button, string? anchor, bool after, bool waits = true)
    {
        if (!Enum.IsDefined(button))
            throw new ArgumentOutOfRangeException(nameof(button), "Not one of the game's main menu buttons");
        Ops.Add(new VanillaOp(context.Id, button, anchor, after, waits));
        Changed();
    }

    private static void Add(ModContext context, string text, Action onClick, string? anchor, bool after, bool waits = true)
    {
        var button = new Button(++_nextId, context.Id, text, onClick);
        ById[button.Id] = button;
        Ops.Add(new AddOp(context.Id, button, anchor, after, waits));
        Changed();
    }

    // A mod switched off: what it did goes (its buttons, and what it cleared comes back).
    internal static void RemoveMod(string mod)
    {
        if (Ops.RemoveAll(op => op.Mod == mod) == 0)
            return;
        // (Its click handlers are its code, which has to be let go to unload.)
        foreach (int id in ById.Where(b => b.Value.Mod == mod).Select(b => b.Key).ToList())
            ById.Remove(id);
        Changed();
    }

    // A game button by name: its VanillaButton name, spaces or not ("New Game", "LoadGame"), or "Start" for Play.
    private static VanillaButton? ParseVanilla(string name)
    {
        string key = name.Replace(" ", "").Trim();
        if (string.Equals(key, "Start", StringComparison.OrdinalIgnoreCase))
            return VanillaButton.Play;
        return Enum.TryParse(key, ignoreCase: true, out VanillaButton which) && Enum.IsDefined(which) ? which : null;
    }

    // The game's text for each of its buttons: their row in its button_hover table.
    private static readonly Dictionary<VanillaButton, int> TextRows = new()
    {
        [VanillaButton.Play] = 13, [VanillaButton.Settings] = 14, [VanillaButton.Credits] = 51, [VanillaButton.Exit] = 15,
        [VanillaButton.Continue] = 21, [VanillaButton.NewGame] = 20, [VanillaButton.LoadGame] = 90,
        [VanillaButton.Prologue] = 81, [VanillaButton.Adventure] = 82, [VanillaButton.Back] = 17,
    };

    // The list, from every loaded mod's calls in order; texts: the game's buttons' as shown (to name them by). A call
    // whose anchor isn't there yet is tried again after the rest (another mod's button may come later), then placed
    // above Exit (or last).
    internal static List<Entry> Layout(IReadOnlyDictionary<VanillaButton, string> texts)
    {
        var list = new List<Entry> { new(VanillaButton.Play, null), new(VanillaButton.Settings, null),
            new(VanillaButton.Credits, null), new(VanillaButton.Exit, null) };
        int Find(string anchor)
        {
            if (ParseVanilla(anchor) is { } vanilla)
                return list.FindIndex(e => e.Vanilla == vanilla);
            return list.FindIndex(e => string.Equals(e.Mod?.Text ?? (e.Vanilla is { } v ? texts.GetValueOrDefault(v) : null),
                anchor.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        // Where an entry goes: by its anchor (-1: it isn't there, and strict), else above Exit or last.
        int Place(string? anchor, bool after, bool strict)
        {
            if (anchor == null)
                return list.Count;
            int at = Find(anchor);
            if (at >= 0)
                return after ? at + 1 : at;
            if (strict)
                return -1;
            int exit = list.FindIndex(e => e.Vanilla == VanillaButton.Exit);
            return exit >= 0 ? exit : list.Count;
        }
        bool Apply(Op op, bool strict)
        {
            switch (op)
            {
                case ClearOp:
                    list.Clear();
                    return true;
                case AddOp add:
                {
                    int at = Place(add.Anchor, add.After, strict && add.Waits);
                    if (at < 0)
                        return false;
                    list.Insert(at, new Entry(null, add.Button));
                    return true;
                }
                case VanillaOp vanilla:
                {
                    int existing = list.FindIndex(e => e.Vanilla == vanilla.Which);
                    if (existing >= 0)
                        list.RemoveAt(existing);
                    int at = Place(vanilla.Anchor, vanilla.After, strict && vanilla.Waits);
                    if (at < 0)
                    {
                        // (Its anchor isn't there yet: it stays where it was until it's tried again.)
                        if (existing >= 0)
                            list.Insert(existing, new Entry(vanilla.Which, null));
                        return false;
                    }
                    list.Insert(at, new Entry(vanilla.Which, null));
                    return true;
                }
            }
            return true;
        }
        var deferred = Ops.Where(op => !Apply(op, strict: true)).ToList();
        foreach (var op in deferred)
            Apply(op, strict: false);
        return list;
    }

    // A change shows at once: the main list is made again next frame - if it's the one showing, and no window is over
    // it (one is: when it closes - UIWindow checks this). One made later is laid out anyway (mods change it as they
    // load, before the main menu is up).
    private static bool _rebuild;
    private static void Changed()
    {
        if (Game.Running && Gm.InstanceExists(GameObjectId.o_mainMenuNavContainer))
        {
            Game.Global["stonemod_menu_dirty"] = true;
            _rebuild = true;
        }
    }

    // Whether the nav container is showing the main list (not the play screen...): it's cleared for every screen
    // (user event 14), and the main list is user event 0.
    private static bool _mainList;

    private static void Rebuild()
    {
        if (!_rebuild)
            return;
        _rebuild = false;
        if (!_mainList || !Game.Global["stonemod_menu_dirty"].AsBool)
            return;
        foreach (var nav in Instances.All<GameInstance>(GameObjectId.o_mainMenuNavContainer))
        {
            if (!nav.Instance.Get("active").AsBool)
                continue;
            Game.Global["stonemod_menu_dirty"] = false;
            Game.CallBuiltinAs("event_user", nav.Instance, nav.Instance, 0);
        }
    }

    // Our buttons carry this variable: their Button.Id (BackId: a game's Back in our menu).
    internal const string IndexVar = "stonemod_button";
    private const int BackId = -2;
    private static Instance _pressed;
    private static readonly List<Instance> Made = new();
    private static Instance _fadeFrom;

    internal static void Install(ModContext context)
    {
        Events.o_mainMenuNavContainer.Other_24.Before(context, _ =>
        {
            _mainList = false;
            return false;
        });
        Events.o_mainMenuNavContainer.Other_10.After(context, LayOut);
        context.Frame += FollowFade;
        context.Frame += Rebuild;
        // Clicks: o_button's input event (user event 15, which o_mainMenuButton's runs first) calls
        // event_user(event) on release. Ours have no event; we note the release before and act after.
        Events.o_mainMenuButton.Other_25.Before(context, button =>
        {
            if (button.Instance.Get(IndexVar) is { Kind: GmKind.Real } mark && mark.AsInt != BackId && button.Instance.Get("guiInteractiveState").AsInt == 4
                && button.Instance.Get("pressed").AsBool && button.Instance.Get("is_activate").AsBool)
                _pressed = button.Instance;
            return false;
        });
        // Our Back (the game's - user event 10, its event 10): one menu back, before the game makes its main list again.
        // (The game's own Back on its play screens is left as it is.)
        Events.o_mainMenuButton.Other_20.Before(context, button =>
        {
            if (button.Instance.Get(IndexVar) is { Kind: GmKind.Real } id && id.AsInt == BackId)
                UndoLastClear();
            return false;
        });
        Events.o_mainMenuButton.Other_25.After(context, button =>
        {
            if (_pressed.IsNone || !button.Instance.Equals(_pressed))
                return;
            _pressed = default;
            if (!ById.TryGetValue(button.Instance.Get(IndexVar).AsInt, out var clicked))
                return;
            Hooks.Invoke(clicked.Mod, "menu " + clicked.Text, () => { clicked.OnClick(); return false; });
        });
    }

    // The main list was (re)built - the game's four buttons, one under another: laid out as the mods want it. The
    // game's that aren't in it are destroyed, the mods' made, and every one put in its row.
    private static void LayOut(o_mainMenuNavContainer nav)
    {
        _mainList = true;
        Made.Clear();
        _fadeFrom = default;
        if (Ops.Count == 0)
            return;
        var vanilla = new Dictionary<VanillaButton, GameInstance>();
        foreach (var button in Instances.All<GameInstance>(GameObjectId.o_mainMenuButton))
            if (button.ObjectIndex == (int)GameObjectId.o_mainMenuButton && button.Instance.Get(IndexVar).Kind != GmKind.Real
                && button.Instance.Get("event").AsInt is >= 0 and <= 3 and int e)
                vanilla.TryAdd((VanillaButton)e, button);
        if (vanilla.Count == 0)
            return;
        var texts = TextRows.ToDictionary(t => t.Key, t => GameText(t.Value));
        var list = Layout(texts);
        // (The game's buttons fade in, and ours with them - with one of the game's that stays.)
        double alpha = vanilla.Values.First().ImageAlpha;
        double offset = nav.buttonsOffset;
        foreach (var (which, button) in vanilla)
            if (!list.Any(entry => entry.Vanilla == which))
                Game.CallBuiltin("instance_destroy", button.Instance);
        // (Prologue and Adventure apply the new game screen's permadeath checkbox: none here.)
        if (!Game.CallBuiltin("variable_instance_exists", nav.Instance, "permadeathCheckbox").AsBool)
            nav.Instance.Set("permadeathCheckbox", -4);
        int row = 0;
        foreach (var entry in list)
        {
            double top = offset * row;
            if (entry.Vanilla is { } which && vanilla.TryGetValue(which, out var built))
            {
                Scripts.scr_guiLayoutOffsetUpdate.Call(nav, built, 0, top);
                _fadeFrom = built.Instance;
                row++;
                continue;
            }
            // (Continue with no last save: left out, as the game does.)
            bool? usable = entry.Vanilla is { } check ? Usable(check) : true;
            if (usable == null)
                continue;
            Instance made = Scripts.scr_guiCreateInteractive.Call(nav, nav.buttonsContainer, GmValue.From(GameObjectId.o_mainMenuButton), nav.Depth - 1, 0, top);
            if (made.IsNone)
            {
                Game.Log($"Main menu: couldn't make the button \"{entry.Mod?.Text ?? entry.Vanilla.ToString()}\"");
                continue;
            }
            if (entry.Vanilla is { } game)
            {
                made.Set("text", texts[game]);
                // (The game's own action; Back, marked as ours, undoes the menu's changes first - see Install.)
                made.Set("event", (int)game);
                if (game == VanillaButton.Back)
                    made.Set(IndexVar, BackId);
                if (usable == false)
                {
                    made.Set("is_activate", false);
                    made.Set("is_deactivate", true);
                }
            }
            else
            {
                made.Set("text", entry.Mod!.Text);
                // (No event: the click is ours - see Install.)
                made.Set("event", -4);
                made.Set(IndexVar, entry.Mod.Id);
            }
            made.Set("image_alpha", alpha);
            Made.Add(made);
            row++;
        }
        nav.height = nav.logoHeight + nav.logoTextHeight + (double)nav.buttonsOffset * Game.CallBuiltin("variable_instance_get", nav.buttonsContainer, "guiChildrenCount").AsReal;
        // The first build runs inside the container's Create, before its size is set (the game checks for
        // buyGameButton, made at the end of Create, the same way); Create lays it out itself afterwards.
        if (Game.CallBuiltin("variable_instance_exists", nav.Instance, "buyGameButton").AsBool)
            Game.CallBuiltinAs("event_user", nav.Instance, nav.Instance, 15);
    }

    // A game button's text, from its button_hover table (its name, before that's loaded).
    private static string GameText(int row)
    {
        GmValue table = Game.Global["button_hover"];
        string text = table.Kind == GmKind.Real ? Game.CallBuiltin("ds_list_find_value", table, row).AsString : "";
        return text.Length > 0 ? text : TextRows.First(t => t.Value == row).Key.ToString().ToUpperInvariant();
    }

    // Whether one of the game's play screen buttons can be used now, as its screen (o_mainMenuNavContainer user event 1)
    // decides: true yes, false greyed out, null left out (Continue without a last save).
    private static bool? Usable(VanillaButton button)
    {
        try
        {
            switch (button)
            {
                case VanillaButton.Continue:
                {
                    GmValue slots = Game.Global["slotsMap"];
                    var character = Game.CallBuiltin("ds_map_find_value", slots, "lastCharacter");
                    var save = Game.CallBuiltin("ds_map_find_value", slots, "lastSave");
                    GmValue map = Game.CallScript("scr_slotSaveMapLoad", default, character, save);
                    if (map.Kind != GmKind.Real || map.AsInt < 0)
                        return null;
                    try
                    {
                        bool valid = Game.CallBuiltin("ds_map_find_value", map, "valid").AsBool;
                        GmValue wipe = Game.CallScript("ds_map_find_value_ext", default, map, "wipeVersion", Game.Global["slotsWipeVersionInit"]);
                        GmValue compiler = Game.CallScript("ds_map_find_value_ext", default, map, "compiler", Game.Global["slotsCompilerInit"]);
                        return valid && wipe == Game.Global["slotsWipeVersion"] && compiler == Game.Global["slotsCompiler"];
                    }
                    finally { Game.CallBuiltin("ds_map_destroy", map); }
                }
                case VanillaButton.NewGame:
                {
                    GmValue order = Game.CallScript("scr_slotsGetOrderList", default);
                    try { return Game.CallBuiltin("ds_list_size", order).AsInt < 10; }
                    finally { Game.CallBuiltin("ds_list_destroy", order); }
                }
                case VanillaButton.LoadGame:
                    return Game.CallScript("scr_slotsExist", default).AsBool;
                default:
                    return true;
            }
        }
        catch (Exception e)
        {
            Game.Log($"Main menu: couldn't tell whether {button} can be used: {e.Message}");
            return button == VanillaButton.Continue ? null : true;
        }
    }

    // While the list fades in, ours keep the game's buttons' opacity (with none of theirs left, ours just show).
    private static void FollowFade()
    {
        if (Made.Count == 0)
            return;
        bool following = !_fadeFrom.IsNone && _fadeFrom.Exists;
        double alpha = following ? _fadeFrom.Get("image_alpha") : 1;
        foreach (var button in Made)
            if (button.Exists)
                button.Set("image_alpha", alpha);
        if (alpha >= 1)
            Made.Clear();
    }

    // (Tests: what the mods' calls make of the menu.)
    internal static IReadOnlyList<string> Describe(IReadOnlyDictionary<VanillaButton, string>? texts = null)
        => Layout(texts ?? new Dictionary<VanillaButton, string>()).Select(e => e.Vanilla?.ToString() ?? e.Mod!.Text).ToList();

    internal static void ResetForTests()
    {
        Ops.Clear();
        ById.Clear();
    }
}
