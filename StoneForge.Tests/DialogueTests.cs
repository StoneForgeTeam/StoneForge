using StoneForge;

public sealed class DialogueTests : FakeGame
{
    private const int PlayerId = 100001, NpcId = 100002;
    private readonly FakeWorld _world = new();
    private readonly FakeScripts _scripts = new();
    private bool _cutscene;
    private readonly ModContext _context = new("dialogue_test");
    private readonly ModContext _other = new("dialogue_other");
    private static Instance Speaker => Instance.FromId(NpcId);
    public DialogueTests()
    {
        Dialogues.ResetForTests();
        World = _world; _world.LendsIds = _world.ExistsByObject = true;
        GameScripts = _scripts; InstallNativeDialogue(_world, _scripts);
        _scripts.Add("scr_is_cutscene", _ => _cutscene);
        _scripts.Add("scr_stop_player", _ => GmValue.Undefined);
        _world.Assets["o_stonemod_modal"] = 999;
        _world.Add(PlayerId, (int)GameObjectId.o_player); _world.Add(NpcId, 998);
        _world.Vars[PlayerId] = new() { ["xx"] = 26, ["yy"] = 26 };
        _world.Vars[NpcId] = new() { ["xx"] = 52, ["yy"] = 26, ["name"] = "Villager", ["avatar"] = -1 };
        Globals["room"] = 12; Globals["resolution"] = "1280x720";
    }
    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id); Hooks.RemoveMod(_other.Id);
        Dialogues.ResetForTests(); base.Dispose();
    }
    private static DialogueDefinition Tree(Action<DialogueConversation>? selected = null) => new("work", "offer")
    {
        Nodes =
        {
            new DialogueNode("offer", "Can you help?") { Choices = { new DialogueChoice("yes", "Yes", "thanks") { OnSelected = selected }, new DialogueChoice("no", "No") } },
            new DialogueNode("thanks", "Thank you") { Choices = { new DialogueChoice("leave", "Goodbye") } },
        },
    };
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Edited_templates_keep_live_progress_and_reject_unknown_arguments(int progress)
    {
        var tree = new DialogueDefinition("templates", "report") { Nodes = {
            new DialogueNode("report", "Delivered {0}/{1}") { TextArguments = _ => new object?[] { progress, 5 }, Choices = {
                new DialogueChoice("back", "Return {0}") { TextArguments = _ => new object?[] { progress } } } } } };
        var dialogue = _context.Dialogues.Add(tree); var session = dialogue.Start(Speaker)!;
        Assert.Equal("Delivered {0}/{1}", session.EditableTemplate(null));
        dialogue.Edits.SetText("node/report", "Supplies: {0} of {1}");
        dialogue.Edits.SetText("choice/report/back", "Back ({0})");
        Assert.Equal($"Supplies: {progress} of 5", session.Text);
        Assert.Equal($"Back ({progress})", session.Responses.Single().Text);
        progress++; session.Refresh();
        Assert.Equal($"Supplies: {progress} of 5", session.Text);
        Assert.Throws<ArgumentException>(() => session.FormatTemplate(null, "Bad {9}", validate: true));
        dialogue.Edits.SetText("node/report", "Bad {9}");
        Assert.Equal($"Delivered {progress}/5", session.Text);
    }
    [Fact]
    public void Action_context_only_exposes_its_own_active_window_and_can_open_a_specific_node()
    {
        var dialogue = _context.Dialogues.Add(Tree()); var session = dialogue.Start(Speaker)!;
        var panel = session.Native!.Panel;
        Assert.Same(session, new DialogOptionContext(_context, "test", Speaker, panel).Conversation);
        Assert.Null(new DialogOptionContext(_other, "test", Speaker, panel).Conversation);
        Assert.Null(new DialogOptionContext(_context, "test", Speaker, default).Conversation);
        session.Close();
        Assert.Equal("thanks", dialogue.Start(Speaker, "thanks")!.NodeKey);
    }
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(128, 2)]
    [InlineData(0, 1)]
    public void Edited_topic_slot_inserts_among_vanilla_options_and_preserves_its_callback(int position, int expected)
    {
        var dialogue = _context.Dialogues.Add(Tree());
        dialogue.AddTopic(_ => true, _ => "Any work?", position: 2);
        dialogue.Edits.SetText("topic/0", "Edited job");
        if (position > 0) dialogue.Edits.SetTopicPosition(0, position);
        var panel = ContextMenus.InstanceOf(Game.CallScript("scr_dialog_create", Speaker, Speaker, false));
        using var context = GmStruct.Create(); context["RootFragment"] = "osbrook_smith";
        foreach (string name in new[] { "Strings", "Speakers", "Fragments", "Specs" })
        { using var member = GmStruct.Create(); context[name] = member; }
        panel.Set("dialog_id", context); panel.Set("topic", "osbrook_smith");
        using var options = GmArray.From(new GmValue[] { "trade", "leave" });
        Game.CallScript("dialogue_create_option_buttons", panel, options, false, false);
        string topic = options[expected].AsString;
        Assert.Contains("_topic", topic);
        Assert.Equal(new[] { "trade", "leave" }, options.Where(v => v.AsString != topic).Select(v => v.AsString));
        using (var strings = context["Strings"].AsStruct!) Assert.Equal("Edited job", strings[topic].AsString);
        Game.CallScript("scr_dialogue_advance", panel, topic);
        Assert.Equal("offer", Dialogues.Active!.NodeKey);
    }
    [Fact]
    public void Variant_edits_keep_dynamic_mod_text_providers_live_when_context_changes()
    {
        int progress = 1;
        var tree = new DialogueDefinition("dynamic_variants", "report") { Nodes = { new DialogueNode("report", "")
        { TextProvider = _ => "Progress: " + progress, Choices = { new DialogueChoice("back", "Back") { TextProvider = _ => "Return " + progress } } } } };
        var dialogue = _context.Dialogues.Add(tree); var session = dialogue.Start(Speaker)!;
        dialogue.Edits.SetText(DialogueEdits.VariantField("node/report", session.SourceText), "One done!");
        dialogue.Edits.SetText(DialogueEdits.VariantField("choice/report/back", session.SourceResponses["back"]), "Return to the first report");
        Assert.Equal("One done!", session.Text);
        progress = 2; session.Refresh(); Assert.Equal("Progress: 2", session.Text); Assert.Equal("Return 2", session.Responses.Single().Text);
        progress = 1; session.Refresh(); Assert.Equal("One done!", session.Text);
        string saved = dialogue.Edits.Serialize(); dialogue.Edits.Replace(saved); Assert.Equal("One done!", session.Text);
    }
    [Fact]
    public void Presentation_edits_refresh_live_without_callbacks_and_keep_conditions_and_actions()
    {
        int entries = 0, actions = 0; bool enabled = false;
        var definition = new DialogueDefinition("edit", "start") { Nodes = { new DialogueNode("start", "Original")
        { OnEnter = _ => entries++, TextProvider = _ => "Dynamic", Choices =
          { new DialogueChoice("a", "A") { OnSelected = _ => actions++, EnabledWhen = _ => enabled }, new DialogueChoice("b", "B") } } } };
        var dialogue = _context.Dialogues.Add(definition); var session = dialogue.Start(Speaker)!;
        dialogue.Edits.SetText("node/start", "New line\nsecond line");
        dialogue.Edits.SetText("choice/start/a", "Edited A"); dialogue.Edits.MoveResponse("start", "a", 2);
        Assert.Equal("New line\nsecond line", session.Text); Assert.Equal(new[] { "b", "a" }, session.Responses.Select(r => r.Key));
        Assert.Equal("Edited A", session.Responses[1].Text); Assert.False(session.Responses[1].IsEnabled);
        Assert.Equal(1, entries); Assert.Equal(0, actions); Assert.False(session.Choose("a"));
        dialogue.Edits.Reset("node/start"); Assert.Equal("Dynamic", session.Text);
        dialogue.Edits.Reset("choice/start/a"); Assert.Equal(new[] { "a", "b" }, session.Responses.Select(r => r.Key));
        enabled = true; Assert.True(session.Choose("a")); Assert.Equal(1, actions);
    }
    [Fact]
    public void Edits_round_trip_validate_atomically_and_only_override_the_edited_language()
    {
        string previous = Localization.Language;
        try
        {
            Localization.SetLanguage("en-US");
            var dialogue = _context.Dialogues.Add(Tree()); dialogue.AddTopic(_ => true, _ => "Job");
            dialogue.Edits.SetText("node/offer", "English"); dialogue.Edits.SetTopicPosition(0, 3); dialogue.Edits.MoveResponse("offer", "no", 1);
            string saved = dialogue.Edits.Serialize();
            Localization.SetLanguage("ru-RU"); Assert.Null(dialogue.Edits.Text("node/offer"));
            dialogue.Edits.SetText("node/offer", "Russian");
            dialogue.Edits.Replace(saved); Assert.Null(dialogue.Edits.Text("node/offer"));
            Localization.SetLanguage("en-US"); Assert.Equal("English", dialogue.Edits.Text("node/offer"));
            Assert.Equal(3, dialogue.Edits.TopicPosition(0)); Assert.Equal("no", dialogue.Edits.OrderedChoices("offer").First().Key);
            Assert.Throws<InvalidDataException>(() => dialogue.Edits.Replace(saved.Replace("\"no\"", "\"unknown\"")));
            Assert.Throws<InvalidDataException>(() => dialogue.Edits.Replace(saved.Replace("\"Version\": 1", "\"Version\": 99")));
            Assert.Equal(saved, dialogue.Edits.Serialize());
            Assert.Throws<ArgumentException>(() => dialogue.Edits.SetText("choice/offer/no", " "));
            Assert.Throws<InvalidDataException>(() => dialogue.Edits.SetText("node/missing", "Bad"));
            Assert.Throws<ArgumentOutOfRangeException>(() => dialogue.Edits.MoveResponse("offer", "no", 3));
            Assert.Equal(saved, dialogue.Edits.Serialize());
        }
        finally { Localization.SetLanguage(previous); }
    }
    [Fact]
    public void Saved_edits_reload_after_mod_unload_and_keep_a_backup()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "editor-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var context = new ModContext("editor_save", folder);
        try
        {
            var dialogue = context.Dialogues.Add(Tree()); dialogue.Edits.SetText("node/offer", "Saved"); dialogue.Edits.Save();
            dialogue.Edits.SetText("node/offer", "Saved again"); dialogue.Edits.Save();
            Assert.Contains("Saved", File.ReadAllText(Path.Combine(folder, dialogue.Edits.Path + ".bak")));
            Hooks.RemoveMod(context.Id);
            var reloaded = new ModContext(context.Id, folder).Dialogues.Add(Tree());
            Assert.Equal("Saved again", reloaded.Edits.Text("node/offer"));
            Assert.Throws<InvalidOperationException>(() => dialogue.Edits.Save());
        }
        finally { Hooks.RemoveMod(context.Id); Directory.Delete(folder, true); }
    }
    [Theory]
    [InlineData("First\nSecond\r\nThird")]
    [InlineData("Literal \\n, a path C:\\mods and a quote \"")]
    public void Editor_text_escape_round_trip_preserves_newlines_and_backslashes(string text)
        => Assert.Equal(text, StoneForge.Loader.DialogueEditor.Decode(StoneForge.Loader.DialogueEditor.Encode(text)));
    [Fact]
    public void Editor_installs_no_startup_frame_handler_and_idle_shortcut_does_not_query_instances()
    {
        int frames = Hooks.FrameHandlers.Count, draws = Hooks.DrawGuiHandlers.Count;
        var context = new ModContext("editor_startup", Path.Combine(ModFiles.GameFolder, "stoneforge-editor-" + Guid.NewGuid()));
        StoneForge.Loader.NpcDialogueEditor.Install(context);
        Assert.Equal(frames, Hooks.FrameHandlers.Count);
        Assert.Equal(draws + 2, Hooks.DrawGuiHandlers.Count); // Screen and shortcut, both in Draw GUI.
        Input = new FakeInput(); Calls.Clear();
        Hooks.DrawGuiHandlers.Last().Handler();
        Assert.DoesNotContain("instance_exists", Calls);
        Assert.DoesNotContain("instance_find", Calls);
        Hooks.RemoveMod(context.Id);
    }
    [Fact]
    public void Registration_qualifies_ids_and_snapshots_nodes_and_choices()
    {
        var definition = Tree(); var registered = _context.Dialogues.Add(definition);
        Assert.Equal("dialogue_test:work", registered.Id);
        definition.Nodes[0].Choices.Clear(); definition.Nodes.Clear();
        var session = registered.Start(Speaker)!;
        Assert.Equal("Can you help?", session.Text); Assert.Equal(2, session.Responses.Count);
        Assert.Equal("Villager", session.SpeakerName);
        Assert.Throws<ArgumentException>(() => _context.Dialogues.Add(Tree()));
        Assert.Equal("dialogue_other:work", _other.Dialogues.Add(Tree()).Id);
    }
    [Fact]
    public void Branches_run_actions_once_and_stale_clicks_cannot_select_a_new_node()
    {
        int accepted = 0;
        var session = _context.Dialogues.Add(Tree(_ => accepted++)).Start(Speaker)!;
        int old = session.Revision;
        Assert.True(session.Choose("yes")); Assert.Equal(1, accepted); Assert.Equal("thanks", session.NodeKey);
        Assert.False(session.Choose("leave", old)); Assert.True(session.IsOpen);
        Assert.True(session.Choose("leave")); Assert.False(session.IsOpen);
        Assert.False(session.Choose("leave")); Assert.Equal(DialogueCloseReason.Completed, session.CloseReason);
        Assert.Null(Dialogues.Active); Assert.False(UIWindow.AnyOpen);
    }
    [Fact]
    public void Conditions_refresh_and_are_rechecked_before_choice_actions()
    {
        bool visible = false, enabled = true; int ran = 0;
        var tree = new DialogueDefinition("conditions", "start") { Nodes = { new DialogueNode("start", "Choose")
        { Choices = { new DialogueChoice("pick", "Pick") { VisibleWhen = _ => visible, EnabledWhen = _ => enabled, OnSelected = _ => ran++ } } } } };
        var session = _context.Dialogues.Add(tree).Start(Speaker)!;
        Assert.Empty(session.Responses); Assert.False(session.Choose("pick"));
        visible = true; session.Refresh(); Assert.True(session.Responses.Single().IsEnabled);
        enabled = false; Assert.False(session.Choose("pick")); Assert.Equal(0, ran);
        session.Refresh(); Assert.False(session.Responses.Single().IsEnabled);
        enabled = true; Assert.True(session.Choose("pick")); Assert.Equal(1, ran);
    }
    [Fact]
    public void Refresh_does_not_repeat_entry_actions_and_explicit_loops_do()
    {
        int enters = 0;
        var tree = new DialogueDefinition("loop", "start") { Nodes = { new DialogueNode("start", "Hello")
        { OnEnter = _ => enters++, Choices = { new DialogueChoice("again", "Again", "start") } } } };
        var session = _context.Dialogues.Add(tree).Start(Speaker)!;
        session.Refresh(); session.Refresh(); Assert.Equal(1, enters);
        int previous = session.Revision;
        Assert.True(session.Choose("again")); Assert.Equal(2, enters);
        Assert.False(session.Choose("again", previous));
    }
    [Fact]
    public void Callback_navigation_takes_precedence_and_reentrant_clicks_are_ignored()
    {
        var tree = Tree(session => { Assert.False(session.Choose("yes")); session.GoTo("offer"); });
        var session = _context.Dialogues.Add(tree).Start(Speaker)!;
        Assert.True(session.Choose("yes")); Assert.Equal("offer", session.NodeKey); Assert.True(session.IsOpen);
        Assert.Throws<ArgumentException>(() => session.GoTo("missing"));
    }
    [Fact]
    public void Choice_failure_closes_without_running_the_next_node_or_repeating_an_action()
    {
        int attempts = 0;
        var session = _context.Dialogues.Add(Tree(_ => { attempts++; throw new Exception("test callback"); })).Start(Speaker)!;
        Assert.False(session.Choose("yes")); Assert.Equal(DialogueCloseReason.CallbackFailed, session.CloseReason);
        Assert.False(session.Choose("yes")); Assert.Equal(1, attempts); Assert.Null(Dialogues.Active);
    }
    [Fact]
    public void Text_and_condition_failures_close_the_conversation()
    {
        var tree = new DialogueDefinition("bad_text", "start") { Nodes = { new DialogueNode("start", "Hi") { TextProvider = _ => throw new Exception("bad text") } } };
        Assert.Null(_context.Dialogues.Add(tree).Start(Speaker)); Assert.Null(Dialogues.Active);
        var conditions = new DialogueDefinition("bad_condition", "start") { Nodes = { new DialogueNode("start", "Hi")
        { Choices = { new DialogueChoice("pick", "Pick") { EnabledWhen = _ => throw new Exception("bad condition") } } } } };
        Assert.Null(_context.Dialogues.Add(conditions).Start(Speaker)); Assert.False(UIWindow.AnyOpen);
    }
    [Fact]
    public void Text_providers_cannot_navigate_or_select_choices_during_refresh()
    {
        var tree = new DialogueDefinition("provider", "start") { Nodes = { new DialogueNode("start", "Hi")
        { TextProvider = session => { Assert.False(session.Choose("pick")); session.GoTo("start"); return "bad"; }, Choices = { new DialogueChoice("pick", "Pick") } } } };
        Assert.Null(_context.Dialogues.Add(tree).Start(Speaker)); Assert.Null(Dialogues.Active);
    }
    [Fact]
    public void Missing_distant_and_native_dialogue_speakers_do_not_open_a_second_modal()
    {
        var registered = _context.Dialogues.Add(Tree());
        Assert.Null(registered.Start(default));
        _world.Vars[NpcId]["xx"] = 260; Assert.Null(registered.Start(Speaker));
        _world.Vars[NpcId]["xx"] = 52;
        _world.Add(100003, (int)GameObjectId.o_dialogue); Assert.Null(registered.Start(Speaker));
        _world.Active.Remove(100003);
        _cutscene = true; Assert.Null(registered.Start(Speaker)); _cutscene = false;
        _world.Add(100004, (int)GameObjectId.o_exit_confirm_panel); Assert.Null(registered.Start(Speaker));
        _world.Active.Remove(100004);
        var session = registered.Start(Speaker)!; Assert.Null(_other.Dialogues.Add(Tree()).Start(Speaker));
        session.Close(); Assert.NotNull(registered.Start(Speaker));
    }
    [Fact]
    public void Speaker_loss_room_changes_and_mod_unload_close_and_release_ui()
    {
        var definition = Tree();
        var registered = _context.Dialogues.Add(definition);
        var first = registered.Start(Speaker)!;
        _world.Active.Remove(NpcId); first.Tick(); Assert.Equal(DialogueCloseReason.SpeakerUnavailable, first.CloseReason);
        _world.Active.Add(NpcId);
        var second = registered.Start(Speaker)!;
        Globals["room"] = 13; second.Tick(); Assert.Equal(DialogueCloseReason.RoomChanged, second.CloseReason);
        var third = registered.Start(Speaker)!;
        Hooks.RemoveMod(_context.Id); Assert.Equal(DialogueCloseReason.ModUnloaded, third.CloseReason);
        Assert.False(UIWindow.AnyOpen); Assert.Null(Dialogues.Active);
        Assert.Throws<InvalidOperationException>(() => registered.Start(Speaker));
    }
    [Fact]
    public void Close_notifications_run_once_but_not_after_mod_unload()
    {
        int calls = 0; DialogueCloseReason? reason = null;
        var tree = new DialogueDefinition("close", "start")
        { OnClosed = (_, why) => { calls++; reason = why; }, Nodes = { new DialogueNode("start", "Hello") } };
        var registered = _context.Dialogues.Add(tree);
        var session = registered.Start(Speaker)!; session.Close(); session.Close();
        Assert.Equal(1, calls); Assert.Equal(DialogueCloseReason.Cancelled, reason);
        registered.Start(Speaker); Hooks.RemoveMod(_context.Id); Assert.Equal(1, calls);
    }
    [Fact]
    public void Invalid_graphs_are_rejected_before_registration()
    {
        Assert.Throws<ArgumentException>(() => _context.Dialogues.Add(new("bad", "missing")));
        var missing = new DialogueDefinition("bad", "start") { Nodes = { new DialogueNode("start", "Hi") { Choices = { new DialogueChoice("pick", "Pick", "missing") } } } };
        Assert.Throws<ArgumentException>(() => _context.Dialogues.Add(missing));
        var duplicate = new DialogueDefinition("bad", "start") { Nodes = { new DialogueNode("start", "Hi"), new DialogueNode("start", "Again") } };
        Assert.Throws<ArgumentException>(() => _context.Dialogues.Add(duplicate));
        var choices = new DialogueDefinition("bad", "start") { Nodes = { new DialogueNode("start", "Hi") { Choices = { new DialogueChoice("pick", "Pick"), new DialogueChoice("pick", "Pick again") } } } };
        Assert.Throws<ArgumentException>(() => _context.Dialogues.Add(choices));
        Assert.NotNull(_context.Dialogues.Add(new("bad", "start") { Nodes = { new DialogueNode("start", "Good") } }));
    }
    [Fact]
    public void Live_localization_refreshes_text_and_choices_without_replaying_entry()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "mods", "sf-dialogue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Localization"));
        File.WriteAllText(Path.Combine(folder, "Localization", "en-US.json"), "{\"line\":\"Hello\",\"choice\":\"Leave\"}");
        File.WriteAllText(Path.Combine(folder, "Localization", "fr.json"), "{\"line\":\"Bonjour\",\"choice\":\"Partir\"}");
        string previous = Localization.Language; var context = new ModContext("dialogue_locale", folder); int enters = 0;
        try
        {
            Localization.SetLanguage("en-US");
            var tree = new DialogueDefinition("test", "start") { Nodes = { new DialogueNode("start", "Fallback")
            { TextKey = "line", OnEnter = _ => enters++, Choices = { new DialogueChoice("leave", "Leave") { TextKey = "choice" } } } } };
            var session = context.Dialogues.Add(tree).Start(Speaker)!;
            Assert.Equal("Hello", session.Text);
            Localization.SetLanguage("fr");
            foreach (var handler in Hooks.FrameHandlers.Where(h => h.Mod == context.Id).ToArray()) handler.Handler();
            Assert.Equal("Bonjour", session.Text); Assert.Equal("Partir", session.Responses.Single().Text); Assert.Equal(1, enters);
        }
        finally { Hooks.RemoveMod(context.Id); Localization.SetLanguage(previous); Directory.Delete(folder, true); }
    }
    [Fact]
    public void Native_choices_execute_callbacks_and_ignore_stale_fragments()
    {
        int actions = 0; var session = _context.Dialogues.Add(Tree(_ => actions++)).Start(Speaker)!;
        var panel = NativeDialogue.Find(); Assert.True(panel.Exists); Assert.False(UIWindow.AnyOpen);
        string oldChoice = session.Native!.Choice("yes");
        Game.CallScript("scr_dialogue_advance", panel, oldChoice);
        Assert.Equal(1, actions); Assert.Equal("thanks", session.NodeKey);
        Assert.Equal("Thank you", panel.Get("full_text").AsString);
        Assert.True(_scripts.Runs["scr_dialogue_advance"] >= 2); // The native flow renders each node.
        Game.CallScript("scr_dialogue_advance", panel, oldChoice); Assert.Equal(1, actions);
        Game.CallScript("scr_dialogue_advance", panel, session.Native.Choice("leave"));
        Assert.False(panel.Exists); Assert.Equal(DialogueCloseReason.Completed, session.CloseReason);
    }
    [Fact]
    public void Talk_topics_preserve_native_options_and_return_to_original_context()
    {
        var dialogue = _context.Dialogues.Add(Tree());
        dialogue.AddTopic(npc => npc.Id == Speaker.Id, _ => "Any work?");
        var panel = ContextMenus.InstanceOf(Game.CallScript("scr_dialog_create", Speaker, Speaker, false));
        using var context = GmStruct.Create(); context["RootFragment"] = "osbrook_smith";
        foreach (string name in new[] { "Strings", "Speakers", "Fragments", "Specs" })
        { using var member = GmStruct.Create(); context[name] = member; }
        panel.Set("dialog_id", context); panel.Set("topic", "osbrook_smith");
        using var options = GmArray.From(new GmValue[] { "trade", "leave" });
        Game.CallScript("dialogue_create_option_buttons", panel, options, false, false);
        Assert.Equal(3, options.Length); Assert.Equal("trade", options[0].AsString); Assert.Equal("leave", options[1].AsString);
        string topic = options[2].AsString;
        _cutscene = true; // Native Talk hides the UI, so scr_is_cutscene returns true.
        using (var strings = context["Strings"].AsStruct!) Assert.Equal("Any work?", strings[topic].AsString);
        _world.Add(100099, (int)GameObjectId.o_cutscene_controller);
        _world.Vars[100099] = new() { ["cutscene_on"] = true };
        Game.CallScript("scr_dialogue_advance", panel, topic);
        Assert.Null(Dialogues.Active); // A real cutscene must still block entry.
        _world.Vars[100099]["cutscene_on"] = false;
        _world.Add(100098, (int)GameObjectId.o_black_overlay);
        Game.CallScript("scr_dialogue_advance", panel, topic); Assert.Null(Dialogues.Active);
        _world.Active.Remove(100098);
        _world.Add(100098, (int)GameObjectId.o_smoothRoomChanger);
        Game.CallScript("scr_dialogue_advance", panel, topic); Assert.Null(Dialogues.Active);
        _world.Active.Remove(100098);
        Game.CallScript("scr_dialogue_advance", panel, topic);
        var session = Dialogues.Active!; Assert.NotNull(session); Assert.True(session.Native!.OwnsPanel);
        session.Choose("no"); Assert.Null(Dialogues.Active); Assert.True(panel.Exists);
        using var restored = panel.Get("dialog_id").AsStruct!; Assert.Equal(context, restored);
    }
    [Fact]
    public void Closing_the_native_panel_cancels_the_session_and_empty_nodes_can_finish()
    {
        var session = _context.Dialogues.Add(Tree()).Start(Speaker)!;
        Game.CallBuiltin("instance_destroy", NativeDialogue.Find()); session.Tick();
        Assert.Equal(DialogueCloseReason.Cancelled, session.CloseReason);
        var empty = _other.Dialogues.Add(new("empty", "start") { Nodes = { new DialogueNode("start", "Hello") } }).Start(Speaker)!;
        Game.CallScript("scr_dialogue_advance", NativeDialogue.Find(), empty.Native!.Choice("@finish"));
        Assert.Equal(DialogueCloseReason.Completed, empty.CloseReason);
    }
    [Fact]
    public void Multiple_topics_share_arguments_without_disposing_them_for_later_hooks()
    {
        for (int i = 0; i < 3; i++)
        {
            var definition = new DialogueDefinition("job_" + i, "offer")
            { Nodes = { new DialogueNode("offer", "Help?") } };
            _context.Dialogues.Add(definition).AddTopic(npc => npc.Id == Speaker.Id, _ => "A job");
        }
        var panel = ContextMenus.InstanceOf(Game.CallScript("scr_dialog_create", Speaker, Speaker, false));
        using var context = GmStruct.Create(); context["RootFragment"] = "osbrook_smith";
        foreach (string member in new[] { "Strings", "Speakers", "Fragments", "Specs" })
        { using var value = GmStruct.Create(); context[member] = value; }
        panel.Set("dialog_id", context); panel.Set("topic", "osbrook_smith");
        using var options = GmArray.From(new GmValue[] { "trade", "leave" });
        int laterCount = -1;
        _other.OnScript("dialogue_create_option_buttons", call =>
        { laterCount = call.Args[0].AsArray!.Length; return false; });
        // Hooks share this exact argument handle: disposing it breaks the next subscriber.
        Hooks.ScriptCalled("dialogue_create_option_buttons", panel, panel, new GmValue[] { options, false, false }, out _);
        Assert.Equal(5, laterCount); Assert.Equal(5, options.Length);
        Assert.Equal(1, _scripts.Runs["scr_dialog_create"]);
    }
    [Fact]
    public void Innkeeper_greeting_menu_exposes_topics_and_inherits_native_response_context()
    {
        var dialogue = _context.Dialogues.Add(Tree()); dialogue.AddTopic(_ => true, _ => "Ask about work");
        var panel = ContextMenus.InstanceOf(Game.CallScript("scr_dialog_create", Speaker, Speaker, false));
        using var native = GmStruct.Create(); native["RootFragment"] = "osbrook_innkeeper";
        foreach (string member in new[] { "Strings", "Speakers", "Fragments", "Specs", "Variables" })
        { using var value = GmStruct.Create(); native[member] = value; }
        native["__sex"] = "Female"; native["__occupation"] = "innkeeper";
        native["__town"] = "Osbrook"; native["__npc_name"] = "Brukk"; native["__faction_id"] = "GrandMagistrate";
        using (var variables = native["Variables"].AsStruct!) variables["rent_price"] = 15;
        panel.Set("dialog_id", native); panel.Set("topic", "greeting_HASH_innkeeper");
        using (var paging = GmArray.From(new GmValue[] { "next" }))
        { Game.CallScript("dialogue_create_option_buttons", panel, paging, false, false); Assert.Single(paging); }
        using var options = GmArray.From(new GmValue[] { "rent_room_HASH_1", "leave_HASH_2" });
        Game.CallScript("dialogue_create_option_buttons", panel, options, false, false);
        Assert.Equal(3, options.Length);
        Game.CallScript("scr_dialogue_advance", panel, options[2]);
        Assert.NotNull(Dialogues.Active);
        using var topic = panel.Get("dialog_id").AsStruct!;
        foreach (string key in new[] { "__sex", "__occupation", "__town", "__npc_name", "__faction_id" })
            Assert.Equal(native[key], topic[key]);
        using var inheritedVariables = topic["Variables"].AsStruct!; Assert.Equal(15, inheritedVariables["rent_price"].AsInt);
        Dialogues.Active!.Close();
    }
    [Fact]
    public void Direct_dialogues_initialize_all_native_line_filter_fields()
    {
        _world.Vars[NpcId]["sex"] = "Male"; _world.Vars[NpcId]["occupation"] = "herbalist";
        _world.Vars[NpcId]["town"] = "Osbrook"; _world.Vars[NpcId]["faction_key"] = "GrandMagistrate";
        var session = _context.Dialogues.Add(Tree()).Start(Speaker)!;
        using var context = NativeDialogue.Find().Get("dialog_id").AsStruct!;
        Assert.Equal("Male", context["__sex"].AsString); Assert.Equal("herbalist", context["__occupation"].AsString);
        Assert.Equal("Osbrook", context["__town"].AsString); Assert.Equal("villager", context["__npc_name"].AsString);
        Assert.Equal("GrandMagistrate", context["__faction_id"].AsString);
        session.Close();
    }}
