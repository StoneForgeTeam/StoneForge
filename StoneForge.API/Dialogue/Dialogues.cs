namespace StoneForge;

/// <summary>Mod-authored conversations and topics in Stoneshard's native NPC dialogue system.</summary>
public static class Dialogues
{
    private static readonly Dictionary<string, RegisteredDialogue> Registered = new(StringComparer.Ordinal);
    internal static IReadOnlyList<RegisteredDialogue> All => Registered.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
    public static DialogueConversation? Active { get; internal set; }
    public static RegisteredDialogue Register(ModContext context, DialogueDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(definition);
        var nodes = definition.Snapshot(); string id = context.ContentId(definition.Key);
        if (Registered.ContainsKey(id)) throw new ArgumentException("Dialogue already registered: " + id);
        var dialogue = new RegisteredDialogue(context, definition, nodes, id);
        Registered.Add(id, dialogue);
        context.Frame += () => { if (Active?.Dialogue == dialogue) Active.Tick(); };
        context.Localization.TranslationsChanged += () => { if (Active?.Dialogue == dialogue) Active.Refresh(); };
        context.OnScript("scr_dialogue_advance", call =>
        {
            if (Active?.Dialogue != dialogue || Active.Native is not { OwnsPanel: true } native || call.Args.Length == 0) return false;
            string key = call.Args[0].AsString;
            if (key == native.Root) return true;
            if (!key.StartsWith(native.Root + "_", StringComparison.Ordinal)) return false;
            if (Active.Responses.Count == 0 && key == native.Choice("@finish")) { Active.Close(DialogueCloseReason.Completed); return true; }
            foreach (var response in Active.Responses)
                if (key == native.Choice(response.Key)) { Active.Choose(response.Key); return true; }
            return true; // A button from an earlier node must never execute a new action.
        });
        return dialogue;
    }
    internal static bool IsRegistered(RegisteredDialogue dialogue) => Registered.GetValueOrDefault(dialogue.Id) == dialogue;
    internal static void Unregister(RegisteredDialogue dialogue)
    {
        if (Active?.Dialogue == dialogue) Active.Close(DialogueCloseReason.ModUnloaded);
        if (IsRegistered(dialogue)) Registered.Remove(dialogue.Id);
    }
    internal static void RemoveMod(string mod)
    {
        if (Active?.Dialogue.Context.Id == mod) Active.Close(DialogueCloseReason.ModUnloaded);
        foreach (string key in Registered.Where(p => p.Value.Context.Id == mod).Select(p => p.Key).ToArray()) Registered.Remove(key);
    }
    internal static void ResetForTests()
    { Active?.Close(DialogueCloseReason.ModUnloaded); Registered.Clear(); Active = null; }
}

/// <summary>A registered dialogue tree using the game's conversation window.</summary>
public sealed class RegisteredDialogue
{
    internal readonly ModContext Context;
    internal readonly DialogueDefinition Definition;
    internal readonly Dictionary<string, DialogueNodeData> Nodes;
    internal readonly List<DialogueTopic> Topics = new();
    internal DialogueEdits Edits { get; }
    internal RegisteredDialogue(ModContext context, DialogueDefinition definition, Dictionary<string, DialogueNodeData> nodes, string id)
    { Context = context; Definition = definition; Nodes = nodes; Id = id; Edits = new(this); Edits.Load(); }
    public string Id { get; }
    public DialogueConversation? Start(Instance speaker, string? node = null) => StartCore(speaker, node, default);
    internal DialogueConversation? StartOnPanel(Instance speaker, Instance panel, string? node = null) => StartCore(speaker, node, panel);
    private DialogueConversation? StartCore(Instance speaker, string? node, Instance panel)
    {
        if (!Dialogues.IsRegistered(this)) throw new InvalidOperationException("This dialogue's mod has been unloaded.");
        string first = node ?? Definition.StartNode;
        if (!Nodes.ContainsKey(first)) throw new ArgumentException("Unknown dialogue node: " + first, nameof(node));
        speaker = speaker.Persist();
        if (Dialogues.Active != null || UIWindow.AnyOpen || !Available(speaker) || (panel.Exists ? NativeDialogue.SceneBlocked : Game.IsCutscene) ||
            (!panel.Exists && Gm.InstanceExists(GameObjectId.o_dialogue)) || Gm.InstanceExists(GameObjectId.o_exit_confirm_panel)) return null;
        var conversation = new DialogueConversation(this, speaker, first); Dialogues.Active = conversation;
        try { conversation.Open(panel); }
        catch { conversation.Close(DialogueCloseReason.CallbackFailed); throw; }
        return conversation.IsOpen ? conversation : null;
    }
    internal bool Available(Instance speaker) => Player.Exists && speaker.Exists &&
        (Definition.MaximumDistance == null || Units.CellOf(speaker).DistanceTo(Units.CellOf(Player.Instance)) <= Definition.MaximumDistance);

