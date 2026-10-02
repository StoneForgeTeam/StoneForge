using System.Text.Json;

namespace StoneForge;

/// <summary>One of a few <see cref="Options"/> (its value is the chosen one's index): a dropdown on
/// the mod's page. Saved by the option's text, so reordering them keeps a player's choice.</summary>
public sealed class ChoiceSetting : ModSetting<int>
{
    internal ChoiceSetting(ModSettings owner, string key, string label, IReadOnlyList<string> options, int defaultIndex, string? tooltip)
        : base(owner, key, label, Math.Clamp(defaultIndex, 0, Math.Max(0, options.Count - 1)), tooltip)
    {
        Options = options;
    }

    public IReadOnlyList<string> Options { get; }
    /// <summary>The chosen option's text.</summary>
    public string Selected => Options.Count == 0 ? "" : Options[Value];

    private protected override int Fit(int value) => Options.Count == 0 ? 0 : Math.Clamp(value, 0, Options.Count - 1);

    internal override void Load(JsonElement saved)
    {
        if (saved.ValueKind != JsonValueKind.String)
            return;
        int index = Options.ToList().IndexOf(saved.GetString()!);
        if (index >= 0)
            SetLoaded(index);
    }

    internal override void Write(Utf8JsonWriter writer) => writer.WriteStringValue(Selected);
}
