using StoneForge;
using StoneForge.Loader;

public sealed class NpcDialogueEditorTests : FakeGame
{
    private readonly FakeWorld _world = new();
    private readonly FakeScripts _scripts = new();
    private readonly ModContext _context = new("npc_editor_test");
    private readonly NpcDialogueEditing _editing;
    private readonly Instance _panel;
    private readonly Dictionary<string, string> _labels = new() { ["trade"] = "Trade", ["leave"] = "Goodbye" };
    public NpcDialogueEditorTests()
    {
        Dialogues.ResetForTests(); World = _world; GameScripts = _scripts;
        _world.LendsIds = _world.ExistsByObject = true; InstallNativeDialogue(_world, _scripts);
        _world.Add(100001, (int)GameObjectId.o_player); _world.Vars[100001] = new() { ["xx"] = 26, ["yy"] = 26 };
        _world.Add(100002, 998); _world.Vars[100002] = new() { ["id_name"] = "osbrook_smith", ["xx"] = 26, ["yy"] = 26, ["name"] = "Smith" };
        _scripts.Add("scr_is_cutscene", _ => false);
        _scripts.Add("scr_dialog_text_wrap", args => args[0]);
        _scripts.Add("scr_guiContainerChildrenDestroy", _ => GmValue.Undefined);
        _scripts.Add("scr_dialogue_sort_options", _ => GmValue.Undefined);
        _scripts.Add("scr_create_contract_button", args =>
        {
            int id = _world.Objects.Keys.Max() + 1; _world.Add(id, (int)GameObjectId.o_contract_button);
            _world.Vars[id] = new() { ["name"] = args[0], ["func"] = args[1], ["parent"] = _panel, ["canPress"] = true };
            return id;
        });
        _scripts.Add("dialogue_create_option_buttons", args =>
        {
            var panel = Instance.FromId((int)((long)_scripts.LastSelf - FakeWorld.PointerBase));
            var options = args[0].AsArray!;
            Game.CallScript("scr_dialogue_sort_options", panel, options);
            using var context = panel.Get("dialog_id").AsStruct!; using var strings = context["Strings"].AsStruct!;
            foreach (var option in options)
            {
                string key = option.AsString;
                string label = _labels.GetValueOrDefault(key) ?? strings[key].AsString;
                Game.CallScript("scr_create_contract_button", panel, label, key);
            }
            return GmValue.Undefined;
        });
        _panel = ContextMenus.InstanceOf(Game.CallScript("scr_dialog_create", Instance.FromId(100002), Instance.FromId(100002), false));
        using var context = GmStruct.Create(); context["RootFragment"] = "osbrook_smith";
        foreach (string member in new[] { "Strings", "Speakers", "Fragments", "Specs", "Variables" })
        { using var value = GmStruct.Create(); context[member] = value; }
        _panel.Set("dialog_id", context); _panel.Set("topic", "osbrook_smith");
        _panel.Set("text_fragment", "greeting"); _panel.Set("full_text", "Welcome");
        Globals["room"] = 12; Globals["resolution"] = "1280x720";
        _editing = new(_context); _editing.Install(); Render();
    }
    private void Render()
    {
        using var options = GmArray.From(new GmValue[] { "trade", "leave" });
        Game.CallScript("dialogue_create_option_buttons", _panel, options, false, false);
    }
    public override void Dispose()
    { Hooks.RemoveMod(_context.Id); Dialogues.ResetForTests(); base.Dispose(); }
    private static int _codeCalls;
    [Fact]
    public void Legacy_document_splits_by_NPC_and_restored_entries_stay_cleared_after_reload()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "npc-split-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            string legacy = Path.Combine(folder, "npcs.json");
            File.WriteAllText(legacy, """
                {"Version":1,"Edits":[
                  {"Npc":"npc_verren","Root":"verren_intro","Field":"option/trade","Text":{"en-US":"Verren trade","ru-RU":"Verren translation"},"ActionId":"stoneforge:exit_dialogue"},
                  {"Npc":"osbrook_smith","Root":"osbrook_smith","Field":"option/trade","Text":{"en-US":"Smith trade"}}],"Topics":[]}
                """);
            var loaded = new NpcDialogueEditing(_context, folder); loaded.Load();
            Assert.False(File.Exists(legacy)); Assert.True(File.Exists(legacy + ".migrated.bak"));
            string verrenPath = Path.Combine(folder, "npc_verren.json"), smithPath = Path.Combine(folder, "npc_osbrook_smith.json");
            Assert.Contains("Verren translation", File.ReadAllText(verrenPath));
            Assert.DoesNotContain("Smith trade", File.ReadAllText(verrenPath));
            Assert.Contains("Smith trade", File.ReadAllText(smithPath));
            Assert.DoesNotContain("Verren trade", File.ReadAllText(smithPath));
            var smith = _editing.Current!;
            Assert.Equal("Smith trade", loaded.Text(smith, "option/trade"));
            loaded.RestoreDialogue(smith); loaded.Save();
            var reload = new NpcDialogueEditing(_context, folder); reload.Load();
            Assert.Null(reload.Text(smith, "option/trade"));
            var verren = new NpcDialogueEditing.View { Panel = _panel, Npc = "npc_verren", Root = "verren_intro" };
            Assert.Equal("Verren trade", reload.Text(verren, "option/trade"));
            Assert.Equal("stoneforge:exit_dialogue", reload.ActionFor(verren, new("trade", "Trade", default, "Trade")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Per_NPC_file_overrides_legacy_edits_and_invalid_files_do_not_block_other_NPCs()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "npc-merge-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            string legacy = Path.Combine(folder, "npcs.json");
            File.WriteAllText(legacy, """
                {"Edits":[{"Npc":"osbrook_smith","Root":"osbrook_smith","Field":"option/trade","Text":{"en-US":"Legacy trade"}}],"Topics":[]}
                """);
            File.WriteAllText(Path.Combine(folder, "npc_osbrook_smith.json"), """
                {"Edits":[],"Topics":[]}
                """);
            File.WriteAllText(Path.Combine(folder, "npc_broken.json"), "invalid JSON");
            var loaded = new NpcDialogueEditing(_context, folder); loaded.Load();
            Assert.Null(loaded.Text(_editing.Current!, "option/trade"));
            Assert.True(File.Exists(legacy)); // Keep the aggregate intact when migration cannot complete.
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Removed_variant_lists_do_not_override_text_or_actions_in_existing_documents()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "removed-variants-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "npcs.json");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, """
                {"Version":1,"Edits":[{"Npc":"osbrook_smith","Root":"osbrook_smith","Field":"option/trade","Text":{"en-US":"Direct edit"}}],"Topics":[],
                 "Variants":[{"Npc":"osbrook_smith","Root":"osbrook_smith","Field":"option/trade","Random":true,"Text":{"en-US":"Old random line"},"ActionId":"stoneforge:exit_dialogue"}]}
                """);
            var loaded = new NpcDialogueEditing(_context, path); loaded.Load();
            var view = _editing.Current!;
            Assert.Equal("Direct edit", loaded.Text(view, "option/trade"));
            Assert.Null(loaded.ActionFor(view, view.Buttons.First(b => b.Key == "trade")));
            Assert.DoesNotContain("Variants", loaded.Serialize());
            Assert.DoesNotContain("Old random line", loaded.Serialize());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Native_translation_preview_indexes_the_open_dialogue_once_and_reuses_it_for_all_languages_and_misses()
    {
        var view = _editing.Current!;
        using var greeting = GmArray.Create(18); greeting[0] = "greeting"; greeting[7] = "Welcome"; greeting[6] = "Russian greeting"; greeting[9] = "German greeting";
        using var leave = GmArray.Create(18); leave[0] = "custom_leave"; leave[7] = "Goodbye"; leave[6] = "Russian goodbye"; leave[9] = "German goodbye";
        using var rows = GmArray.From(new GmValue[] { greeting, leave }); Globals["npc_lines"] = rows;
        _editing.SetPreviewLanguage(view, "ru-RU"); Assert.Equal(1, _editing.NativePreviewIndexBuilds);
        for (int i = 0; i < 10; i++) _editing.RefreshLanguage(view);
        Assert.Equal("Russian greeting", _panel.Get("full_text").AsString);
        Assert.Equal("Russian goodbye", view.Buttons.First(b => b.Key == "leave").Label);
        Assert.Equal("Trade", view.Buttons.First(b => b.Key == "trade").Label); // Missing native row is cached too.
        _editing.SetPreviewLanguage(view, "de-DE");
        Assert.Equal("German greeting", _panel.Get("full_text").AsString);
        Assert.Equal("German goodbye", view.Buttons.First(b => b.Key == "leave").Label);
        Assert.Equal(1, _editing.NativePreviewIndexBuilds);
        _editing.SetPreviewLanguage(view, null); Assert.Equal("Welcome", _panel.Get("full_text").AsString);
    }
    [Fact]
    public void Editing_the_displayed_vanilla_line_is_direct_and_restore_recovers_the_original_text()
    {
        KeepGlobalWrites = true; Input = new FakeInput { KeyboardOnly = true };
        var view = _editing.Current!;
        using var context = _panel.Get("dialog_id").AsStruct!; using var strings = context["Strings"].AsStruct!;
        using var pool = GmArray.From(new GmValue[] { "Trade", "Till then" }); strings["trade"] = pool;
        var render = ContextMenus.InstanceOf(_panel.Get("render")); _panel.Set("is_activate", true);
        render.Set("surfaceDraw", true); render.Set("guiVisibleAreaBorderRight", 500); render.Set("guiVisibleAreaBorderBottom", 400);
        var tool = _context.UI.InGame.Add(new NpcDialogueEditor(_editing));
        void OpenMenu()
        {
            var button = view.Buttons.First(b => b.Key == "trade").Button;
            button.Set("x", 20); button.Set("y", 20); button.Set("textWidth", 100); button.Set("textHeight", 20);
            button.Set("guiVisibleAreaBorderRight", 500); button.Set("guiVisibleAreaBorderBottom", 400);
            Assert.True(tool.EditAt(view, 25, 25));
        }
        void Click(string key) => tool.ContextPopup!.Children.OfType<UIButton>().Single(b => b.Text == Localization.Get("npc_editor." + key)).RaiseClick();
        try
        {
            OpenMenu(); Click("edit_text"); Assert.Equal("Trade", tool.InlineInput!.Text);
            Keyboard.Typed = "Custom trade"; Input.PressedKeys.Add(Keyboard.Enter); tool.InlineInput.RunUpdate(0.1); Input.PressedKeys.Clear();
            Assert.Null(tool.ContextPopup); Assert.Equal("Custom trade", view.Buttons.First(b => b.Key == "trade").Label);
            Assert.DoesNotContain("\"Variants\"", _editing.Serialize());
            OpenMenu(); Assert.DoesNotContain(tool.ContextPopup!.Children.OfType<UIButton>(), b => b.Text is "Other lines…" or "Random alternatives" or "Delete alternative");
            Click("restore_original"); Assert.Equal("Trade", view.Buttons.First(b => b.Key == "trade").Label);
            Assert.Equal("Trade", pool[0].AsString); Assert.Equal("Till then", pool[1].AsString);
        }
        finally { tool.Close(); UITextBox.ReleaseFocus(); }
    }
    [Fact]
    public void Registered_dialogue_providers_keep_the_preview_language_during_periodic_refresh_without_reentering()
    {
        int entered = 0, selected = 0;
        var dialogue = _context.Dialogues.Add(new("language_preview", "start")
        {
            Nodes = { new DialogueNode("start", "") { TextProvider = _ => "NPC " + Localization.Language, OnEnter = _ => entered++,
                Choices = { new DialogueChoice("answer", "Response") { TextProvider = _ => "Response " + Localization.Language, OnSelected = _ => selected++ } } } }
        });
        var conversation = dialogue.StartOnPanel(Instance.FromId(100002), _panel)!;
        var view = _editing.Current!;
        _editing.SetPreviewLanguage(view, "ru-RU");
        Assert.Equal("NPC ru-RU", conversation.Text); Assert.Equal("Response ru-RU", conversation.Responses.Single().Text);
        for (int i = 0; i < 30; i++) conversation.Tick();
        Assert.Equal("NPC ru-RU", conversation.Text); Assert.Equal("en-US", Localization.Language);
        Assert.Equal(1, entered); Assert.Equal(0, selected);
        _editing.SetPreviewLanguage(view, null); Assert.Equal("NPC en-US", conversation.Text);
    }
    [Fact]
    public void Whole_dialogue_language_picker_previews_all_entries_and_inline_edits_save_to_that_locale()
    {
        KeepGlobalWrites = true; Input = new FakeInput { KeyboardOnly = true };
        var view = _editing.Current!; var trade = view.Buttons.First(b => b.Key == "trade"); var leave = view.Buttons.First(b => b.Key == "leave");
        _editing.SetTranslation(view, null, "ru-RU", "Russian greeting");
        _editing.SetTranslation(view, trade, "ru-RU", "Russian trade"); _editing.SetTranslation(view, leave, "ru-RU", "Russian goodbye");
        var render = ContextMenus.InstanceOf(_panel.Get("render")); _panel.Set("is_activate", true); render.Set("surfaceDraw", true);
        render.Set("guiVisibleAreaBorderRight", 500); render.Set("guiVisibleAreaBorderBottom", 400);
        trade.Button.Set("x", 20); trade.Button.Set("y", 20); trade.Button.Set("textWidth", 100); trade.Button.Set("textHeight", 20);
        trade.Button.Set("guiVisibleAreaBorderRight", 500); trade.Button.Set("guiVisibleAreaBorderBottom", 400);
        var tool = _context.UI.InGame.Add(new NpcDialogueEditor(_editing)); string gameLanguage = Localization.Language;
        try
        {
            tool.UpdateLanguageStrip(true); Assert.NotNull(tool.LanguageDropdown); tool.LanguageDropdown!.Pick(1);
            Assert.Equal(gameLanguage, Localization.Language); Assert.Equal("ru-RU", _editing.Language);
            Assert.Equal("Russian greeting", _panel.Get("full_text").AsString);
            Assert.Equal("Russian trade", view.Buttons.First(b => b.Key == "trade").Label);
            Assert.Equal("Russian goodbye", view.Buttons.First(b => b.Key == "leave").Label);
            trade = view.Buttons.First(b => b.Key == "trade");
            trade.Button.Set("x", 20); trade.Button.Set("y", 20); trade.Button.Set("textWidth", 100); trade.Button.Set("textHeight", 20);
            trade.Button.Set("guiVisibleAreaBorderRight", 500); trade.Button.Set("guiVisibleAreaBorderBottom", 400);
            Assert.True(tool.EditAt(view, 25, 25));
            tool.ContextPopup!.Children.OfType<UIButton>().Single(b => b.Text == Localization.Get("npc_editor.edit_text")).RaiseClick();
            Assert.Equal("Russian trade", tool.InlineInput!.Text);
            Keyboard.Typed = "Edited Russian trade"; Input.PressedKeys.Add(Keyboard.Enter); tool.InlineInput.RunUpdate(0.1); Input.PressedKeys.Clear();
            Assert.Equal("Edited Russian trade", _editing.Find(view, "option/trade")!.Text["ru-RU"]);
            Assert.Equal("Trade", _editing.Find(view, "option/trade")!.Text[gameLanguage]);
            tool.UpdateLanguageStrip(true); tool.LanguageDropdown.Pick(0);
            Assert.Null(_editing.PreviewLanguage); Assert.Equal("Welcome", _panel.Get("full_text").AsString);
            Assert.Equal("Trade", view.Buttons.First(b => b.Key == "trade").Label);
            Assert.DoesNotContain("scr_dialogue_advance", Calls);
        }
        finally { tool.Close(); UITextBox.ReleaseFocus(); }
    }
    [Fact]
    public void Native_translation_preview_reads_the_table_without_changing_game_language()
    {
        var view = _editing.Current!; var leave = view.Buttons.First(b => b.Key == "leave");
        using var row = GmArray.Create(18);
        row[0] = "custom_leave"; row[7] = "Goodbye"; row[6] = "Russian farewell";
        using var rows = GmArray.From(new GmValue[] { row }); Globals["npc_lines"] = rows;
        _editing.SetPreviewLanguage(view, "ru-RU");
        Assert.Equal("Russian farewell", view.Buttons.First(b => b.Key == "leave").Label); Assert.Equal("en-US", Localization.Language);
        _editing.SetPreviewLanguage(view, null); Assert.Equal("Goodbye", view.Buttons.First(b => b.Key == "leave").Label);
        _editing.SetPreviewLanguage(view, "ru-RU");
        var tool = _context.UI.InGame.Add(new NpcDialogueEditor(_editing)); tool.UpdateLanguageStrip(false);
        Assert.Null(_editing.PreviewLanguage); Assert.Equal("Goodbye", view.Buttons.First(b => b.Key == "leave").Label);
    }
    [Fact]
    public void Built_in_exit_is_deferred_and_consumes_the_response_without_resuming_native_code()
    {
        var view = _editing.Current!; var button = view.Buttons.First(b => b.Key == "trade");
        _editing.BindAction(view, button, "stoneforge:exit_dialogue");
        int resumed = 0; _world.OnEventPerform = (_, _, _) => resumed++;
        Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", button.Button.Id)); Assert.True(_panel.Exists);
        RunFrame(); Assert.False(_panel.Exists); Assert.Equal(0, resumed);
    }
    [Fact]
    public void Built_in_trade_clicks_the_native_service_once_and_rechecks_its_lock()
    {
        var view = _editing.Current!; var button = view.Buttons.First(b => b.Key == "trade");
        _panel.Set("is_activate", true); button.Button.Set("is_activate", true); button.Button.Set("canPress", true);
        _editing.BindAction(view, button, "stoneforge:open_trade");
        Assert.True(DialogOptions.IsEnabled(_context.Id, "stoneforge:open_trade", Instance.FromId(100002), _panel));
        int clicked = 0;
        _world.OnEventPerform = (id, kind, ev) =>
        { Assert.Equal(button.Button.Id, id); Assert.Equal(6, kind); Assert.Equal(4, ev); Assert.Equal(0, RunBefore("gml_Object_o_contract_button_Mouse_4", id)); clicked++; };
        Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", button.Button.Id)); RunFrame(); Assert.Equal(1, clicked);
        Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", button.Button.Id)); button.Button.Set("canPress", false);
        RunFrame(); Assert.Equal(1, clicked);
        Assert.False(DialogOptions.IsEnabled(_context.Id, "stoneforge:open_trade", Instance.FromId(100002), _panel));
        Assert.False(DialogOptions.IsEnabled(_context.Id, "stoneforge:rent_room", Instance.FromId(100002), _panel));
    }
    [Fact]
    public void Built_in_bindings_persist_but_unknown_framework_ids_and_other_mods_are_rejected()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "builtin-edits-" + Guid.NewGuid().ToString("N"));
        try
        {
            var view = _editing.Current!; var button = view.Buttons.First(b => b.Key == "trade");
            var editing = new NpcDialogueEditing(_context, Path.Combine(folder, "npcs.json"));
            Assert.Contains(editing.CodeOptions, e => e.Id == "stoneforge:exit_dialogue" && e.BuiltIn);
            Assert.Throws<ArgumentException>(() => editing.ResolveAction("stoneforge:unknown"));
            Assert.Throws<ArgumentException>(() => editing.ResolveAction("othermod:kill"));
            editing.BindAction(view, button, "stoneforge:exit_dialogue"); editing.Save();
            var loaded = new NpcDialogueEditing(_context, Path.Combine(folder, "npcs.json")); loaded.Load();
            Assert.Equal("stoneforge:exit_dialogue", loaded.ActionFor(view, button));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private static bool _showConditional, _enableConditional;
    private static class ConditionalActions
    {
        [DialogOption("conditional")]
        public static void Run() => _codeCalls++;
        [DialogCondition("available")]
        private static DialogConditionResult Available(DialogOptionContext c) => c.Speaker.Id != 100002 || !_showConditional ? DialogConditionResult.Hidden :
            _enableConditional ? DialogConditionResult.Enabled : DialogConditionResult.Visible;
    }
    [Fact]
    public void Conditional_options_refresh_hidden_and_disabled_states_and_recheck_a_queued_click()
    {
        _codeCalls = 0; _showConditional = false; _enableConditional = true;
        DialogOptions.Register(_context, new[] { typeof(ConditionalActions) });
        DialogConditions.Register(_context, new[] { typeof(ConditionalActions) });
        var view = _editing.Current!;
        var topic = _editing.AddCodeRelative(view, "leave", false, "npc_editor_test:conditional");
        _editing.Refresh(view);
        _editing.BindCondition(view, view.Buttons.Single(b => b.Key == topic.Fragment), "available");
        _editing.Refresh(view); Assert.DoesNotContain(view.Buttons, b => b.Key == topic.Fragment);
        Assert.Contains(topic.Fragment, view.Options); // Hidden responses can return on refresh.
        _showConditional = true; _enableConditional = false; _editing.Refresh(view);
        Assert.Contains(view.Buttons, b => b.Key == topic.Fragment); Assert.Contains(topic.Fragment, view.ConditionLocks);
        Game.CallScript("scr_dialogue_advance", _panel, topic.Fragment); Assert.Equal(0, _codeCalls);
        _enableConditional = true; _editing.Refresh(view); Assert.DoesNotContain(topic.Fragment, view.ConditionLocks);
        var option = view.Buttons.Single(b => b.Key == topic.Fragment);
        Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", option.Button.Id));
        _showConditional = false; RunFrame(); Assert.Equal(0, _codeCalls);
        _showConditional = true; Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", option.Button.Id));
        RunFrame(); Assert.Equal(1, _codeCalls);
    }
    [Fact]
    public void Independent_conditions_gate_vanilla_responses_poll_changes_and_reload_per_NPC()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "npc-conditions-" + Guid.NewGuid().ToString("N"));
        try
        {
            _showConditional = true; _enableConditional = false;
            DialogConditions.Register(_context, new[] { typeof(ConditionalActions) });
            var editing = new NpcDialogueEditing(_context, folder); var view = _editing.Current!;
            Assert.Throws<ArgumentException>(() => editing.BindCondition(view, view.Buttons.First(b => b.Key == "trade"), "other_mod:available"));
            editing.BindCondition(view, view.Buttons.First(b => b.Key == "trade"), "available"); editing.Save();
            Assert.Contains("npc_editor_test:available", File.ReadAllText(Path.Combine(folder, "npc_osbrook_smith.json")));
            Hooks.RemoveMod(_context.Id);
            var loaded = new NpcDialogueEditing(_context, folder); loaded.Load(); loaded.Install(); Render(); view = loaded.Current!;
            Assert.Equal("npc_editor_test:available", loaded.ConditionFor(view, "trade"));
            // An unavailable condition stays disabled until its mod method registers again.
            Assert.False(view.Buttons.First(b => b.Key == "trade").Button.Get("canPress").AsBool);
            DialogConditions.Register(_context, new[] { typeof(ConditionalActions) });
            _panel.Set("is_activate", true); _enableConditional = true; loaded.TickConditions(true);
            Assert.True(view.Buttons.First(b => b.Key == "trade").Button.Get("canPress").AsBool);
            _enableConditional = false;
            Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", view.Buttons.First(b => b.Key == "trade").Button.Id));
            int nativeRuns = 0; _scripts.Add("scr_dialogue_advance", _ => { nativeRuns++; return GmValue.Undefined; });
            Game.CallScript("scr_dialogue_advance", _panel, "trade"); Assert.Equal(0, nativeRuns);
            loaded.TickConditions(true); Assert.Contains("trade", view.ConditionLocks);
            _showConditional = false; loaded.TickConditions(true);
            Assert.DoesNotContain(view.Buttons, b => b.Key == "trade"); Assert.Contains("trade", view.Options);
            _showConditional = true; _enableConditional = true; loaded.TickConditions(true);
            Assert.Contains(view.Buttons, b => b.Key == "trade");
            loaded.BindCondition(view, view.Buttons.First(b => b.Key == "trade"), null); loaded.Refresh(view); loaded.Save();
            var removed = new NpcDialogueEditing(_context, folder); removed.Load(); Assert.Null(removed.ConditionFor(view, "trade"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Condition_picker_searches_binds_and_switches_to_remove_in_the_context_menu()
    {
        _showConditional = true; _enableConditional = true;
        DialogConditions.Register(_context, new[] { typeof(ConditionalActions) });
        KeepGlobalWrites = true; Input = new FakeInput { KeyboardOnly = true };
        var view = _editing.Current!; var render = ContextMenus.InstanceOf(_panel.Get("render"));
        _panel.Set("is_activate", true); render.Set("surfaceDraw", true);
        render.Set("guiVisibleAreaBorderRight", 500); render.Set("guiVisibleAreaBorderBottom", 400);
        var tool = _context.UI.InGame.Add(new NpcDialogueEditor(_editing));
        IEnumerable<UIElement> Elements(UIElement root) => root.Children.SelectMany(c => new[] { c }.Concat(Elements(c)));
        void OpenMenu()
        {
            var button = view.Buttons.First(b => b.Key == "trade").Button;
            button.Set("x", 20); button.Set("y", 20); button.Set("textWidth", 100); button.Set("textHeight", 20);
            button.Set("guiVisibleAreaBorderRight", 500); button.Set("guiVisibleAreaBorderBottom", 400);
            Assert.True(tool.EditAt(view, 25, 25));
        }
        try
        {
            OpenMenu(); tool.ContextPopup!.Children.OfType<UIButton>().Single(b => b.Text == Localization.Get("npc_editor.condition_add")).RaiseClick();
            var input = tool.ContextPopup!.Children.OfType<UITextBox>().Single();
            Keyboard.Typed = "missing"; input.RunUpdate(0.1);
            Assert.DoesNotContain(Elements(tool.ContextPopup).OfType<UIButton>(), b => b.Text == "npc_editor_test:available");
            Keyboard.Typed = "available"; input.RunUpdate(0.1);
            Elements(tool.ContextPopup).OfType<UIButton>().Single(b => b.Text == "npc_editor_test:available").RaiseClick();
            Assert.Null(tool.ContextPopup); Assert.Equal("npc_editor_test:available", _editing.ConditionFor(view, "trade"));
            Assert.DoesNotContain("scr_dialogue_advance", Calls);
            OpenMenu(); Assert.DoesNotContain(tool.ContextPopup!.Children.OfType<UIButton>(), b => b.Text == Localization.Get("npc_editor.condition_add"));
            tool.ContextPopup.Children.OfType<UIButton>().Single(b => b.Text == Localization.Get("npc_editor.condition_remove")).RaiseClick();
            Assert.Null(_editing.ConditionFor(view, "trade"));
        }
        finally { tool.Close(); UITextBox.ReleaseFocus(); }
    }
    [Fact]
    public void Added_labels_and_translations_refresh_immediately_without_reopening_or_running_actions()
    {
        var view = _editing.Current!;
        var topic = _editing.AddRelative(view, "trade", true, "New option", "A reply");
        _editing.Refresh(view);
        Assert.Equal("New option", view.Buttons.Single(b => b.Key == topic.Fragment).Label);
        topic.Label[Localization.Language] = "Updated option";
        _editing.Refresh(view);
        Assert.Equal("Updated option", view.Buttons.Single(b => b.Key == topic.Fragment).Label);
        _editing.SetTranslation(view, view.Buttons.Single(b => b.Key == topic.Fragment), Localization.Language, "Translated option");
        _editing.RefreshLanguage(view);
        Assert.Equal("Translated option", view.Buttons.Single(b => b.Key == topic.Fragment).Label);
        Assert.Equal(1, view.Buttons.Count(b => b.Key == topic.Fragment));
        Assert.Null(Dialogues.Active); Assert.DoesNotContain("scr_dialogue_advance", Calls);
    }
    [Fact]
    public void Restore_dialogue_removes_own_additions_text_order_and_deletions_and_keeps_other_NPC_edits()
    {
        var view = _editing.Current!;
        var other = new NpcDialogueEditing.View { Panel = _panel, Npc = "osbrook_herbalist", Root = "herbalist" };
        _editing.Set(other, "line/greeting", "Other NPC edit", null);
        _editing.SetTranslation(view, null, Localization.Language, "Edited greeting");
        _editing.Set(view, "option/trade", "Shop", 2);
        var topic = _editing.AddRelative(view, "trade", true, "New option", "Reply");
        _editing.Refresh(view); _editing.DeleteOption(view, "leave"); _editing.Refresh(view);
        _editing.RestoreDialogue(view);
        Assert.Equal(new[] { "trade", "leave" }, view.Buttons.Select(b => b.Key));
        Assert.Equal("Trade", view.Buttons[0].Label);
        Assert.Equal("Welcome", _panel.Get("full_text").AsString);
        Assert.False(_editing.HasDeleted(view)); Assert.Null(_editing.Authored(view, topic.Fragment));
        Assert.Equal("Other NPC edit", _editing.Text(other, "line/greeting"));
        Assert.DoesNotContain("scr_dialogue_advance", Calls);
    }
    [Fact]
    public void Restore_original_removes_translations_and_order_for_only_the_selected_entry()
    {
        var view = _editing.Current!; var option = view.Buttons.First(b => b.Key == "trade");
        _editing.SetTranslation(view, option, "en-US", "Shop");
        _editing.SetTranslation(view, option, "ru-RU", "Translated shop");
        _editing.SetPosition(view, "trade", 2);
        _editing.SetTranslation(view, null, Localization.Language, "Edited greeting");
        _editing.RefreshLanguage(view);
        _editing.RestoreOriginal(view, option);
        Assert.Equal("Trade", view.Buttons.First(b => b.Key == "trade").Label);
        Assert.Null(_editing.Find(view, "option/trade"));
        Assert.Equal("Edited greeting", _panel.Get("full_text").AsString);
    }
    private static DialogOptionContext? _codeContext;
    private static class CodeActions
    {
        [DialogOption("work")]
        public static void Work(DialogOptionContext context) { _codeCalls++; _codeContext = context; }
        [DialogOption("destroy")]
        public static void DestroyPanel(DialogOptionContext context) { _codeCalls++; context.Panel.Destroy(); }
    }
    [Fact]
    public void Native_click_defers_destructive_callback_until_click_returns_and_never_resumes_a_destroyed_panel()
    {
        _codeCalls = 0; DialogOptions.Register(_context, new[] { typeof(CodeActions) });
        var view = _editing.Current!; var button = view.Buttons.First(b => b.Key == "trade");
        _editing.BindAction(view, button, "destroy");
        int resumed = 0; _world.OnEventPerform = (_, _, _) => resumed++;
        Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", button.Button.Id));
        Assert.Equal(0, _codeCalls); Assert.True(_panel.Exists);
        RunFrame();
        Assert.Equal(1, _codeCalls); Assert.False(_panel.Exists); Assert.Equal(0, resumed);
        RunFrame(); Assert.Equal(1, _codeCalls);
    }
    [Fact]
    public void Native_click_resumes_original_button_once_after_a_non_destructive_callback_without_running_code_twice()
    {
        _codeCalls = 0; DialogOptions.Register(_context, new[] { typeof(CodeActions) });
        var view = _editing.Current!; var button = view.Buttons.First(b => b.Key == "trade");
        _editing.BindAction(view, button, "work");
        int originals = 0; _scripts.Add("scr_dialogue_advance", _ => { originals++; return GmValue.Undefined; });
        _world.OnEventPerform = (id, kind, ev) =>
        {
            Assert.Equal(button.Button.Id, id); Assert.Equal(6, kind); Assert.Equal(4, ev);
            Assert.Equal(0, RunBefore("gml_Object_o_contract_button_Mouse_4", id));
            Game.CallScript("scr_dialogue_advance", _panel, "trade");
        };
        Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", button.Button.Id));
        Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", button.Button.Id)); // Deduplicate pending clicks.
        Assert.Equal(0, _codeCalls); Assert.Equal(0, originals);
        RunFrame(); Assert.Equal(1, _codeCalls); Assert.Equal(1, originals);
        RunFrame(); Assert.Equal(1, _codeCalls); Assert.Equal(1, originals);
    }
    [Fact]
    public void Bound_response_runs_code_on_selection_and_preserves_its_original_action()
    {
        _codeCalls = 0;
        DialogOptions.Register(_context, new[] { typeof(CodeActions) });
        var view = _editing.Current!; var option = view.Buttons.First(b => b.Key == "trade");
        int nativeRuns = 0; _scripts.Add("scr_dialogue_advance", _ => { nativeRuns++; return GmValue.Undefined; });
        _editing.BindAction(view, option, "work");
        Assert.Equal("npc_editor_test:work", _editing.ActionFor(view, option));
        Assert.Throws<ArgumentException>(() => _editing.BindAction(view, option, "other:work"));
        _editing.Refresh(view); _editing.RefreshLanguage(view); Assert.Equal(0, _codeCalls);
        _editing.EditingPanel = _panel;
        Game.CallScript("scr_dialogue_advance", _panel, "trade"); Assert.Equal(0, _codeCalls); Assert.Equal(0, nativeRuns);
        _editing.EditingPanel = default;
        Game.CallScript("scr_dialogue_advance", _panel, "trade"); Assert.Equal(1, _codeCalls); Assert.Equal(1, nativeRuns);
        _editing.BindAction(view, view.Buttons.First(b => b.Key == "trade"), null);
        Game.CallScript("scr_dialogue_advance", _panel, "trade"); Assert.Equal(1, _codeCalls); Assert.Equal(2, nativeRuns);
    }
    [Fact]
    public void Ordinary_response_binding_survives_saved_document_reload()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "bound-edits-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "npcs.json");
        try
        {
            _codeCalls = 0;
            DialogOptions.Register(_context, new[] { typeof(CodeActions) });
            var view = _editing.Current!; var option = view.Buttons.First(b => b.Key == "trade");
            var editing = new NpcDialogueEditing(_context, path); editing.BindAction(view, option, "work"); editing.Save();
            var loaded = new NpcDialogueEditing(_context, path); loaded.Load();
            Assert.Equal("npc_editor_test:work", loaded.ActionFor(view, option));
            loaded.Install(); Game.CallScript("scr_dialogue_advance", _panel, "trade"); Assert.Equal(1, _codeCalls);
            loaded.RestoreDeleted(view); Assert.Equal("npc_editor_test:work", loaded.ActionFor(view, option));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Trigger_code_menu_is_visible_without_functions_and_picker_search_binds_without_executing()
    {
        _codeCalls = 0; KeepGlobalWrites = true; Input = new FakeInput { KeyboardOnly = true };
        var view = _editing.Current!; var button = view.Buttons.First(b => b.Key == "trade").Button;
        _panel.Set("is_activate", true);
        var render = ContextMenus.InstanceOf(_panel.Get("render")); render.Set("surfaceDraw", true);
        render.Set("guiVisibleAreaBorderLeft", 0); render.Set("guiVisibleAreaBorderTop", 0);
        render.Set("guiVisibleAreaBorderRight", 400); render.Set("guiVisibleAreaBorderBottom", 400);
        button.Set("x", 20); button.Set("y", 20); button.Set("textWidth", 100); button.Set("textHeight", 20);
        button.Set("guiVisibleAreaBorderLeft", 0); button.Set("guiVisibleAreaBorderTop", 0);
        button.Set("guiVisibleAreaBorderRight", 400); button.Set("guiVisibleAreaBorderBottom", 400);
        var tool = _context.UI.InGame.Add(new NpcDialogueEditor(_editing));
        IEnumerable<UIElement> Descendants(UIElement root) => root.Children.SelectMany(c => new[] { c }.Concat(Descendants(c)));
        void OpenPicker()
        {
            Assert.True(tool.EditAt(view, 25, 25));
            tool.ContextPopup!.Children.OfType<UIButton>().Single(b => b.Text == Localization.Get("npc_editor.trigger_code")).RaiseClick();
        }
        try
        {
            OpenPicker(); Assert.Contains(Descendants(tool.ContextPopup!).OfType<UIButton>(), b => b.Text == Localization.Get("dialog_trigger.exit_dialogue") + " (StoneForge)");
            tool.Close(); DialogOptions.Register(_context, new[] { typeof(CodeActions) }); OpenPicker();
            var input = tool.ContextPopup!.Children.OfType<UITextBox>().Single();
            Keyboard.Typed = "nonexistent"; input.RunUpdate(0.1);
            Assert.DoesNotContain(Descendants(tool.ContextPopup!).OfType<UIButton>(), b => b.Text == "npc_editor_test:work");
            Keyboard.Typed = "work"; input.RunUpdate(0.1);
            Descendants(tool.ContextPopup!).OfType<UIButton>().Single(b => b.Text == "npc_editor_test:work").RaiseClick();
            Assert.Null(tool.ContextPopup); Assert.Equal(0, _codeCalls);
            Assert.Equal("npc_editor_test:work", _editing.ActionFor(view, view.Buttons.First(b => b.Key == "trade")));
        }
        finally { tool.Close(); UITextBox.ReleaseFocus(); }
    }
    [Fact]
    public void Code_binding_reloads_and_resolves_the_new_mod_callback()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "code-edits-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "npcs.json");
        try
        {
            _codeCalls = 0;
            DialogOptions.Register(_context, new[] { typeof(CodeActions) });
            var service = new NpcDialogueEditing(_context, path);
            var topic = service.AddCodeRelative(_editing.Current!, "leave", false, "npc_editor_test:work");
            service.Save();
            Hooks.RemoveMod(_context.Id);
            var reloadedMod = new ModContext(_context.Id);
            DialogOptions.Register(reloadedMod, new[] { typeof(CodeActions) });
            var loaded = new NpcDialogueEditing(reloadedMod, path); loaded.Load(); loaded.Install(); Render();
            Assert.Contains(loaded.Current!.Buttons, b => b.Key == topic.Fragment);
            Assert.Equal(0, _codeCalls);
            Game.CallScript("scr_dialogue_advance", _panel, topic.Fragment);
            Assert.Equal(1, _codeCalls); Assert.Same(reloadedMod, _codeContext!.Mod);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Inserted_code_option_runs_only_on_selection_and_keeps_its_binding_in_json()
    {
        _codeCalls = 0; _codeContext = null;
        DialogOptions.Register(_context, new[] { typeof(CodeActions) });
        var topic = _editing.AddCodeRelative(_editing.Current!, "trade", true, "npc_editor_test:work");
        _editing.Refresh(_editing.Current!);
        Assert.Equal(new[] { "trade", topic.Fragment, "leave" }, _editing.Current!.Buttons.Select(b => b.Key));
        Assert.Equal(0, _codeCalls);
        Assert.Contains("\"ActionId\": \"npc_editor_test:work\"", _editing.Serialize());
        _editing.EditingPanel = _panel;
        Game.CallScript("scr_dialogue_advance", _panel, topic.Fragment);
        Assert.Equal(0, _codeCalls);
        _editing.EditingPanel = default;
        Game.CallScript("scr_dialogue_advance", _panel, topic.Fragment);
        Assert.Equal(1, _codeCalls);
        Assert.Equal(100002, _codeContext!.Speaker.Id);
        Assert.Equal(_panel.Id, _codeContext.Panel.Id);
        Assert.Null(Dialogues.Active);
        _editing.Refresh(_editing.Current!);
        Assert.Equal(1, _codeCalls);
        DialogOptions.RemoveMod(_context.Id);
        _editing.Refresh(_editing.Current!);
        Assert.DoesNotContain(_editing.Current!.Buttons, b => b.Key == topic.Fragment);
    }
    [Fact]
    public void NPC_strip_controls_apply_native_option_text_and_prevent_losing_a_draft()
    {
        var tool = _context.UI.InGame.Add(new NpcDialogueEditor(_editing)); tool.Open(_editing.Current!);
        IEnumerable<UIElement> Elements(UIElement parent)
        { foreach (var child in parent.Children) { yield return child; foreach (var nested in Elements(child)) yield return nested; } }
        try
        {
            KeepGlobalWrites = true; tool.Select("trade");
            var text = Elements(tool).OfType<UITextBox>().First();
            text.Focus(); Keyboard.Typed = "Shop here"; text.RunUpdate(0.1);
            var picker = Elements(tool).OfType<UIDropdown>().First(); picker.Pick(0);
            Assert.Equal(1, picker.SelectedIndex);
            Elements(tool).OfType<UIButton>().First(b => b.Text == Localization.Get("npc_editor.apply")).RaiseClick();
            Assert.Equal("Shop here", _editing.Current!.Buttons.First(b => b.Key == "trade").Label);
            Assert.False(UIWindow.AnyOpen); // The native NPC window is the conversation.
        }
        finally { tool.Close(); UITextBox.ReleaseFocus(); }
        Assert.False(_editing.EditingPanel.Exists);
    }
    [Fact]
    public void Right_click_menu_edits_and_moves_native_text_without_advancing_and_respects_scroll_clipping()
    {
        Ds = new FakeDs(); var map = new DsMap(Ds.NewMap()); map["height"] = 30;
        var view = _editing.Current!; var render = ContextMenus.InstanceOf(_panel.Get("render"));
        _panel.Set("is_activate", true); render.Set("surfaceDraw", true); render.Set("drawScale", 2);
        render.Set("x", 100); render.Set("y", 50); render.Set("surfaceOffsetX", 10); render.Set("surfaceOffsetY", 20);
        render.Set("contentX", 5); render.Set("contentY", -40); render.Set("historyHeight", 80);
        render.Set("portraitWidth", 40); render.Set("speakerSpaceX", 5); render.Set("fontDmgHeight", 10);
        render.Set("speakerSpaceY", 5); render.Set("speakerTextWidth", 100); render.Set("speakerTextMap", map.Id);
        void Clip(Instance target, int bottom)
        { target.Set("guiVisibleAreaBorderLeft", 100); target.Set("guiVisibleAreaBorderTop", 100);
          target.Set("guiVisibleAreaBorderRight", 500); target.Set("guiVisibleAreaBorderBottom", bottom); }
        Clip(render, 400);
        var trade = view.Buttons.First(b => b.Key == "trade").Button;
        trade.Set("x", 220); trade.Set("y", 250); trade.Set("textSpaceX", 2); trade.Set("textSpaceY", 1);
        trade.Set("textWidth", 60); trade.Set("textHeight", 20); Clip(trade, 265);
        var tool = _context.UI.InGame.Add(new NpcDialogueEditor(_editing));
        void Click(string key) => tool.ContextPopup!.Children.OfType<UIButton>().Single(b => b.Text == Localization.Get("npc_editor." + key)).RaiseClick();
        UITextBox TextBox() => tool.InlineInput!;
        try
        {
            KeepGlobalWrites = true; Input = new FakeInput { KeyboardOnly = true };
            Globals["guiMouseX"] = 0; Globals["guiMouseY"] = 0;
            Assert.False(tool.EditAt(view, 230, 268)); // Scrolled-off response text.
            Assert.False(tool.Visible);
            Assert.True(tool.EditAt(view, 230, 255));
            Assert.False(tool.Visible); Assert.False(UITextBox.AnyFocused);
            Assert.Equal(10, tool.ContextPopup!.Children.OfType<UIButton>().Count());
            Assert.False(tool.ContextPopup.Children.OfType<UIButton>().Single(b => b.Text == Localization.Get("npc_editor.move_up")).Enabled);
            Click("edit_text"); Assert.Equal("Trade", TextBox().Text); Assert.True(TextBox().IsFocused);
            Assert.Null(tool.ContextPopup); Assert.Equal(222, TextBox().X); Assert.Equal(251, TextBox().Y);
            Keyboard.Typed = "Trade "; Input.PressedKeys.Add(Keyboard.Space); TextBox().RunUpdate(0.1); Input.PressedKeys.Clear();
            Assert.Equal("Trade ", TextBox().Text); Assert.True(TextBox().IsFocused);
            Assert.Equal("Trade", view.Buttons.First(b => b.Key == "trade").Label); // No save on Space.
            Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Step_1", trade.Id));
            Assert.Equal(1, RunBefore("gml_Object_o_dialog_button_Step_0", trade.Id));
            Keyboard.Typed = "Discarded"; TextBox().RunUpdate(0.1);
            Input.PressedKeys.Add(Keyboard.Escape); TextBox().RunUpdate(0.1); Input.PressedKeys.Clear();
            Assert.Equal("Trade", view.Buttons.First(b => b.Key == "trade").Label);
            Assert.Null(tool.ContextPopup); Assert.False(UITextBox.AnyFocused); Assert.False(_editing.EditingPanel.Exists);
            Assert.Equal(0, RunBefore("gml_Object_o_contract_button_Step_1", trade.Id));
            Assert.Equal(0, RunBefore("gml_Object_o_dialog_button_Step_0", trade.Id));
            Assert.DoesNotContain("scr_dialogue_advance", Calls);
            string longLine = new string('a', 200) + "\nSecond line";
            _panel.Set("full_text", longLine);
            Assert.True(tool.EditAt(view, 230, 185)); // Surface offsets + history + scale.
            Assert.Equal(4, tool.ContextPopup!.Children.OfType<UIButton>().Count());
            Click("edit_text");
            Assert.Equal(longLine, TextBox().Text); Assert.True(TextBox().IsFocused);
            Assert.Equal(210, TextBox().X); Assert.Equal(180, TextBox().Y);
            Keyboard.Typed = "Edited greeting"; Input.PressedKeys.Add(Keyboard.Enter); TextBox().RunUpdate(0.1); Input.PressedKeys.Clear();
            Assert.Equal("Edited greeting", _panel.Get("full_text").AsString); Assert.False(tool.Visible);
            trade = view.Buttons.First(b => b.Key == "trade").Button;
            trade.Set("x", 220); trade.Set("y", 250); trade.Set("textWidth", 60); trade.Set("textHeight", 20); Clip(trade, 265);
            Assert.True(tool.EditAt(view, 230, 255)); Click("move_down");
            Assert.Equal(new[] { "leave", "trade" }, view.Buttons.Select(b => b.Key));
            Assert.Null(_editing.Text(view, "option/trade")); // Moving never freezes a dynamic label.
            Assert.False(tool.EditAt(view, 120, 185)); // Portrait is not the NPC line.
        }
        finally { tool.Close(); UITextBox.ReleaseFocus(); }
    }
    [Fact]
    public void Entry_translations_refresh_immediately_preserve_other_languages_and_reload_with_the_mod()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "translation-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "npcs.json"); string language = Localization.Language;
        var context = new ModContext("translation_test"); var service = new NpcDialogueEditing(context, path);
        try
        {
            Localization.SetLanguage("en-US"); var view = _editing.Current!;
            service.SetTranslation(view, view.Buttons.First(b => b.Key == "trade"), "de-DE", "Handeln");
            service.SetTranslation(view, null, "en-US", "English greeting");
            service.SetTranslation(view, null, "de-DE", "Deutsche Begrüßung"); service.Save(); service.Install();
            service.RefreshLanguage(view);
            Assert.Equal("en-US", Localization.Language); Assert.Equal("English greeting", _panel.Get("full_text").AsString);
            Assert.Equal("Trade", service.Current!.Buttons.First(b => b.Key == "trade").Label);
            Localization.SetLanguage("de-DE"); service.RefreshLanguage(service.Current);
            Assert.Equal("Deutsche Begrüßung", _panel.Get("full_text").AsString);
            var option = service.Current.Buttons.First(b => b.Key == "trade"); Assert.Equal("Handeln", option.Label);
            service.SetTranslation(service.Current, option, "de-DE", "Neuer Handel"); service.RefreshLanguage(service.Current); service.Save();
            Assert.Equal("Neuer Handel", service.Current.Buttons.First(b => b.Key == "trade").Label);
            Hooks.RemoveMod(context.Id);
            var reload = new NpcDialogueEditing(context, path); reload.Load(); reload.Install(); Render();
            Assert.Equal("Neuer Handel", reload.Current!.Buttons.First(b => b.Key == "trade").Label);
            Localization.SetLanguage("fr-FR"); reload.RefreshLanguage(reload.Current);
            Assert.Equal("Trade", reload.Current.Buttons.First(b => b.Key == "trade").Label);
            Assert.Equal("English greeting", _panel.Get("full_text").AsString);
        }
        finally { Localization.SetLanguage(language); Hooks.RemoveMod(context.Id); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Add_above_and_below_keep_the_current_order_and_authored_options_can_be_deleted()
    {
        var view = _editing.Current!;
        _editing.SetPosition(view, "trade", 2); _editing.Refresh(view);
        Assert.Equal(new[] { "leave", "trade" }, view.Buttons.Select(o => o.Key));
        var above = _editing.AddRelative(view, "trade", false, "Above", "Above reply"); _editing.Refresh(view);
        Assert.Equal(new[] { "leave", above.Fragment, "trade" }, view.Buttons.Select(o => o.Key));
        var below = _editing.AddRelative(view, "trade", true, "Below", "Below reply"); _editing.Refresh(view);
        Assert.Equal(new[] { "leave", above.Fragment, "trade", below.Fragment }, view.Buttons.Select(o => o.Key));
        _editing.DeleteOption(view, above.Fragment); _editing.Refresh(view);
        Assert.Null(_editing.Authored(view, above.Fragment));
        Assert.Equal(new[] { "leave", "trade", below.Fragment }, view.Buttons.Select(o => o.Key));
        Assert.DoesNotContain(view.Options, key => key == above.Fragment);
    }
    [Fact]
    public void Deleted_native_options_persist_and_restore_without_mutating_the_games_source_array()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "delete-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "npcs.json"); var context = new ModContext("delete_test");
        var service = new NpcDialogueEditing(context, path);
        try
        {
            var view = _editing.Current!; service.DeleteOption(view, "trade"); service.Save(); service.Install();
            using var original = GmArray.From(new GmValue[] { "trade", "leave" });
            Game.CallScript("dialogue_create_option_buttons", _panel, original, false, false);
            Assert.Equal(new[] { "trade", "leave" }, original.Select(v => v.AsString));
            Assert.Equal("leave", service.Current!.Buttons.Single().Key);
            Assert.Throws<InvalidOperationException>(() => service.DeleteOption(service.Current, "leave"));
            Hooks.RemoveMod(context.Id);
            var loaded = new NpcDialogueEditing(context, path); loaded.Load(); loaded.Install(); Render();
            Assert.True(loaded.HasDeleted(loaded.Current!)); Assert.Equal("leave", loaded.Current!.Buttons.Single().Key);
            loaded.RestoreDeleted(loaded.Current); loaded.Refresh(loaded.Current);
            Assert.Equal(new[] { "trade", "leave" }, loaded.Current.Buttons.Select(o => o.Key));
            Assert.Equal("trade", loaded.Current.Buttons.First().Button.Get("func").AsString);
            Assert.False(loaded.HasDeleted(loaded.Current));
        }
        finally { Hooks.RemoveMod(context.Id); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Dev_mode_is_exclusive_toggleable_and_removed_with_its_mod()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "dev-tests-" + Guid.NewGuid().ToString("N"));
        var first = new ModContext("dev_first", Path.Combine(folder, "first"));
        var second = new ModContext("dev_second", Path.Combine(folder, "second"));
        try
        {
            DialogueEditor.Install(first); DialogueEditor.Install(second);
            Assert.True(DialogueEditor.CanEnable(first.Id)); Assert.True(DialogueEditor.CanEnable(second.Id));
            DialogueEditor.Toggle(first.Id);
            Assert.Equal(first.Id, DialogueEditor.DevMod); Assert.False(DialogueEditor.CanEnable(second.Id));
            DialogueEditor.Toggle(second.Id); Assert.Equal(first.Id, DialogueEditor.DevMod);
            DialogueEditor.Toggle(first.Id); Assert.Null(DialogueEditor.DevMod);
            Assert.True(DialogueEditor.CanEnable(second.Id)); DialogueEditor.Toggle(second.Id);
            DialogueEditor.Remove(second.Id); Hooks.RemoveMod(second.Id);
            Assert.Null(DialogueEditor.DevMod); Assert.False(DialogueEditor.CanEnable(second.Id));
        }
        finally { DialogueEditor.Remove(first.Id); DialogueEditor.Remove(second.Id); Hooks.RemoveMod(first.Id); Hooks.RemoveMod(second.Id); }
    }
    [Fact]
    public void NPC_edits_load_only_with_the_owning_mod_and_never_write_to_another_mod()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "scope-tests-" + Guid.NewGuid().ToString("N"));
        var first = new ModContext("scope_first", Path.Combine(folder, "first"));
        var second = new ModContext("scope_second", Path.Combine(folder, "second"));
        string path = first.Files.Resolve("Dialogue/npcs.json", true);
        var service = new NpcDialogueEditing(first, path);
        try
        {
            var view = _editing.Current!;
            service.SetOption(view, view.Buttons.First(b => b.Key == "trade"), "My shop", 2); service.Save();
            Assert.True(first.Files.Exists("Dialogue/npc_osbrook_smith.json")); Assert.False(second.Files.Exists("Dialogue/npc_osbrook_smith.json"));
            var empty = new NpcDialogueEditing(second, second.Files.Resolve("Dialogue/npcs.json", true)); empty.Load();
            Assert.Null(empty.Text(view, NpcDialogueEditing.VariantField("option", "trade", "Trade")));
            service.Install(); Render(); Assert.Equal("My shop", view.Buttons.First(b => b.Key == "trade").Label);
            Hooks.RemoveMod(first.Id); Render(); Assert.Equal("Trade", view.Buttons.First(b => b.Key == "trade").Label);
            var reload = new NpcDialogueEditing(first, path); reload.Load(); reload.Install(); Render();
            Assert.Equal("My shop", view.Buttons.First(b => b.Key == "trade").Label);
        }
        finally { Hooks.RemoveMod(first.Id); Hooks.RemoveMod(second.Id); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void Applying_native_edits_runs_the_render_layout_before_new_buttons_draw()
    {
        _panel.Set("is_activate", true); _world.UserEvents.Clear();
        _editing.Refresh(_editing.Current!);
        Assert.Contains((_panel.Id, 0), _world.UserEvents);
        Assert.True(_panel.Get("surfaceDraw").AsBool);
        Assert.True(Calls.LastIndexOf("event_user") > Calls.LastIndexOf("scr_create_contract_button"));
    }
    [Fact]
    public void Editing_a_dynamic_greeting_only_overrides_the_selected_original_text_variant()
    {
        using var context = _panel.Get("dialog_id").AsStruct!;
        using var strings = context["Strings"].AsStruct!;
        strings["greeting"] = "First meeting";
        Game.CallScript("scr_dialogue_set_text", _panel, "greeting");
        var view = _editing.Current!;
        _editing.Set(view, NpcDialogueEditing.VariantField("line", "greeting", view.OriginalLine), "Edited first meeting", null);
        Game.CallScript("scr_dialogue_set_text", _panel, "greeting");
        Assert.Equal("Edited first meeting", _panel.Get("full_text").AsString);
        strings["greeting"] = "We have already discussed that";
        Game.CallScript("scr_dialogue_set_text", _panel, "greeting");
        Assert.Equal("We have already discussed that", _panel.Get("full_text").AsString);
        strings["greeting"] = "First meeting";
        Game.CallScript("scr_dialogue_set_text", _panel, "greeting");
        Assert.Equal("Edited first meeting", _panel.Get("full_text").AsString);
    }
    [Fact]
    public void Dynamic_option_labels_keep_other_variants_and_their_game_action()
    {
        var view = _editing.Current!;
        _editing.SetOption(view, view.Buttons.First(b => b.Key == "trade"), "Shop", 2);
        _editing.Refresh(view); Assert.Equal("Shop", view.Buttons.First(b => b.Key == "trade").Label);
        _labels["trade"] = "Trade again"; Render();
        var button = view.Buttons.First(b => b.Key == "trade");
        Assert.Equal("Trade again", button.Label); Assert.Equal("trade", button.Button.Get("func").AsString);
        _labels["trade"] = "Trade"; Render();
        Assert.Equal("Shop", view.Buttons.First(b => b.Key == "trade").Label);
    }
    [Fact]
    public void Presentation_refresh_preserves_native_paging_options_without_advancing()
    {
        var view = _editing.Current!;
        _panel.Set("text_wrap_max_number", 1); _panel.Set("text_wrap_number", 0);
        _editing.Refresh(view);
        Assert.True(view.IsPaging); Assert.Equal("next", view.Buttons.Single().Key);
        using var pending = _panel.Get("text_wrap_options").AsArray!;
        Assert.Equal(new[] { "trade", "leave" }, pending.Select(v => v.AsString));
        Assert.Throws<InvalidOperationException>(() => _editing.Add(view, "Topic", "Reply", 1));
        _panel.Set("text_wrap_number", 1); _editing.Refresh(view);
        Assert.False(view.IsPaging); Assert.Equal(new[] { "trade", "leave" }, view.Buttons.Select(b => b.Key));
    }
    [Fact]
    public void Vanilla_edits_change_labels_and_slots_without_changing_action_keys_or_advancing()
    {
        var view = _editing.Current!;
        int advances = _scripts.Runs.GetValueOrDefault("scr_dialogue_advance");
        _editing.Set(view, "option/trade", "Buy and sell", 2); _editing.Refresh(view);
        Assert.Equal(new[] { "leave", "trade" }, view.Buttons.Select(b => b.Key));
        Assert.Equal("Buy and sell", view.Buttons[1].Label);
        Assert.Equal("trade", view.Buttons[1].Button.Get("func").AsString);
        Assert.Equal(advances, _scripts.Runs.GetValueOrDefault("scr_dialogue_advance"));
        _editing.Reset(view, "option/trade"); _editing.Refresh(view);
        Assert.Equal(new[] { "trade", "leave" }, view.Buttons.Select(b => b.Key));
        Assert.Equal("Trade", view.Buttons[0].Label);
    }
    [Fact]
    public void Clicking_native_option_in_edit_mode_selects_it_and_blocks_game_actions()
    {
        var button = _editing.Current!.Buttons.First().Button;
        _editing.EditingPanel = _panel; int selected = 0;
        _editing.Selected = value => { Assert.Equal(button.Id, value.Id); selected++; };
        Assert.Equal(1, RunBefore("gml_Object_o_contract_button_Mouse_4", button.Id)); Assert.Equal(1, selected);
        _editing.EditingPanel = default;
        Assert.Equal(0, RunBefore("gml_Object_o_contract_button_Mouse_4", button.Id));
    }
    [Fact]
    public void Authored_topic_inserts_a_native_option_opens_native_reply_and_returns_to_NPC_context()
    {
        var view = _editing.Current!;
        var topic = _editing.Add(view, "Ask about the forge", "It has stood here for years.", 2);
        _editing.Refresh(view);
        Assert.Equal(new[] { "trade", topic.Fragment, "leave" }, view.Buttons.Select(b => b.Key));
        Game.CallScript("scr_dialogue_advance", _panel, topic.Fragment);
        Assert.Equal("It has stood here for years.", Dialogues.Active!.Text);
        Dialogues.Active.Choose("back");
        Assert.Null(Dialogues.Active);
        using var context = _panel.Get("dialog_id").AsStruct!; Assert.Equal("osbrook_smith", context["RootFragment"].AsString);
        _editing.Delete(topic); Render(); Assert.DoesNotContain(view.Buttons, b => b.Key == topic.Fragment);
    }
    [Fact]
    public void NPC_edit_document_reloads_authored_topics_and_scopes_edits_to_NPC_and_language()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "npc-edits-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "npcs.json"); string language = Localization.Language;
        try
        {
            Localization.SetLanguage("en-US");
            var editing = new NpcDialogueEditing(_context, path);
            var view = _editing.Current!;
            editing.Set(view, "option/trade", "Shop", 2);
            var topic = editing.Add(view, "Hello", "Welcome back", 1);
            topic.Back["en-US"] = "Return to topics";
            editing.Save();
            Hooks.RemoveMod(_context.Id);
            var loaded = new NpcDialogueEditing(new ModContext(_context.Id), path); loaded.Load();
            Assert.Equal(editing.Serialize(), loaded.Serialize());
            Assert.Equal("Shop", loaded.Text(view, "option/trade"));
            Localization.SetLanguage("ru-RU"); Assert.Null(loaded.Text(view, "option/trade"));
            view.Npc = "osbrook_herbalist"; Localization.SetLanguage("en-US"); Assert.Null(loaded.Text(view, "option/trade"));
        }
        finally { Localization.SetLanguage(language); Directory.Delete(folder, true); }
    }
}
