namespace StoneForge;

/// <summary>A branching conversation owned by a mod. Register during Load with context.Dialogues.Add.</summary>
public sealed class DialogueDefinition
{
    public DialogueDefinition(string key, string startNode)
    {
        ModIdentity.CheckKey(key, "dialogue"); ModIdentity.CheckKey(startNode, "dialogue node");
        Key = key; StartNode = startNode;
    }
    public string Key { get; }
    public string StartNode { get; }
    public List<DialogueNode> Nodes { get; } = new();
    /// <summary>Maximum room-grid distance to the speaker; null allows speaking from any distance.</summary>
    public int? MaximumDistance { get; init; } = 2;
    public Action<DialogueConversation, DialogueCloseReason>? OnClosed { get; init; }

    internal Dictionary<string, DialogueNodeData> Snapshot()
    {
        if (MaximumDistance is < 0) throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        if (Nodes.Count is 0 or > 128) throw new ArgumentException("A dialogue needs between 1 and 128 nodes.");
        var nodes = new Dictionary<string, DialogueNodeData>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.Choices.Count > 32) throw new ArgumentException("A node supports at most 32 choices.");
            var choices = node.Choices.ToArray();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in choices)
            {
                ArgumentNullException.ThrowIfNull(choice);
                if (!keys.Add(choice.Key)) throw new ArgumentException("Duplicate dialogue choice: " + choice.Key);
            }
            if (!nodes.TryAdd(node.Key, new(node, choices))) throw new ArgumentException("Duplicate dialogue node: " + node.Key);
        }
        if (!nodes.ContainsKey(StartNode)) throw new ArgumentException("The dialogue's start node does not exist.");
        foreach (var node in nodes.Values)
            foreach (var choice in node.Choices)
                if (choice.NextNode is { } next && !nodes.ContainsKey(next)) throw new ArgumentException("Unknown next dialogue node: " + next);
        return nodes;
    }
}

/// <summary>An NPC's line and the player's responses. Text providers are evaluated again when the UI refreshes.</summary>
public sealed class DialogueNode
{
    public DialogueNode(string key, string text)
    {
        ModIdentity.CheckKey(key, "dialogue node"); ArgumentNullException.ThrowIfNull(text);
        Key = key; Text = text;
    }
    public string Key { get; }
    public string Text { get; }
    public string? TextKey { get; init; }
    /// <summary>Optional dynamic text; takes precedence over TextKey and Text.</summary>
    public Func<DialogueConversation, string>? TextProvider { get; init; }
    /// <summary>Live values for {0}, {1}, etc. in the text template, including edited translations.</summary>
    public Func<DialogueConversation, object?[]>? TextArguments { get; init; }
    public List<DialogueChoice> Choices { get; } = new();
    /// <summary>Runs once per visit to this node, not on localization or condition refresh.</summary>
    public Action<DialogueConversation>? OnEnter { get; init; }
}

/// <summary>A player response. A null NextNode ends the conversation after OnSelected.</summary>
public sealed class DialogueChoice
{
    public DialogueChoice(string key, string text, string? nextNode = null)
    {
        ModIdentity.CheckKey(key, "dialogue choice"); ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (nextNode != null) ModIdentity.CheckKey(nextNode, "dialogue node");
        Key = key; Text = text; NextNode = nextNode;
    }
    public string Key { get; }
    public string Text { get; }
    public string? TextKey { get; init; }
    public Func<DialogueConversation, string>? TextProvider { get; init; }
    /// <summary>Live values for placeholders in the response template.</summary>
    public Func<DialogueConversation, object?[]>? TextArguments { get; init; }
    public string? NextNode { get; }
    public Func<DialogueConversation, bool>? VisibleWhen { get; init; }
    public Func<DialogueConversation, bool>? EnabledWhen { get; init; }
    public Action<DialogueConversation>? OnSelected { get; init; }
}

public enum DialogueCloseReason { Completed, Cancelled, SpeakerUnavailable, RoomChanged, ModUnloaded, CallbackFailed }
internal sealed record DialogueNodeData(DialogueNode Node, DialogueChoice[] Choices);
public sealed record DialogueResponse(string Key, string Text, bool IsEnabled);
