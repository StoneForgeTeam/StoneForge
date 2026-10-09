using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace StoneForge.Loader;

// All native handles are transient. The document stores NPC/flow/fragment identities, never instance IDs.
internal sealed partial class NpcDialogueEditing
{
    internal sealed class Edit
    {
        public string Npc { get; set; } = "";
        public string Root { get; set; } = "";
        public string Field { get; set; } = "";
        public Dictionary<string, string> Text { get; set; } = new();
        public int? Position { get; set; }
        public bool Deleted { get; set; }
        public bool Localized { get; set; }
        public string? ActionId { get; set; }
        public string? ConditionId { get; set; }
    }
    internal sealed class Topic
    {
        public string Key { get; set; } = "";
        public string Npc { get; set; } = "";
        public string Root { get; set; } = "";
        public Dictionary<string, string> Label { get; set; } = new();
        public Dictionary<string, string> Reply { get; set; } = new();
        public Dictionary<string, string> Back { get; set; } = new();
        public int Position { get; set; } = 1;
        public string? ActionId { get; set; }
        internal RegisteredDialogue? Dialogue;
        internal string Fragment => NativeDialogue.Prefix + "editor_" + Key;
    }
    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<Edit> Edits { get; set; } = new();
        public List<Topic> Topics { get; set; } = new();
    }
    internal sealed record Option(string Key, string Label, Instance Button, string OriginalLabel);
    internal sealed class View
    {
        internal required Instance Panel;
        internal required string Npc, Root;
        internal string[] Options = Array.Empty<string>();
        internal bool NpcTalk, Continue, IsPaging;
        internal readonly List<Option> Buttons = new();
        internal readonly HashSet<string> ConditionLocks = new(StringComparer.Ordinal);
        internal string OriginalLine = "";
        internal string ConditionSignature = "";
    }
    private readonly ModContext _owner;
    private readonly string? _directory;
    private readonly HashSet<string> _savedNpcs = new(StringComparer.Ordinal);
    private Document _document = new();
    internal string? PreviewLanguage { get; private set; }
    internal string Language => PreviewLanguage ?? Localization.Language;
    internal void SetPreviewLanguage(View view, string? language)
    {
        PreviewLanguage = language == null ? null : CultureInfo.GetCultureInfo(language).Name;
        if (Dialogues.Active is { } active && active.Native is { OwnsPanel: true } native && native.Panel.Equals(view.Panel)) active.PreviewLanguage = PreviewLanguage;
        RefreshLanguage(view);
    }
    internal View? Current { get; private set; }
    internal Action<Instance>? Selected;
    internal Instance EditingPanel;
    internal bool SelectingLanguage;
    private bool _runningCode;
    private bool _resumingResponse;
    private bool _frameInstalled;
    private long _nextConditionCheck;
    private string ConditionSignature(View view) => string.Join(";", view.Options.Select(key => ConditionFor(view, key) is { } id ? key + ":" + id + ":" + (int)ConditionState(view, key) : ""));
    internal void TickConditions(bool force = false)
    {
        if (!force && Environment.TickCount64 < _nextConditionCheck) return;
        _nextConditionCheck = Environment.TickCount64 + 250;
        if (Current is not { } view || !view.Panel.Exists || !view.Panel.Get("is_activate").AsBool || EditingPanel.Exists || view.IsPaging) return;
        if (view.Options.Any(key => ConditionFor(view, key) != null) && ConditionSignature(view) != view.ConditionSignature) Refresh(view);
    }
    private readonly Queue<(View View, Option Option, string Action, string? Condition)> _pendingActions = new();
    internal IReadOnlyList<DialogOptions.Entry> CodeOptions => DialogOptions.WithBuiltIns(_owner.Id);
    internal string? ActionFor(View view, Option option) => ActionFor(view, option.Key);
    internal string? ConditionFor(View view, string key) => Find(view, "option/" + StableKey(view, key))?.ConditionId;
    internal IReadOnlyList<DialogConditions.Entry> ConditionOptions => DialogConditions.ForMod(_owner.Id);
    internal void BindCondition(View view, Option option, string? name)
    {
        if (!view.Buttons.Any(o => o.Key == option.Key)) throw new InvalidOperationException("This response is no longer displayed.");
        string? id = name?.Trim();
        if (id != null)
        {
            if (!id.Contains(':')) id = _owner.ContentId(id);
            if (!DialogConditions.Contains(_owner.Id, id)) throw new ArgumentException("Choose a registered [DialogCondition] function from this mod.");
        }
        string field = "option/" + StableKey(view, option.Key);
        var edit = Find(view, field);
        if (edit == null && id != null) _document.Edits.Add(edit = new() { Npc = view.Npc, Root = view.Root, Field = field });
        if (edit != null) edit.ConditionId = id;
    }
    private DialogConditionResult ConditionState(View view, string key) => ConditionFor(view, key) is { } id
        ? DialogConditions.Evaluate(_owner.Id, id, ContextMenus.InstanceOf(view.Panel.Get("owner")), view.Panel) : DialogConditionResult.Enabled;
    private string? ActionFor(View view, string key) => Find(view, "option/" + StableKey(view, key))?.ActionId ?? Authored(view, key)?.ActionId;
    internal string ResolveAction(string name)
    {
        string id = name.Trim(); if (!id.Contains(':')) id = _owner.ContentId(id);
        if (!DialogOptions.Contains(_owner.Id, id)) throw new ArgumentException("Choose a StoneForge trigger or a registered [DialogOption] function from this mod.");
        return id;
    }
    internal void BindAction(View view, Option option, string? name)
    {
        if (!view.Buttons.Any(o => o.Key == option.Key)) throw new InvalidOperationException("This response is no longer displayed.");
        string? id = name == null ? null : ResolveAction(name);
        if (Authored(view, option.Key) is { ActionId: not null } topic)
        {
            if (id == null) throw new InvalidOperationException("Delete this code-only response to remove its trigger.");
            topic.ActionId = id; return;
        }
        string field = "option/" + StableKey(view, option.Key);
        var edit = Find(view, field);
        if (edit == null && id != null) _document.Edits.Add(edit = new() { Npc = view.Npc, Root = view.Root, Field = field });
        if (edit != null) edit.ActionId = id;
    }
    private readonly Dictionary<(int Panel, string Npc, string Root), View> _views = new();
    private readonly Dictionary<(int Panel, string Key), string> _originalLabels = new();
    private readonly Dictionary<ScriptCall, GmArray> _optionCopies = new();
    internal NpcDialogueEditing(ModContext owner, string? path = null)
    {
        _owner = owner;
        // Accept the old aggregate path too, so existing callers migrate normally.
        _directory = path == null ? null : Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path) : path;
    }
    internal static string Normalize(string key)
    { int at = key.IndexOf("_HASH", StringComparison.Ordinal); key = at > 0 ? key[..at] : key; return key == "@dialogue_end" ? "leave" : key; }
    internal static string VariantField(string kind, string key, string original)
        => kind + "/" + Normalize(key) + "/variant/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)));
    private string Translate(Dictionary<string, string> text) => text.GetValueOrDefault(Language)
        ?? text.GetValueOrDefault(Localization.DefaultLanguage) ?? text.Values.FirstOrDefault() ?? "";
    internal View? Inspect(Instance panel)
    {
        if (!panel.Exists) return null;
        var npc = ContextMenus.InstanceOf(panel.Get("owner"));
        using var context = panel.Get("dialog_id").AsStruct;
        string id = npc.Get("id_name").AsString;
        string root = context?["RootFragment"].AsString ?? "";
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(root)) return null;
        var key = (panel.Id, id, root);
        if (!_views.TryGetValue(key, out var view))
            _views[key] = view = new() { Panel = panel.Persist(), Npc = id, Root = root, OriginalLine = panel.Get("full_text").AsString };
        return view;
    }
    internal Edit? Find(View view, string field) => _document.Edits.FirstOrDefault(e => e.Npc == view.Npc && e.Root == view.Root && e.Field == field);
    internal string? Text(View view, string field)
    {
        var edit = Find(view, field);
        return edit?.Text.GetValueOrDefault(Language) ?? (edit?.Localized == true ? edit.Text.GetValueOrDefault(Localization.DefaultLanguage) : null);
    }
    private static string TranslationField(View view, Option? option) => option == null ? "line/" + LineKey(view) : "option/" + StableKey(view, option.Key);
    private static string? ChoiceKey(View view, Option? option) => option == null ? null : ActiveFor(view)?.Responses.FirstOrDefault(c => ActiveFor(view)!.Native!.Choice(c.Key) == option.Key)?.Key;
    private static string? Template(View view, Option? option) => ActiveFor(view)?.EditableTemplate(ChoiceKey(view, option));
    internal string EditableText(View view, Option? option) => Translation(view, option, Language);
    internal bool IsLocalized(View view, Option? option) => Template(view, option) != null || Find(view, TranslationField(view, option))?.Localized == true;
    internal string Translation(View view, Option? option, string locale) =>
        option != null && Authored(view, option.Key) is { } topic ? topic.Label.GetValueOrDefault(locale) ?? option.Label :
        Find(view, TranslationField(view, option))?.Text.GetValueOrDefault(locale) ?? Template(view, option) ?? option?.Label ?? view.Panel.Get("full_text").AsString;
    internal void SetTranslation(View view, Option? option, string locale, string text)
    {
        locale = CultureInfo.GetCultureInfo(locale).Name;
        if (string.IsNullOrEmpty(locale)) throw new ArgumentException("Choose a language.");
        ValidateText(text);
        ActiveFor(view)?.FormatTemplate(ChoiceKey(view, option), text, validate: true);
        if (option != null && Authored(view, option.Key) is { } topic) { topic.Label[locale] = text; return; }
        string field = TranslationField(view, option);
        var edit = Find(view, field);
        if (edit == null) _document.Edits.Add(edit = new() { Npc = view.Npc, Root = view.Root, Field = field });
        if (!edit.Text.ContainsKey(Localization.Language)) edit.Text[Localization.Language] = Template(view, option) ?? option?.OriginalLabel ?? view.OriginalLine;
        edit.Localized = true; edit.Text[locale] = text;
    }
    internal void RefreshLanguage(View view)
    {
        if (!view.Panel.Exists) return;
        if (Dialogues.Active?.Native is not { OwnsPanel: true })
            DisplayLine(view.Panel, (IsLocalized(view, null) ? Text(view, "line/" + LineKey(view)) ?? view.OriginalLine :
                Text(view, VariantField("line", LineKey(view), view.OriginalLine)) ?? Text(view, "line/" + LineKey(view)) ?? VanillaPreviewText(view, null, view.OriginalLine)));
        Refresh(view);
        // The native graph may reuse its current fragment when only an editor override changed.
        if (ActiveFor(view) is { } active && Text(view, "line/" + LineKey(view)) is { } template)
            DisplayLine(view.Panel, active.FormatTemplate(null, template));
    }
    internal void Set(View view, string field, string text, int? position)
    {
        ValidateText(text); ValidatePosition(position);
        var edit = Find(view, field);
        if (edit == null) _document.Edits.Add(edit = new() { Npc = view.Npc, Root = view.Root, Field = field });
        edit.Text[Language] = text; edit.Position = position;
    }
    internal void Reset(View view, string field)
    {
        var edit = Find(view, field);
        if (edit == null) return;
        edit.Text.Remove(Language); edit.Position = null; edit.Deleted = false;
        if (edit.Text.Count == 0 && edit.ActionId == null && edit.ConditionId == null) _document.Edits.Remove(edit);
    }
    internal void SetOption(View view, Option option, string text, int position)
    {
        Set(view, VariantField("option", StableKey(view, option.Key), option.OriginalLabel), text, null);
        SetPosition(view, option.Key, position);
    }
    internal void SetPosition(View view, string key, int position)
    {
        ValidatePosition(position);
        string field = "option/" + StableKey(view, key);
        var placement = Find(view, field);
        if (placement == null) _document.Edits.Add(placement = new() { Npc = view.Npc, Root = view.Root, Field = field });
        placement.Position = position;
    }
    internal void ResetOption(View view, Option option)
    {
        Reset(view, VariantField("option", StableKey(view, option.Key), option.OriginalLabel));
        Reset(view, "option/" + StableKey(view, option.Key));
    }
    internal void RestoreOriginal(View view, Option? option)
    {
        string kind = option == null ? "line" : "option";
        string key = option == null ? LineKey(view) : StableKey(view, option.Key);
        string variant = VariantField(kind, key, option?.OriginalLabel ?? view.OriginalLine);
        string field = kind + "/" + key;
        _document.Edits.RemoveAll(e => e.Npc == view.Npc && e.Root == view.Root && (e.Field == variant || e.Field == field));
        RefreshLanguage(view);
    }
    internal Topic Add(View view, string label, string reply, int position, string? actionId = null)
    {
        ValidateText(label); if (actionId == null) ValidateText(reply); ValidatePosition(position);
        if (actionId != null && !DialogOptions.Contains(_owner.Id, actionId)) throw new ArgumentException("Unknown dialogue action: " + actionId);
        if (Dialogues.Active != null || view.IsPaging || view.Root.StartsWith(NativeDialogue.Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Return to the NPC's main Talk options before adding a topic.");
        if (_document.Topics.Count >= 128) throw new InvalidOperationException("The editor supports 128 authored topics.");
        var topic = new Topic { Key = Guid.NewGuid().ToString("N"), Npc = view.Npc, Root = view.Root, Position = position, ActionId = actionId };
        topic.Label[Language] = label; if (actionId == null) topic.Reply[Language] = reply;
        Register(topic); _document.Topics.Add(topic); return topic;
    }
    internal static bool CanAdd(View view) => Dialogues.Active == null && !view.IsPaging && !view.Root.StartsWith(NativeDialogue.Prefix, StringComparison.Ordinal);
    internal Topic AddRelative(View view, string anchor, bool below, string label, string reply, string? actionId = null)
    {
        var keys = view.Buttons.Select(o => o.Key).ToList();
        int index = keys.IndexOf(anchor);
        if (index < 0) throw new InvalidOperationException("This option is no longer displayed.");
        int position = index + (below ? 2 : 1);
        var topic = Add(view, label, reply, position, actionId);
        keys.Insert(position - 1, topic.Fragment);
        for (int i = 0; i < keys.Count; i++)
            if (Authored(view, keys[i]) is { } authored) authored.Position = i + 1;
            else SetPosition(view, keys[i], i + 1);
        return topic;
    }
    internal Topic AddCodeRelative(View view, string anchor, bool below, string id)
    {
        var entry = CodeOptions.FirstOrDefault(e => e.Id == id) ?? throw new ArgumentException("Unknown dialogue action: " + id);
        return AddRelative(view, anchor, below, entry.Label, "", id);
    }
    internal void DeleteOption(View view, string key)
    {
        if (view.IsPaging || view.Buttons.Count <= 1) throw new InvalidOperationException("Keep at least one response in the conversation.");
        if (!view.Buttons.Any(o => o.Key == key)) throw new InvalidOperationException("This option is no longer displayed.");
        if (Authored(view, key) is { } topic)
        { Delete(topic); view.Options = view.Options.Where(k => k != key).ToArray(); }
        else
        {
            string field = "option/" + StableKey(view, key);
            var edit = Find(view, field);
            if (edit == null) _document.Edits.Add(edit = new() { Npc = view.Npc, Root = view.Root, Field = field });
            edit.Deleted = true;
        }
    }
    internal bool HasDeleted(View view) => _document.Edits.Any(e => e.Npc == view.Npc && e.Root == view.Root && e.Deleted);
    internal void RestoreDeleted(View view)
    {
        foreach (var edit in _document.Edits.Where(e => e.Npc == view.Npc && e.Root == view.Root)) edit.Deleted = false;
        _document.Edits.RemoveAll(e => !e.Deleted && e.Position == null && e.Text.Count == 0 && e.ActionId == null && e.ConditionId == null);
    }
    internal void RestoreDialogue(View view)
    {
        var topics = _document.Topics.Where(t => t.Npc == view.Npc && t.Root == view.Root).ToArray();
        var fragments = topics.Select(t => t.Fragment).ToHashSet(StringComparer.Ordinal);
        foreach (var topic in topics) Delete(topic);
        _document.Edits.RemoveAll(e => e.Npc == view.Npc && e.Root == view.Root);
        view.Options = view.Options.Where(k => !fragments.Contains(k)).ToArray();
        RefreshLanguage(view);
    }
    private void Register(Topic topic)
    {
        if (topic.ActionId != null) return;
        topic.Dialogue = _owner.Dialogues.Add(new("editor_" + topic.Key, "reply")
        {
            Nodes = { new DialogueNode("reply", "") { TextProvider = _ => Translate(topic.Reply),
                Choices = { new DialogueChoice("back", "Back") { TextProvider = _ => topic.Back.Count == 0 ? Localization.Get("npc_editor.back") : Translate(topic.Back) } } } }
        });
    }
    internal Topic? Authored(View view, string key) => _document.Topics.FirstOrDefault(t => t.Npc == view.Npc && t.Root == view.Root && t.Fragment == key);
    internal Topic? AuthoredDialogue(RegisteredDialogue dialogue) => _document.Topics.FirstOrDefault(t => t.Dialogue == dialogue);
    internal void Delete(Topic topic)
    { _document.Topics.Remove(topic); if (topic.Dialogue != null) Dialogues.Unregister(topic.Dialogue); }
    internal void Install()
    {
        SaveData.OnLoaded(_owner, _ => { _nativePreviewRows.Clear(); });
        _owner.OnScript("dialogue_create_option_buttons", call =>
        {
            var view = Inspect(call.Self); if (view == null || call.Args.Length < 3 || call.Args[0].AsArray is not { } options) return false;
            if (Dialogues.Active is { } active && active.Native is { OwnsPanel: true } native && native.Panel.Equals(view.Panel)) active.PreviewLanguage = PreviewLanguage;
            if (_document.Topics.Any(t => t.Npc == view.Npc && t.Root == view.Root) || HasDeleted(view) ||
                _document.Edits.Any(e => e.Npc == view.Npc && e.Root == view.Root && e.ConditionId != null))
            {
                options = GmArray.From(options.ToArray()); call.Args[0] = options; _optionCopies[call] = options;
            }
            view.Buttons.Clear();
            // Refresh may start from a cached option list containing a removed callback.
            for (int i = options.Length - 1; i >= 0; i--)
                if (Authored(view, options[i].AsString) is { ActionId: { } action } && !DialogOptions.Contains(_owner.Id, action))
                    options.Delete(i);
            if (!options.Any(v => v.AsString == "next") && Dialogues.Active == null)
            {
                using var context = call.Self.Get("dialog_id").AsStruct!;
                using var strings = context["Strings"].AsStruct!;
                using var fragments = context["Fragments"].AsStruct!;
                using var speakers = context["Speakers"].AsStruct!;
                using var specs = context["Specs"].AsStruct!;
                foreach (var topic in _document.Topics.Where(t => t.Npc == view.Npc && t.Root == view.Root))
                {
                    if (topic.ActionId != null && !DialogOptions.Contains(_owner.Id, topic.ActionId)) continue;
                    using var generic = GmStruct.Create(); generic["generic"] = true;
                    strings[topic.Fragment] = Translate(topic.Label); speakers[topic.Fragment] = "Player"; specs[topic.Fragment] = generic;
                    string line = topic.Fragment + "_line";
                    fragments[topic.Fragment] = line; strings[line] = Translate(topic.Reply); speakers[line] = "NPC"; specs[line] = generic;
                    // Cached options already contain this key after insertion. Rewrite
                    // the text each time so edits appear without reopening the NPC.
                    if (!options.Any(v => v.AsString == topic.Fragment)) options.Push(topic.Fragment);
                }
            }
            view.IsPaging = options.Any(v => v.AsString == "next");
            if (view.IsPaging)
            {
                using var pending = call.Self.Get("text_wrap_options").AsArray;
                if (pending != null) view.Options = pending.Select(v => v.AsString).ToArray();
                view.NpcTalk = call.Self.Get("text_wrap_npctalk").AsBool; view.Continue = call.Self.Get("text_wrap_continue").AsBool;
            }
            else { view.Options = options.Select(v => v.AsString).ToArray(); view.NpcTalk = call.Args[1].AsBool; view.Continue = call.Args[2].AsBool; }
            if (!view.IsPaging)
            {
                // Keep the full source list cached for restore; filter only the displayed array.
                var removed = Enumerable.Range(0, options.Length)
                    .Where(i => Find(view, "option/" + StableKey(view, options[i].AsString))?.Deleted == true).ToArray();
                // A context may eventually show only hidden responses. Keep a way forward.
                int keep = removed.Length == options.Length ? 1 : 0;
                foreach (int i in removed.Reverse().Skip(keep)) options.Delete(i);
                var speaker = ContextMenus.InstanceOf(view.Panel.Get("owner"));
                foreach (string key in view.ConditionLocks) Game.CallScript("scr_dialogue_set_option_lock", view.Panel, key, false);
                view.ConditionLocks.Clear();
                for (int i = options.Length - 1; i >= 0; i--)
                {
                    string key = options[i].AsString;
                    var state = ConditionState(view, key);
                    if (state == DialogConditionResult.Hidden) { options.Delete(i); continue; }
                    if (state == DialogConditionResult.Visible)
                    { Game.CallScript("scr_dialogue_set_option_lock", view.Panel, key, true); view.ConditionLocks.Add(key); }
                    if (ActionFor(view, key) is not { } action || !DialogOptions.Contains(_owner.Id, action)) continue;
                    if (!DialogOptions.IsVisible(_owner.Id, action, speaker, view.Panel)) { options.Delete(i); continue; }
                    if (!DialogOptions.IsBuiltIn(action) && !DialogOptions.IsEnabled(_owner.Id, action, speaker, view.Panel))
                    { Game.CallScript("scr_dialogue_set_option_lock", view.Panel, key, true); view.ConditionLocks.Add(key); }
                }
            }
            Current = view; return false;
        }, after: call =>
        {
            if (_optionCopies.Remove(call, out var copy)) copy.Dispose();
            var view = Inspect(call.Self); if (view == null) return;
            var speaker = ContextMenus.InstanceOf(view.Panel.Get("owner"));
            // Built-in services inspect the completed native buttons, including
            // their game locks; before creation there are no buttons to inspect.
            foreach (var option in view.Buttons)
                if (view.ConditionLocks.Contains(option.Key) || ActionFor(view, option) is { } action && DialogOptions.IsBuiltIn(action) && !DialogOptions.IsEnabled(_owner.Id, action, speaker, view.Panel))
                {
                    Game.CallScript("scr_dialogue_set_option_lock", view.Panel, option.Key, true);
                    option.Button.Set("canPress", false); view.ConditionLocks.Add(option.Key);
                }
            view.ConditionSignature = ConditionSignature(view);
        }, order: HookOrder.Last);
        _owner.OnScript("scr_dialogue_sort_options", after: call =>
        {
            var view = Inspect(call.Self); if (view == null || call.Args.Length == 0 || call.Args[0].AsArray is not { } options) return;
            var order = options.Select(v => v.AsString).ToList();
            var moves = order.Select(key => (Key: key, Position: Find(view, "option/" + StableKey(view, key))?.Position ?? Authored(view, key)?.Position))
                .Where(p => p.Position != null).OrderBy(p => p.Position).ThenBy(p => p.Key, StringComparer.Ordinal).ToArray();
            // Remove first, then insert: native actions and close options retain their identities.
            foreach (var move in moves) order.Remove(move.Key);
            foreach (var move in moves) order.Insert(Math.Min(move.Position!.Value - 1, order.Count), move.Key);
            for (int i = 0; i < order.Count; i++) options[i] = order[i];
        });
        _owner.OnScript("scr_create_contract_button", call =>
        {
            var view = Inspect(call.Self);
            if (view != null && call.Args.Length >= 2)
            {
                string key = call.Args[1].AsString, original = call.Args[0].AsString;
                _originalLabels[(view.Panel.Id, key)] = original;
                string field = "option/" + StableKey(view, key);
                call.Args[0] = (Find(view, field)?.Localized == true ? Text(view, field) : Text(view, VariantField("option", StableKey(view, key), original)) ?? Text(view, field)) ??
                    VanillaPreviewText(view, new(key, original, default, original), original);
                if (ActiveFor(view) is { } active)
                    call.Args[0] = active.FormatTemplate(ChoiceKey(view, new(key, original, default, original)), call.Args[0].AsString);
            }
            return false;
        }, after: call =>
        {
            var view = Inspect(call.Self); if (view == null || call.Args.Length < 2) return;
            var button = ContextMenus.InstanceOf(call.Result);
            string key = call.Args[1].AsString;
            view.Buttons.Add(new(key, call.Args[0].AsString, button.Persist(), _originalLabels.GetValueOrDefault((view.Panel.Id, key)) ?? call.Args[0].AsString));
        });
        _owner.OnScript("scr_dialogue_set_text", after: call =>
        {
            var view = Inspect(call.Self); if (view == null || call.Args.Length == 0) return;
            view.OriginalLine = call.Self.Get("full_text").AsString;
            string field = "line/" + LineKey(view);
            string? text = (Find(view, field)?.Localized == true ? Text(view, field) : Text(view, VariantField("line", LineKey(view), view.OriginalLine)) ?? Text(view, field));
            if (text != null) DisplayLine(call.Self, ActiveFor(view)?.FormatTemplate(null, text) ?? text);
            else if (PreviewLanguage != null) DisplayLine(call.Self, VanillaPreviewText(view, null, view.OriginalLine));
        });
        _owner.OnScript("scr_dialogue_advance", call =>
        {
            var view = Inspect(call.Self); if (view == null || call.Args.Length == 0) return false;
            // Native response shortcuts use raw key checks (Space/Enter/number keys),
            // bypassing the game's normal hotkey guard while the text overlay is focused.
            if ((UITextBox.AnyFocused || SelectingLanguage) && EditingPanel.Exists && EditingPanel.Id == call.Self.Id) return true;
            if (!_resumingResponse && ConditionState(view, call.Args[0].AsString) != DialogConditionResult.Enabled) return true;
            if (!_resumingResponse && ActionFor(view, call.Args[0].AsString) is { } bound && Authored(view, call.Args[0].AsString)?.ActionId == null)
            {
                if (_runningCode || EditingPanel.Exists) return true;
                if (!DialogOptions.Contains(_owner.Id, bound)) return false; // Preserve the response when an optional binding is unavailable.
                if (Dialogues.Active is { } conversation && conversation.Native is { OwnsPanel: true } native &&
                    !conversation.Responses.Any(r => native.Choice(r.Key) == call.Args[0].AsString && r.IsEnabled)) return false;
                _runningCode = true;
                var active = Dialogues.Active;
                int? revision = active?.Revision;
                try
                {
                    bool ran = InvokeAction(bound, ContextMenus.InstanceOf(call.Self.Get("owner")), call.Self);
                    if (DialogOptions.IsBuiltIn(bound)) return true;
                    if (!ran || !call.Self.Exists || Inspect(call.Self)?.Root != view.Root || Dialogues.Active != active || active?.Revision != revision) return true;
                }
                finally { _runningCode = false; }
            }
            var topic = Authored(view, call.Args[0].AsString); if (topic == null) return false;
            if (topic.ActionId != null)
            {
                if (_runningCode || EditingPanel.Exists) return true;
                _runningCode = true;
                try
                {
                    string action = ActionFor(view, call.Args[0].AsString) ?? topic.ActionId;
                    bool ran = InvokeAction(action, ContextMenus.InstanceOf(call.Self.Get("owner")), call.Self);
                    if (!DialogOptions.IsBuiltIn(action) && ran && call.Self.Exists && Inspect(call.Self)?.Root == view.Root && Dialogues.Active == null) Refresh(view);
                }
                finally { _runningCode = false; }
                return true;
            }
            topic.Dialogue!.StartOnPanel(ContextMenus.InstanceOf(call.Self.Get("owner")), call.Self.Persist()); return true;
        }, order: HookOrder.First);
        _owner.OnCode("gml_Object_o_contract_button_Mouse_4", (self, _) =>
        {
            var panel = ContextMenus.InstanceOf(self.Get("parent"));
            if (EditingPanel.Exists && panel.Id == EditingPanel.Id)
            { Selected?.Invoke(self.Persist()); return true; }
            if (_resumingResponse) return false;
            var view = Inspect(panel);
            var option = view?.Buttons.FirstOrDefault(o => o.Button.Id == self.Id);
            if (view == null || option == null) return false;
            if (ConditionState(view, option.Key) != DialogConditionResult.Enabled) return true;
            if (ActionFor(view, option) is not { } action || !DialogOptions.Contains(_owner.Id, action)) return false;
            if (!_pendingActions.Any(p => p.View.Panel.Id == panel.Id)) _pendingActions.Enqueue((view, option, action, ConditionFor(view, option.Key)));
            if (!_frameInstalled) { _frameInstalled = true; _owner.Frame += RunPendingActions; }
            return true;
        });
        _owner.OnCode("gml_Object_o_contract_button_Step_1", (self, _) =>
            UITextBox.AnyFocused && EditingPanel.Exists && ContextMenus.InstanceOf(self.Get("parent")).Id == EditingPanel.Id);
        _owner.OnCode("gml_Object_o_dialog_button_Step_0", (self, _) =>
            UITextBox.AnyFocused && EditingPanel.Exists);
    }
    private void RunPendingActions()
    {
        // Empty until a room-backed click queues work. A callback may destroy
        // the panel/speaker; run outside the native mouse event's with scope.
        int count = _pendingActions.Count;
        while (count-- > 0)
        {
            var (view, option, action, condition) = _pendingActions.Dequeue();
            if (!view.Panel.Exists || !option.Button.Exists || Inspect(view.Panel)?.Root != view.Root || EditingPanel.Exists) continue;
            if (ActionFor(view, option) != action) continue;
            if (ConditionFor(view, option.Key) != condition || ConditionState(view, option.Key) != DialogConditionResult.Enabled) continue;
            var active = Dialogues.Active;
            if (active?.Native is { OwnsPanel: true } native && !active.Responses.Any(r => native.Choice(r.Key) == option.Key && r.IsEnabled)) continue;
            int? revision = active?.Revision;
            var speaker = ContextMenus.InstanceOf(view.Panel.Get("owner"));
            if (!speaker.Exists) continue;
            _runningCode = true;
            bool ran;
            try { ran = InvokeAction(action, speaker, view.Panel); }
            finally { _runningCode = false; }
            if (DialogOptions.IsBuiltIn(action)) continue;
            if (!ran || !view.Panel.Exists || !speaker.Exists || Inspect(view.Panel)?.Root != view.Root || Dialogues.Active != active || active?.Revision != revision) continue;
            if (Authored(view, option.Key)?.ActionId != null) { Refresh(view); continue; }
            if (!option.Button.Exists) continue;
            _resumingResponse = true;
            try { Game.CallBuiltinAs("event_perform", option.Button, option.Button, 6, 4); } // Mouse, Left Pressed.
            finally { _resumingResponse = false; }
        }
    }
    private bool InvokeAction(string action, Instance speaker, Instance panel)
    {
        bool previous = _resumingResponse;
        if (DialogOptions.IsBuiltIn(action)) _resumingResponse = true;
        try { return DialogOptions.Invoke(_owner.Id, action, speaker, panel); }
        finally { _resumingResponse = previous; }
    }
    internal static string LineKey(View view) => ActiveFor(view) is { } active ? "mod/" + active.Id + "/" + active.NodeKey + "/line" : Normalize(view.Panel.Get("text_fragment").AsString);
    internal static string StableKey(View view, string key)
    {
        var active = ActiveFor(view);
        var choice = active?.Responses.FirstOrDefault(c => active.Native!.Choice(c.Key) == key);
        return choice == null ? Normalize(key) : "mod/" + active!.Id + "/" + active.NodeKey + "/choice/" + choice.Key;
    }
    private static DialogueConversation? ActiveFor(View view) => Dialogues.Active is { } active && active.Native is { OwnsPanel: true } &&
        view.Root == active.Native.Root && active.Native.Panel.Equals(view.Panel) ? active : null;
    internal static void DisplayLine(Instance panel, string text)
    {
        panel.Set("full_text", text);
        panel.Set("text_wrap_number", 0);
        var wrapped = Game.CallScript("scr_dialog_text_wrap", panel, text);
        panel.Set("text", wrapped); wrapped.AsArray?.Dispose(); panel.Set("is_text_changed", true);
    }
    internal void Refresh(View view)
    {
        if (!view.Panel.Exists) return;
        if (Dialogues.Active is { } active && active.Native is { OwnsPanel: true } native)
        {
            using var preview = Localization.Preview(PreviewLanguage);
            active.Refresh(); native.Render();
            return;
        }
        // Render cached options, never advance a flow or re-execute its dialogue actions.
        using var context = view.Panel.Get("dialog_id").AsStruct!;
        Game.CallScript("scr_dialogue_context_set", view.Panel, context);
        var render = ContextMenus.InstanceOf(view.Panel.Get("render"));
        if (render.Exists) Game.CallScript("scr_guiContainerChildrenDestroy", render, render.Get("buttonsContainer"));
        using var options = GmArray.From(view.Options.Select(s => (GmValue)s));
        if (view.Panel.Get("text_wrap_max_number").AsReal > view.Panel.Get("text_wrap_number").AsReal)
        {
            view.Panel.Set("text_wrap_options", options); view.Panel.Set("text_wrap_npctalk", view.NpcTalk); view.Panel.Set("text_wrap_continue", view.Continue);
            using var next = GmArray.From(new GmValue[] { "next" });
            Game.CallScript("dialogue_create_option_buttons", view.Panel, next, false, false);
        }
        else Game.CallScript("dialogue_create_option_buttons", view.Panel, options, view.NpcTalk, view.Continue);
        // Match scr_dialogue_advance's final native render pass. User event 0 on
        // o_surfaceRender invokes the dialogue layout, including each button's
        // colorTextMap/font creation, before those buttons can draw.
        if (render.Exists)
        {
            render.Set("surfaceDraw", view.Panel.Get("is_activate"));
            Game.CallBuiltinTrusted("event_user", render, view.Panel, 0);
        }
    }
    internal string Serialize() => JsonSerializer.Serialize(_document, new JsonSerializerOptions { WriteIndented = true });
    internal void Load()
    {
        if (_directory == null) return;
        _owner.OptionalFiles?.Resolve(_directory, write: false);
        if (!Directory.Exists(_directory)) return;
        string legacy = Path.Combine(_directory, "npcs.json");
        bool migrate = File.Exists(legacy), failed = false;
        var documents = new Document();
        var paths = Directory.GetFiles(_directory, "npc_*.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (migrate) paths.Insert(0, legacy);
        foreach (string path in paths)
        {
            try
            {
                var document = ReadDocument(path);
                var npcs = document.Edits.Select(e => e.Npc).Concat(document.Topics.Select(t => t.Npc)).Distinct().ToArray();
                if (path != legacy)
                {
                    if (npcs.Length > 1 || npcs.Any(npc => !Path.GetFileName(path).Equals(NpcFileName(npc), StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("NPC dialogue file must contain only its named NPC.");
                    // A per-NPC file takes precedence over its legacy aggregate entries,
                    // including an empty document saved by Restore dialogue.
                    var replaced = npcs.Concat(documents.Edits.Select(e => e.Npc).Concat(documents.Topics.Select(t => t.Npc))
                        .Where(npc => Path.GetFileName(path).Equals(NpcFileName(npc), StringComparison.OrdinalIgnoreCase))).Distinct().ToArray();
                    documents.Edits.RemoveAll(e => replaced.Contains(e.Npc));
                    documents.Topics.RemoveAll(t => replaced.Contains(t.Npc));
                }
                documents.Edits.AddRange(document.Edits); documents.Topics.AddRange(document.Topics);
                foreach (string npc in npcs) _savedNpcs.Add(npc);
            }
            catch (Exception error) { failed = true; Game.Log("NPC dialogue editor (" + Path.GetFileName(path) + "): " + error.Message); }
        }
        if (documents.Topics.Count > 128 || documents.Topics.Select(t => t.Key).Distinct().Count() != documents.Topics.Count)
        { Game.Log("NPC dialogue editor: Invalid combined NPC topics."); return; }
        _document = documents;
        foreach (var topic in _document.Topics) Register(topic);
        if (migrate && !failed)
        {
            try
            {
                Save();
                _owner.OptionalFiles?.Resolve(legacy, write: true);
                _owner.OptionalFiles?.Resolve(legacy + ".migrated.bak", write: true);
                File.Move(legacy, legacy + ".migrated.bak", true);
            }
            catch (Exception error) { Game.Log("NPC dialogue migration: " + error.Message); }
        }
    }
    internal static string NpcFileName(string npc)
    {
        string encoded = Uri.EscapeDataString(npc);
        return (npc.StartsWith("npc_", StringComparison.Ordinal) ? encoded : "npc_" + encoded) + ".json";
    }
    private Document ReadDocument(string path)
    {
        _owner.OptionalFiles?.Resolve(path, write: false);
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("NPC dialogue edits exceed 1 MB.");
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty NPC edits.");
            if (document.Version != 1 || document.Edits == null || document.Topics == null || document.Topics.Count > 128) throw new InvalidDataException("Invalid NPC edit format.");
            foreach (var edit in document.Edits)
            { Identity(edit.Npc, edit.Root); if (string.IsNullOrWhiteSpace(edit.Field) || edit.Text == null) throw new InvalidDataException("Invalid edit."); ValidatePosition(edit.Position); ValidateAction(edit.ActionId); ValidateCondition(edit.ConditionId); foreach (var text in edit.Text.Values) ValidateText(text); }
            var keys = new HashSet<string>();
            foreach (var topic in document.Topics)
            {
                Identity(topic.Npc, topic.Root);
                if (!Guid.TryParseExact(topic.Key, "N", out _) || !keys.Add(topic.Key) || topic.Label == null || topic.Reply == null || topic.Back == null) throw new InvalidDataException("Invalid authored topic.");
                ValidateAction(topic.ActionId);
                ValidatePosition(topic.Position); foreach (var text in topic.Label.Values.Concat(topic.Reply.Values).Concat(topic.Back.Values)) ValidateText(text);
            }
            return document;
    }
    internal void Save()
    {
        if (_directory == null) return;
        foreach (string npc in _document.Edits.Select(e => e.Npc).Concat(_document.Topics.Select(t => t.Npc))) _savedNpcs.Add(npc);
        foreach (string npc in _savedNpcs)
        {
            var document = new Document { Edits = _document.Edits.Where(e => e.Npc == npc).ToList(), Topics = _document.Topics.Where(t => t.Npc == npc).ToList() };
            string json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            if (json.Length > 1024 * 1024) throw new InvalidDataException("NPC dialogue edits exceed 1 MB.");
            string path = Path.Combine(_directory, NpcFileName(npc));
            _owner.OptionalFiles?.Resolve(path, write: true);
            _owner.OptionalFiles?.Resolve(path + ".tmp", write: true);
            _owner.OptionalFiles?.Resolve(path + ".bak", write: true);
            Directory.CreateDirectory(_directory); File.WriteAllText(path + ".tmp", json);
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(path + ".tmp", path, true);
        }
    }
    private static void Identity(string npc, string root)
    { if (string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(root)) throw new InvalidDataException("Missing NPC identity."); }
    private void ValidateAction(string? id)
    {
        if (id == null) return;
        if (DialogOptions.IsBuiltIn(id)) return;
        string prefix = _owner.Id + ":";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Dialogue action belongs to another mod.");
        ModIdentity.CheckKey(id[prefix.Length..], "dialogue action");
    }
    private void ValidateCondition(string? id)
    {
        if (id == null) return;
        string prefix = _owner.Id + ":";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Dialogue condition belongs to another mod.");
        ModIdentity.CheckKey(id[prefix.Length..], "dialogue condition");
    }
    internal static void ValidateText(string text)
    { if (string.IsNullOrWhiteSpace(text) || text.Length > 8192) throw new ArgumentException("Enter text between 1 and 8192 characters."); }
    internal static void ValidatePosition(int? value)
    { if (value is < 1 or > 128) throw new ArgumentException("Position must be between 1 and 128."); }
}
