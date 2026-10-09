namespace StoneForge;

// Uses the game's o_dialogue, renderer, history, paging and contract response buttons.
// Only the fragment graph and callbacks are supplied by StoneForge.
internal sealed class NativeDialogue
{
    internal const string Prefix = "stoneforge_dialogue_";
    private readonly DialogueConversation _conversation;
    private Instance _panel;
    private readonly bool _nested;
    private int _presentation;
    internal NativeDialogue(DialogueConversation conversation, Instance panel = default)
    { _conversation = conversation; _panel = panel; _nested = panel.Exists; }
    internal static Instance Find() => ContextMenus.InstanceOf(Game.CallBuiltin("instance_find", (int)GameObjectId.o_dialogue, 0));
    // Native o_dialogue creates o_gui_no_click with show_ui=false, so scr_is_cutscene
    // reports true during normal Talk. A nested topic must ignore that owned blocker,
    // while still refusing genuine cutscenes, fades and room changes.
    internal static bool SceneBlocked
    {
        get
        {
            if (Gm.InstanceExists(GameObjectId.o_black_overlay) || Gm.InstanceExists(GameObjectId.o_smoothRoomChanger)) return true;
            var controller = ContextMenus.InstanceOf(Game.CallBuiltin("instance_find", (int)GameObjectId.o_cutscene_controller, 0));
            return controller.Exists && controller.Get("cutscene_on").AsBool;
        }
    }
    internal string Root => Prefix + _conversation.Id;
    internal Instance Panel => _panel;
    private string Line => Root + "_line";
    internal string Choice(string key) => Root + "_" + _conversation.Revision + "_choice_" + key;
    internal bool OwnsPanel
    {
        get { if (!_panel.Exists) return false; using var context = _panel.Get("dialog_id").AsStruct; return context != null && context["RootFragment"].AsString == Root; }
    }
    internal void Open()
    {
        using var context = GmStruct.Create();
        foreach (string member in new[] { "Fragments", "Scripts", "Specs", "Sounds", "Variables", "Speakers", "Strings" })
        { using var value = GmStruct.Create(); context[member] = value; }
        context["RootFragment"] = Root; context["Monologue"] = false; context["owner"] = _conversation.Speaker;
        // Built-in responses (including text paging) read these fields even when all
        // mod-authored lines are literal strings. Match scr_dialogue_context_create.
        using var parent = _nested ? _panel.Get("dialog_id").AsStruct : null;
        void Metadata(string key, GmValue fallback)
        {
            GmValue inherited = parent?[key] ?? GmValue.Undefined;
            context[key] = inherited.IsUndefined ? fallback : inherited;
        }
        Metadata("__sex", _conversation.Speaker.Get("sex"));
        Metadata("__occupation", _conversation.Speaker.Get("occupation"));
        Metadata("__town", _conversation.Speaker.Get("town"));
        Metadata("__npc_name", Game.CallScript("scr_npc_dialogue_name_tag", _conversation.Speaker, _conversation.Speaker));
        GmValue faction = _conversation.Speaker.Get("town_faction");
        Metadata("__faction_id", faction.Kind == GmKind.String ? faction : _conversation.Speaker.Get("faction_key"));
        using var inheritedVariables = parent?["Variables"].AsStruct;
        if (inheritedVariables != null) context["Variables"] = inheritedVariables;
        using (var variables = context["Variables"].AsStruct!) variables["owner"] = _conversation.Speaker;
        if (_nested)
        {
            // This script pushes the NPC's original context and returns to it when our topic ends.
            Game.CallScript("scr_dialogue_change_context", _panel, context, Root);
            _panel.Set("block_event", false);
        }
        else
        {
            _panel = ContextMenus.InstanceOf(Game.CallScript("scr_dialog_create", _conversation.Speaker, _conversation.Speaker, false));
            if (!_panel.Exists) throw new InvalidOperationException("Stoneshard could not create its dialogue panel.");
            _panel.Set("dialog_id", context); _panel.Set("interact_id", _conversation.Speaker);
        }
        Render(true);
    }
    internal void Render(bool newNode = false)
    {
        if (!OwnsPanel) return;
        using var context = _panel.Get("dialog_id").AsStruct!;
        foreach (string member in new[] { "Fragments", "Strings", "Speakers", "Specs" })
        { using var value = GmStruct.Create(); context[member] = value; }
        using var fragments = context["Fragments"].AsStruct!;
        using var strings = context["Strings"].AsStruct!;
        using var speakers = context["Speakers"].AsStruct!;
        using var specs = context["Specs"].AsStruct!;
        using var options = GmArray.Create();
        strings[Line] = _conversation.Text; speakers[Line] = "NPC";
        using var generic = GmStruct.Create(); generic["generic"] = true; specs[Line] = generic;
        foreach (var response in _conversation.Responses)
        {
            string key = Choice(response.Key);
            options.Push(key); strings[key] = response.Text; speakers[key] = "Player";
            fragments[key] = Line; specs[key] = generic;
            Game.CallScript("scr_dialogue_set_option_lock", _panel, key, !response.IsEnabled);
        }
        if (options.Length == 0)
        {
            string finish = Choice("@finish"); options.Push(finish); strings[finish] = Game.CallScript("scr_player_answer", _panel, "leave");
            speakers[finish] = "Player"; fragments[finish] = Line; specs[finish] = generic;
        }
        // Every node has its own fragment, so native history and stale buttons refer to the right turn.
        if (newNode || _panel.Get("full_text").AsString != _conversation.Text) _presentation++;
        string line = Line + "_" + _conversation.Revision + "_" + _presentation;
        strings[line] = _conversation.Text; speakers[line] = "NPC"; specs[line] = generic;
        fragments[Root] = line;
        fragments[line] = options;
        // Let Stoneshard advance its graph and render the line, history, paging and
        // response buttons together, inside its own GML error boundary.
        try { Hooks.CallOriginal("scr_dialogue_advance", _panel, _panel, new GmValue[] { Root, true }); }
        catch (Exception error)
        { throw new InvalidOperationException("Stoneshard failed to advance dialogue " + Root + " at " + _conversation.NodeKey, error); }
    }
    internal void Close()
    {
        if (!OwnsPanel) return;
        if (!_nested || !Game.CallScript("scr_dialogue_exit_context", _panel).AsBool)
            Game.CallBuiltin("instance_destroy", _panel);
    }
}
