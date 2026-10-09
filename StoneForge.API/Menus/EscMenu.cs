namespace StoneForge;

/// <summary>The game's own Esc menu buttons (the in-game menu: o_close_panel), by what they do - the value is the game's
/// event for it. Their text is the game's, in its language.</summary>
public enum EscButton
{
    /// <summary>Closes the menu, back to the game.</summary>
    Resume = 0,
    /// <summary>Opens the settings.</summary>
    Settings = 1,
    /// <summary>Back to the main menu without saving (after the game's confirmation). The game shows it only where it
    /// can't save - in place of Save and Exit.</summary>
    Exit = 2,
    /// <summary>Saves, then back to the main menu (after the game's confirmation).</summary>
    SaveAndExit = 3,
    /// <summary>Quits the game, to the desktop (after the game's confirmation). Not in the game's own menu.</summary>
    ExitGame = 4,
    /// <summary>Opens the message log.</summary>
    MessageLog = 5,
    /// <summary>Opens the saves. The game shows it only outside permadeath, with saves to load.</summary>
    LoadGame = 6,
}

/// <summary>The Esc menu's buttons (the game's in-game menu), as <see cref="MainMenu"/> is the main menu's: mods add their
/// own (<see cref="AddButton(ModContext, string, Action)"/>, above the exit), before or after any button, add the game's by
/// <see cref="EscButton"/>, take buttons out (<see cref="RemoveButton(ModContext, EscButton)"/>), empty it
/// (<see cref="ClearButtons"/>), put it back as it was when the game started (<see cref="RestoreButtons"/>), and undo just
/// their own changes since (<see cref="UndoChanges"/>). A client that mustn't save, for one:
/// <code>
/// EscMenu.RemoveButton(context, EscButton.SaveAndExit);
/// EscMenu.AddButton(context, EscButton.Exit);
/// </code>
/// A button is named by its <see cref="EscButton"/> name ("SaveAndExit", "Save and Exit"), by the text shown on it, or by
/// the text of a button a mod added; case doesn't matter. The menu starts each time as the game makes it - what it shows
/// depends on where you are (Load Game with saves outside permadeath; Exit in place of Save and Exit where it can't
/// save) - and every loaded mod's calls are applied to that, in the order they were made. A change shows at once, the
/// menu open or not. A mod switched off takes what it did with it.</summary>
public static class EscMenu
{
    private abstract record Op(string Mod)
    {
        public bool FromLoad { get; init; } = _loading > 0;
    }
    private sealed record AddOp(string Mod, Button Button, string? Anchor, bool After, bool Waits) : Op(Mod);
    private sealed record VanillaOp(string Mod, EscButton Which, string? Anchor, bool After, bool Waits) : Op(Mod);
    private sealed record RemoveOp(string Mod, string Name) : Op(Mod);
    private sealed record ClearOp(string Mod) : Op(Mod);

    internal sealed record Button(int Id, string Mod, string Text, Action OnClick)
    {
        internal Func<string>? TextProvider { get; init; }
    }
    internal readonly record struct Entry(EscButton? Vanilla, Button? Mod);

    private static readonly List<Op> Ops = new();
    private static readonly Dictionary<int, Button> ById = new();
    private static int _nextId;
    private static int _loading;

    internal static void BeginLoad() => _loading++;
    internal static void EndLoad() => _loading = Math.Max(0, _loading - 1);

    /// <summary>Adds a button above the exit (Save and Exit, or Exit), or last if there's none; <paramref name="onClick"/>
    /// runs when it's clicked.</summary>
    public static void AddButton(ModContext context, string text, Action onClick) => Add(context, text, onClick, null, after: false, waits: false);