    /// <summary>Adds a topic to the normal Talk conversation of matching NPCs.</summary>
    public void AddTopic(Func<Instance, bool> appliesTo, Func<Instance, string> text, Func<Instance, string?>? startNode = null)
        => AddTopicCore(appliesTo, text, startNode, null);
    /// <summary>Adds a topic at a one-based slot among the native Talk options.</summary>
    public void AddTopic(Func<Instance, bool> appliesTo, Func<Instance, string> text, int position, Func<Instance, string?>? startNode = null)
        => AddTopicCore(appliesTo, text, startNode, position);
    private void AddTopicCore(Func<Instance, bool> appliesTo, Func<Instance, string> text, Func<Instance, string?>? startNode, int? position)
    {
        ArgumentNullException.ThrowIfNull(appliesTo); ArgumentNullException.ThrowIfNull(text);
        if (!Dialogues.IsRegistered(this)) throw new InvalidOperationException("This dialogue's mod has been unloaded.");
        if (position is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(position));
        int index = Topics.Count;
        var entry = new DialogueTopic(text, position); Topics.Add(entry);
        string topic = NativeDialogue.Prefix + Id + "_topic" + (index == 0 ? "" : "_" + index);
        Context.OnScript("dialogue_create_option_buttons", call =>
        {
            if (Dialogues.Active != null || call.Args.Length == 0 || call.Args[0].AsArray is not { } options ||
                call.Self.Get("dialog_id").AsStruct is not { } context) return false;
            // The argument handle is shared by every subscriber to this call.
            // Only dispose the context handle acquired by this handler.
            using (context)
            {
                string root = context["RootFragment"].AsString;
                string phase = call.Self.Get("topic").AsString;
                if (options.Any(v => v.AsString == "next")) return false;
                bool topicsMenu = options.Any(value =>
                {
                    string key = value.AsString;
                    int hash = key.IndexOf("_HASH", StringComparison.Ordinal);
                    if (hash > 0) key = key[..hash];
                    return key is "trade" or "tradeVogt" or "chat" or "rent_room" or "leave" or "back" or "custom_leave";
                });
                // NPCs such as innkeepers enter their main options through greeting/return
                // fragments. Their current fragment need not equal the flow's root name.
                if (root.StartsWith(NativeDialogue.Prefix, StringComparison.Ordinal) ||
                    (phase != root && phase != "return" && !topicsMenu)) return false;
                var npc = ContextMenus.InstanceOf(call.Self.Get("owner"));
                if (!Available(npc) || !appliesTo(npc) || options.Any(v => v.AsString == topic)) return false;
                using var strings = context["Strings"].AsStruct!;
                using var speakers = context["Speakers"].AsStruct!;
                using var fragments = context["Fragments"].AsStruct!;
                using var specs = context["Specs"].AsStruct!;
                using var generic = GmStruct.Create(); generic["generic"] = true;
                string label = Edits.Text("topic/" + index) ?? text(npc);
                entry.LastText = label; entry.LastLanguage = Localization.Language;
                strings[topic] = label; speakers[topic] = "Player"; specs[topic] = generic;
                string destination = topic + "_line";
                fragments[topic] = destination; strings[destination] = ""; speakers[destination] = "NPC"; specs[destination] = generic;
                int? slot = Edits.TopicPosition(index) ?? entry.Position;
                if (slot is { } at) options.Insert(Math.Min(at - 1, options.Length), topic);
                else options.Push(topic);
            }
            return false;
        });
        Context.OnScript("scr_dialogue_advance", call =>
        {
            if (call.Args.Length == 0 || call.Args[0].AsString != topic) return false;
            var npc = ContextMenus.InstanceOf(call.Self.Get("owner"));
            if (Available(npc) && appliesTo(npc)) StartCore(npc, startNode?.Invoke(npc), call.Self.Persist());
            else Game.CallScript("scr_dialogue_advance", call.Self, Game.CallScript("scr_dialogue_get_root_name", call.Self));
            return true;
        });
    }
}

internal sealed record DialogueTopic(Func<Instance, string> Text, int? Position)
{
    internal string? LastText { get; set; }
    internal string? LastLanguage { get; set; }
}

public sealed class ModDialogues
{
    private readonly ModContext _context;
    internal ModDialogues(ModContext context) => _context = context;
    public RegisteredDialogue Add(DialogueDefinition definition) => Dialogues.Register(_context, definition);
}
