using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace StoneForge;

// Presentation overrides only. Mod-authored conditions, destinations and actions remain authoritative.
internal sealed class DialogueEdits
{
    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Dictionary<string, string>> Text { get; set; } = new();
        public Dictionary<int, int> TopicPositions { get; set; } = new();
        public Dictionary<string, string[]> ResponseOrder { get; set; } = new();
    }
    private readonly RegisteredDialogue _dialogue;
    private Document _document = new();
    internal DialogueEdits(RegisteredDialogue dialogue) => _dialogue = dialogue;
    internal static string VariantField(string field, string original)
        => field + "/variant/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)));
    internal string Path => "Dialogue/" + _dialogue.Definition.Key + ".editor.json";
    internal string? Text(string field) => _document.Text.TryGetValue(field, out var translations)
        ? translations.GetValueOrDefault(Localization.Language) : null;
    internal int? TopicPosition(int index) => _document.TopicPositions.TryGetValue(index, out int value) ? value : null;
    internal IEnumerable<DialogueChoice> OrderedChoices(string node)
    {
        var choices = _dialogue.Nodes[node].Choices;
        if (!_document.ResponseOrder.TryGetValue(node, out var order)) return choices;
        return order.Select(key => choices.First(c => c.Key == key));
    }
    internal void SetText(string field, string text)
    {
        ValidateField(field);
        if (text.Length > 8192) throw new ArgumentException("Text is limited to 8192 characters.");
        if (!field.StartsWith("node/", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("A response cannot be empty.");
        if (!_document.Text.TryGetValue(field, out var translations)) _document.Text[field] = translations = new();
        translations[Localization.Language] = text;
        Refresh();
    }
    internal void SetTopicPosition(int index, int? position)
    {
        if (index < 0 || index >= _dialogue.Topics.Count || position is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(position));
        if (position is { } value) _document.TopicPositions[index] = value;
        else _document.TopicPositions.Remove(index);
    }
    internal void MoveResponse(string node, string choice, int position)
    {
        var order = OrderedChoices(node).Select(c => c.Key).ToList();
        if (!order.Remove(choice) || position < 1 || position > order.Count + 1) throw new ArgumentOutOfRangeException(nameof(position));
        order.Insert(position - 1, choice); _document.ResponseOrder[node] = order.ToArray(); Refresh();
    }
    internal void Reset(string field)
    {
        ValidateField(field);
        if (_document.Text.TryGetValue(field, out var translations))
        {
            translations.Remove(Localization.Language);
            if (translations.Count == 0) _document.Text.Remove(field);
        }
        if (field.StartsWith("topic/", StringComparison.Ordinal)) _document.TopicPositions.Remove(int.Parse(field[6..], CultureInfo.InvariantCulture));
        if (field.StartsWith("choice/", StringComparison.Ordinal)) _document.ResponseOrder.Remove(field.Split('/')[1]);
        Refresh();
    }
    private void Refresh() { if (Dialogues.Active?.Dialogue == _dialogue) Dialogues.Active.Refresh(); }
    internal string Serialize() => JsonSerializer.Serialize(_document, new JsonSerializerOptions { WriteIndented = true });
    internal void Replace(string json)
    {
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Dialogue edits exceed 1 MB.");
        var document = JsonSerializer.Deserialize<Document>(json) ?? throw new InvalidDataException("Empty dialogue edits.");
        if (document.Version != 1 || document.Text == null || document.TopicPositions == null || document.ResponseOrder == null)
            throw new InvalidDataException("Unsupported dialogue edit format.");
        foreach (var (field, translations) in document.Text)
        {
            ValidateField(field);
            if (translations == null) throw new InvalidDataException("Missing translations.");
            foreach (var (locale, text) in translations)
            {
                if (CultureInfo.GetCultureInfo(locale).Name != locale || text == null || text.Length > 8192 ||
                    (!field.StartsWith("node/", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(text)))
                    throw new InvalidDataException("Invalid dialogue text.");
            }
        }
        foreach (var (index, position) in document.TopicPositions)
            if (index is < 0 or > 127 || position is < 1 or > 128) throw new InvalidDataException("Invalid topic position.");
        foreach (var (node, order) in document.ResponseOrder)
            if (!_dialogue.Nodes.TryGetValue(node, out var data) || order == null ||
                !order.Order(StringComparer.Ordinal).SequenceEqual(data.Choices.Select(c => c.Key).Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Invalid response order.");
        _document = document; Refresh();
    }
    private void ValidateField(string field)
    {
        int variant = field.LastIndexOf("/variant/", StringComparison.Ordinal);
        if (variant > 0)
        {
            string hash = field[(variant + 9)..];
            if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid text variant.");
            field = field[..variant];
        }
        var parts = field.Split('/');
        bool valid = parts.Length == 2 && parts[0] == "node" && _dialogue.Nodes.ContainsKey(parts[1]) ||
            parts.Length == 3 && parts[0] == "choice" && _dialogue.Nodes.TryGetValue(parts[1], out var node) && node.Choices.Any(c => c.Key == parts[2]) ||
            parts.Length == 2 && parts[0] == "topic" && int.TryParse(parts[1], out int index) && index is >= 0 and < 128;
        if (!valid) throw new InvalidDataException("Unknown dialogue field: " + field);
    }
    internal void Load()
    {
        try
        {
            if (_dialogue.Context.OptionalFiles is { } files && files.Exists(Path))
            {
                string full = files.Resolve(Path, write: false);
                if (new FileInfo(full).Length > 1024 * 1024) throw new InvalidDataException("Dialogue edits exceed 1 MB.");
                Replace(files.ReadAllText(Path));
            }
        }
        catch (Exception error) { _dialogue.Context.Log("Dialogue editor: " + error.Message); }
    }
    internal void Save()
    {
        if (!Dialogues.IsRegistered(_dialogue)) throw new InvalidOperationException("This dialogue's mod has been unloaded.");
        var files = _dialogue.Context.Files;
        string json = Serialize();
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Dialogue edits exceed 1 MB.");
        files.WriteAllText(Path + ".tmp", json);
        string target = files.Resolve(Path, write: true);
        if (File.Exists(target)) File.Copy(target, files.Resolve(Path + ".bak", write: true), overwrite: true);
        File.Move(files.Resolve(Path + ".tmp", write: true), target, overwrite: true);
    }
}