    /// <summary>Adds a button whose text refreshes with the mod's translations.</summary>
    public static void AddButton(ModContext context, Func<string> text, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(text);
        _ = context.Localization;
        Add(context, text(), onClick, null, false, false);
        var op = (AddOp)Ops[^1];
        var button = op.Button with { TextProvider = text };
        Ops[^1] = op with { Button = button };
        ById[button.Id] = button;
    }
    internal static void RefreshLocalizedButtons(string owner)
    {
        bool changed = false;
        for (int i = 0; i < Ops.Count; i++)
            if (Ops[i] is AddOp op && op.Mod == owner && op.Button.TextProvider is { } provider)
            {
                string text = provider();
                if (text == op.Button.Text) continue;
                var button = op.Button with { Text = text };
                Ops[i] = op with { Button = button };
                ById[button.Id] = button;
                changed = true;
            }
        if (changed) Changed();
    }

    /// <summary>Adds a button just above <paramref name="anchor"/> (a game button's name, or a button's text).</summary>
    public static void AddBefore(ModContext context, string anchor, string text, Action onClick) => Add(context, text, onClick, anchor, after: false);

    /// <inheritdoc cref="AddBefore(ModContext, string, string, Action)"/>
    public static void AddBefore(ModContext context, EscButton anchor, string text, Action onClick) => Add(context, text, onClick, anchor.ToString(), after: false);

    /// <summary>Adds a button just below <paramref name="anchor"/> (a game button's name, or a button's text).</summary>
    public static void AddAfter(ModContext context, string anchor, string text, Action onClick) => Add(context, text, onClick, anchor, after: true);

    /// <inheritdoc cref="AddAfter(ModContext, string, string, Action)"/>
    public static void AddAfter(ModContext context, EscButton anchor, string text, Action onClick) => Add(context, text, onClick, anchor.ToString(), after: true);

    /// <summary>Adds one of the game's buttons above the exit (or last) - one that's already in the menu is moved. It
    /// shows wherever the game is (Exit where it could save too).</summary>
    public static void AddButton(ModContext context, EscButton button) => Vanilla(context, button, null, after: false, waits: false);

    /// <summary>Adds one of the game's buttons just above <paramref name="anchor"/>.</summary>
    public static void AddBefore(ModContext context, string anchor, EscButton button) => Vanilla(context, button, anchor, after: false);

    /// <inheritdoc cref="AddBefore(ModContext, string, EscButton)"/>
    public static void AddBefore(ModContext context, EscButton anchor, EscButton button) => Vanilla(context, button, anchor.ToString(), after: false);

    /// <summary>Adds one of the game's buttons just below <paramref name="anchor"/>.</summary>
    public static void AddAfter(ModContext context, string anchor, EscButton button) => Vanilla(context, button, anchor, after: true);

    /// <inheritdoc cref="AddAfter(ModContext, string, EscButton)"/>
    public static void AddAfter(ModContext context, EscButton anchor, EscButton button) => Vanilla(context, button, anchor.ToString(), after: true);

    /// <summary>Takes one of the game's buttons out of the menu (<see cref="AddButton(ModContext, EscButton)"/> puts it
    /// back).</summary>
    public static void RemoveButton(ModContext context, EscButton button)
    {
        if (!Enum.IsDefined(button))
            throw new ArgumentOutOfRangeException(nameof(button), "Not one of the game's Esc menu buttons");
        Remove(context, button.ToString());
    }

    /// <summary>Takes a button out by its name: a game button's, the text shown on it, or another mod's button's text. One
    /// that isn't there yet (another mod's, added later) goes when it comes.</summary>
    public static void RemoveButton(ModContext context, string name) => Remove(context, name);

    /// <summary>Empties the menu: the game's buttons and every mod's added so far. Add back what's wanted.</summary>
    public static void ClearButtons(ModContext context)
    {
        Ops.Add(new ClearOp(context.Id));
        Changed();
    }

    /// <summary>The menu back as it was when the game started: the game's buttons and what every mod did to it while it
    /// loaded - whatever's been cleared, added, moved or taken out since is undone.</summary>
    public static void RestoreButtons(ModContext context)
    {
        for (int i = Ops.Count - 1; i >= 0; i--)
            if (!Ops[i].FromLoad)
                Forget(i);
        Changed();
    }

