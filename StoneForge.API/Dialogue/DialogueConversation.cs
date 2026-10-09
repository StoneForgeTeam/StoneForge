namespace StoneForge;

/// <summary>One transient conversation. Save persistent outcomes through quests or ModData, not in State.</summary>
public sealed class DialogueConversation
{
    internal readonly RegisteredDialogue Dialogue;
    private readonly int _room;
    private NativeDialogue? _native;
    private bool _busy, _entering, _refreshing;
    private int _revision, _refreshFrames;
    private string _text = "", _speakerName = "";
    internal string SourceText { get; private set; } = "";
    internal Dictionary<string, string> SourceResponses { get; } = new();
    private DialogueResponse[] _responses = Array.Empty<DialogueResponse>();
    internal DialogueConversation(RegisteredDialogue dialogue, Instance speaker, string node)
    { Dialogue = dialogue; Speaker = speaker; NodeKey = node; _room = Gm.Room; }
    public string Id => Dialogue.Id;
    public Instance Speaker { get; }
    public string NodeKey { get; private set; }
    public string Text => _text;
    public string SpeakerName => _speakerName;
    public IReadOnlyList<DialogueResponse> Responses => Array.AsReadOnly(_responses);
    public IDictionary<string, object?> State { get; } = new Dictionary<string, object?>();
    public bool IsOpen { get; private set; } = true;
    public DialogueCloseReason? CloseReason { get; private set; }
    internal int Revision => _revision;
    internal string? PreviewLanguage;
    internal NativeDialogue? Native => _native;
    internal void Open(Instance panel = default)
    {
        _native = new NativeDialogue(this, panel);
        if (!Enter()) return;
        _native.Open();
    }
    private bool Enter()
    {
        _revision++;
        _entering = true;
        try
        {
            if (Dialogue.Nodes[NodeKey].Node.OnEnter is { } enter && !Run(() => enter(this), "enter")) return false;
        }
        finally { _entering = false; }
        if (!IsOpen) return false;
        Refresh();
        if (IsOpen) _native?.Render(true);
        return IsOpen;
    }
    public bool GoTo(string node)
    {
        if (!IsOpen) return false;
        if (_entering || _refreshing) throw new InvalidOperationException("Change nodes from a choice callback, not OnEnter or text/condition providers.");
        if (!Dialogue.Nodes.ContainsKey(node)) throw new ArgumentException("Unknown dialogue node: " + node, nameof(node));
        NodeKey = node;
        return Enter();
    }
    public bool Choose(string key) => Choose(key, _revision);
    internal bool Choose(string key, int revision)
    {
        if (!IsOpen || _busy || _refreshing || revision != _revision) return false;
        if (!Valid()) return false;
        var choice = Dialogue.Nodes[NodeKey].Choices.FirstOrDefault(c => c.Key == key)
            ?? throw new ArgumentException("Unknown dialogue choice: " + key, nameof(key));
        _busy = true;
        try
        {
            if (!Condition(choice.VisibleWhen) || !Condition(choice.EnabledWhen) || !IsOpen) return false;
            int before = _revision;
            if (choice.OnSelected is { } selected && !Run(() => selected(this), "choice " + key)) return false;
            if (!IsOpen || _revision != before) return true; // Callback explicitly closed or changed the node.
            if (choice.NextNode is { } next) GoTo(next);
            else Close(DialogueCloseReason.Completed);
            return true;
        }
        finally { _busy = false; }
    }
    private bool Condition(Func<DialogueConversation, bool>? condition)
    {
        if (condition == null) return true;
        bool result = false;
        return Run(() => result = condition(this), "condition") && result;
    }
    private bool Run(Action action, string where)
    {
        bool success = Hooks.Invoke(Dialogue.Context.Id, "dialogue " + Id + " " + where, () => { action(); return true; }, action);
        if (!success) Close(DialogueCloseReason.CallbackFailed);
        return success;
    }
    private string Resolve(string text, string? key, Func<DialogueConversation, string>? provider)
    {
        string result = text;
        if (provider != null) Run(() => result = provider(this) ?? "", "text");
        else if (key != null) result = Dialogue.Context.Localization.Get(key);
        return result;
    }
    /// <summary>Re-evaluates text and conditions without re-entering the node or running choice actions.</summary>
    public void Refresh()
    {
        if (!IsOpen || _refreshing) return;
        using var preview = Localization.Preview(PreviewLanguage);
        _refreshing = true;
        try { RefreshCore(); }
        finally { _refreshing = false; }
    }
    private void RefreshCore()
    {
        var node = Dialogue.Nodes[NodeKey];
        string field = "node/" + NodeKey;
        SourceText = Dialogue.Edits.Text(field) ?? Resolve(node.Node.Text, node.Node.TextKey, node.Node.TextProvider);
        string text = Dialogue.Edits.Text(DialogueEdits.VariantField(field, SourceText)) ?? SourceText;
        SourceResponses.Clear();
        var responses = new List<DialogueResponse>();
        foreach (var choice in Dialogue.Edits.OrderedChoices(NodeKey))
        {
            if (!IsOpen) return;
            if (!Condition(choice.VisibleWhen)) continue;
            string choiceField = "choice/" + NodeKey + "/" + choice.Key;
            string original = Dialogue.Edits.Text(choiceField) ?? Resolve(choice.Text, choice.TextKey, choice.TextProvider);
            SourceResponses[choice.Key] = original;
            string label = Dialogue.Edits.Text(DialogueEdits.VariantField(choiceField, original)) ?? original;
            bool enabled = Condition(choice.EnabledWhen);
            responses.Add(new(choice.Key, label, enabled));
        }
        if (!IsOpen) return;
        string name = Speaker.Get("name") is { Kind: GmKind.String } value ? value.AsString : "";
        bool changed = _text != text || _speakerName != name || !_responses.SequenceEqual(responses);
        _text = text; _speakerName = name; _responses = responses.ToArray();
        if (changed) _native?.Render();
    }
    private bool Valid()
    {
        if (Gm.Room != _room) { Close(DialogueCloseReason.RoomChanged); return false; }
        if (!Dialogue.Available(Speaker)) { Close(DialogueCloseReason.SpeakerUnavailable); return false; }
        return true;
    }
    internal void Tick()
    {
        if (!IsOpen || !Valid()) return;
        if (_native is { OwnsPanel: false }) { Close(DialogueCloseReason.Cancelled); return; }
        if (++_refreshFrames >= 15) { _refreshFrames = 0; Refresh(); }
    }
    public void Close() => Close(DialogueCloseReason.Cancelled);
    internal void Close(DialogueCloseReason reason)
    {
        if (!IsOpen) return;
        IsOpen = false; CloseReason = reason;
        if (Dialogues.Active == this) Dialogues.Active = null;
        _native?.Close();
        if (reason != DialogueCloseReason.ModUnloaded && Dialogue.Definition.OnClosed is { } closed)
            Hooks.Invoke(Dialogue.Context.Id, "dialogue " + Id + " closed", () => { closed(this, reason); return true; }, closed);
    }
}