    /// <summary>Undoes what this mod has done to the menu since it loaded (what other mods did stays): for a change a mod
    /// makes for a while, and takes back.</summary>
    public static void UndoChanges(ModContext context)
    {
        for (int i = Ops.Count - 1; i >= 0; i--)
            if (!Ops[i].FromLoad && Ops[i].Mod == context.Id)
                Forget(i);
        Changed();
    }

    private static void Forget(int index)
    {
        if (Ops[index] is AddOp add)
            ById.Remove(add.Button.Id);
        Ops.RemoveAt(index);
    }

    private static void Add(ModContext context, string text, Action onClick, string? anchor, bool after, bool waits = true)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        var button = new Button(++_nextId, context.Id, text, onClick);
        ById[button.Id] = button;
        Ops.Add(new AddOp(context.Id, button, anchor, after, waits));
        Changed();
    }

    private static void Vanilla(ModContext context, EscButton button, string? anchor, bool after, bool waits = true)
    {
        if (!Enum.IsDefined(button))
            throw new ArgumentOutOfRangeException(nameof(button), "Not one of the game's Esc menu buttons");
        Ops.Add(new VanillaOp(context.Id, button, anchor, after, waits));
        Changed();
    }

    private static void Remove(ModContext context, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Ops.Add(new RemoveOp(context.Id, name));
        Changed();
    }

    // A mod switched off: what it did goes.
    internal static void RemoveMod(string mod)
    {
        if (Ops.RemoveAll(op => op.Mod == mod) == 0)
            return;
        foreach (int id in ById.Where(b => b.Value.Mod == mod).Select(b => b.Key).ToList())
            ById.Remove(id);
        Changed();
    }

    // A game button by name: its EscButton name, spaces or "and"/"&" written out or not ("Save & Exit", "load game").
    private static EscButton? ParseVanilla(string name)
    {
        string key = name.Replace(" ", "").Replace("&", "And").Trim();
        return Enum.TryParse(key, ignoreCase: true, out EscButton which) && Enum.IsDefined(which) ? which : null;
    }

    // The game's text for each of its buttons: their row in its button_hover table.
    private static readonly Dictionary<EscButton, int> TextRows = new()
    {
        [EscButton.Resume] = 21, [EscButton.LoadGame] = 90, [EscButton.MessageLog] = 56, [EscButton.Settings] = 14,
        [EscButton.Exit] = 45, [EscButton.SaveAndExit] = 104, [EscButton.ExitGame] = 15,
    };

    // The menu from every loaded mod's calls in order, applied to the game's as it made it (shown: its buttons, top to
    // bottom); texts: the game's buttons' as shown, to name them by. A call whose anchor (or a button to take out) isn't
    // there yet is tried again after the rest; then an add goes above the exit (or last), and a removal does nothing.
    internal static List<Entry> Layout(IReadOnlyList<EscButton> shown, IReadOnlyDictionary<EscButton, string> texts)
    {
        var list = shown.Select(b => new Entry(b, null)).ToList();
        int Find(string anchor)
        {
            if (ParseVanilla(anchor) is { } vanilla)
                return list.FindIndex(e => e.Vanilla == vanilla);
            return list.FindIndex(e => string.Equals(e.Mod?.Text ?? (e.Vanilla is { } v ? texts.GetValueOrDefault(v) : null),
                anchor.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        // (Above the exit: Save and Exit, or Exit - the last of them; else last.)
        int AboveExit()
        {
            int exit = list.FindLastIndex(e => e.Vanilla is EscButton.SaveAndExit or EscButton.Exit);
            return exit >= 0 ? exit : list.Count;
        }
        int Place(string? anchor, bool after, bool strict)
        {
            if (anchor == null)
                return AboveExit();
            int at = Find(anchor);
            if (at >= 0)
                return after ? at + 1 : at;
            return strict ? -1 : AboveExit();
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
                        if (existing >= 0)
                            list.Insert(existing, new Entry(vanilla.Which, null));
                        return false;
                    }
                    list.Insert(at, new Entry(vanilla.Which, null));
                    return true;
                }
                case RemoveOp remove:
                {
                    int at = Find(remove.Name);
                    if (at < 0)
                        return !strict;
                    list.RemoveAt(at);
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

    // ---- in the game ----

    // Our buttons carry this variable: their Button.Id (the game's: -1, made by us as the game makes them).
    internal const string IndexVar = "stonemod_esc_button";
    private const int VanillaMark = -1;
    private static Instance _pressed;
    private static bool _rebuild;

    // A change shows at once: an open menu is laid out again next frame.
    private static void Changed() => _rebuild = true;

    internal static void Install(ModContext context)
    {
        // The menu made (o_close_panel's Create: the game's buttons, top to bottom): laid out as the mods want it.
        context.OnCode("gml_Object_o_close_panel_Create_0", after: (panel, _) =>
        {
            _rebuild = false;
            LayOut(panel.Persist());
        });
        context.Frame += () =>
        {
            if (!_rebuild)
                return;
            _rebuild = false;
            if (Game.CallBuiltin("instance_find", (int)GameObjectId.o_close_panel, 0).AsInstance is { IsNone: false } panel)
                LayOut(panel);
        };
        // Clicks: o_button's input event (user event 15) runs its event on release - ours have none (-4); the release is
        // noted before, and the mod's click run after.
        context.OnCode("gml_Object_o_button_Other_25", before: (button, _) =>
        {
            if (button.Get(IndexVar) is { Kind: GmKind.Real } mark && mark.AsInt != VanillaMark && button.Get("guiInteractiveState").AsInt == 4
                && button.Get("pressed").AsBool && button.Get("is_activate").AsBool)
                _pressed = button.Persist();
            return false;
        }, after: (button, _) =>
        {
            if (_pressed.IsNone || !button.Persist().Equals(_pressed))
                return;
            _pressed = default;
            if (ById.TryGetValue(button.Get(IndexVar).AsInt, out var clicked))
                Hooks.Invoke(clicked.Mod, "Esc menu " + clicked.Text, () => { clicked.OnClick(); return false; });
        });
    }

    // The menu laid out: its buttons (the game's as it made them, and any we made before) gone, and the list made again
    // as the game makes its buttons - each in its row, the column centred under the logo as the game centres it.
    private static void LayOut(Instance panel)
    {
        if (Ops.Count == 0 || !panel.Exists)
            return;
        var buttons = Instances.All(GameObjectId.o_ingame_menu_button)
            .Where(b => GuiParent(b).Equals(panel)).OrderBy(b => b.Get("y").AsReal).ToList();
        var shown = buttons.Where(b => b.Get(IndexVar).Kind != GmKind.Real || b.Get(IndexVar).AsInt == VanillaMark)
            .Select(b => b.Get("event")).Where(e => e.Kind == GmKind.Real && Enum.IsDefined((EscButton)e.AsInt))
            .Select(e => (EscButton)e.AsInt).ToList();
        // (Laid out again from what the game made: what we made last time - the game's or the mods' - isn't what it made.)
        if (buttons.Any(b => b.Get(IndexVar).Kind == GmKind.Real))
            shown = _shown;
        else
            _shown = shown;
        var texts = TextRows.ToDictionary(t => t.Key, t => GameText(t.Value));
        var list = Layout(shown, texts);
        foreach (var button in buttons)
            Game.CallBuiltin("instance_destroy", button);
        // (The game's spacing and centring: o_close_panel's Create.)
        int sprite = Game.CallBuiltin("object_get_sprite", (int)GameObjectId.o_ingame_menu_button).AsInt;
        double offset = Game.CallBuiltin("sprite_get_height", sprite).AsReal + 2;
        double logo = Game.CallBuiltin("sprite_get_height", Gm.AssetGetIndex("s_stoneshard_logo")).AsReal;
        const double textOffset = 30;
        double width = Game.Global["cameraWidth"].AsReal, height = Game.Global["cameraHeight"].AsReal;
        double x = width / 2;
        double top = height / 2 + offset / 2 - list.Count * offset / 2 + logo / 2 + textOffset / 2;
        panel["logoY"] = top - offset / 2 - logo / 2 - textOffset;
        panel["textY"] = panel.Get("logoY").AsReal + logo / 2 + textOffset / 2;
        double depth = panel.Get("depth").AsReal - 1;
        for (int row = 0; row < list.Count; row++)
        {
            Entry entry = list[row];
            Instance made = Scripts.scr_guiCreateInteractive.Call(null, panel, GmValue.From(GameObjectId.o_ingame_menu_button), depth, x, top + offset * row);
            if (made.IsNone)
            {
                Game.Log($"Esc menu: couldn't make the button \"{entry.Mod?.Text ?? entry.Vanilla.ToString()}\"");
                continue;
            }
            made = made.Persist();
            if (entry.Vanilla is { } game)
            {
                made["text"] = texts[game];
                made["event"] = (int)game;
                made[IndexVar] = VanillaMark;
                // (Its sounds, as the game gives them: Resume a click of its own - the game's sound 1181 - and the plain ones
                // no press sound.)
                if (game == EscButton.Resume)
                    made["click_snd"] = 1181;
                if (game is EscButton.Resume or EscButton.LoadGame or EscButton.MessageLog or EscButton.Settings)
                    made["pre_click_snd"] = -4;
                // (Load Game with no saves: greyed out, as the main menu's.)
                if (game == EscButton.LoadGame && !Game.CallScript("scr_slotsExist", default).AsBool)
                {
                    made["is_activate"] = false;
                    made["is_deactivate"] = true;
                }
            }
            else
            {
                made["text"] = entry.Mod!.Text;
                made["event"] = -4;
                made[IndexVar] = entry.Mod.Id;
                made["pre_click_snd"] = -4;
            }
        }
    }

    // What the game made, the last time it made the menu (laid out again from it while the menu's open).
    private static List<EscButton> _shown = new();

    private static Instance GuiParent(Instance button) => InstanceOf(button.Get("guiParent"));

    // An instance the game keeps a reference or number to, by its id (none if it names none).
    private static Instance InstanceOf(GmValue value) => value.Kind switch
    {
        GmKind.Instance => value.AsInstance.Persist(),
        GmKind.Real when value.AsInt >= 0 => Instance.FromId(value.AsInt),
        _ => default,
    };

    private static string GameText(int row)
    {
        GmValue table = Game.Global["button_hover"];
        string text = table.Kind == GmKind.Real ? Game.CallBuiltin("ds_list_find_value", table, row).AsString : "";
        return text.Length > 0 ? text : TextRows.First(t => t.Value == row).Key.ToString().ToUpperInvariant();
    }

    // (Tests: what the mods' calls make of the menu the game made.)
    internal static IReadOnlyList<string> Describe(IReadOnlyList<EscButton>? shown = null, IReadOnlyDictionary<EscButton, string>? texts = null)
        => Layout(shown ?? Default, texts ?? new Dictionary<EscButton, string>()).Select(e => e.Vanilla?.ToString() ?? e.Mod!.Text).ToList();

    // The menu as the game makes it outside permadeath with saves, where it can save.
    internal static readonly IReadOnlyList<EscButton> Default = new[]
        { EscButton.Resume, EscButton.LoadGame, EscButton.MessageLog, EscButton.Settings, EscButton.SaveAndExit };

    internal static void ResetForTests()
    {
        Ops.Clear();
        ById.Clear();
        _shown.Clear();
    }
}
