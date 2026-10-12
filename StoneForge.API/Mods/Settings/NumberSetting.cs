using System.Text.Json;

namespace StoneForge;

/// <summary>A whole number from <see cref="Min"/> to <see cref="Max"/>: a text box on the mod's page that takes digits
/// only - for a value typed exactly (a port, a seed) rather than slid to.</summary>
public sealed class NumberSetting : ModSetting<int>
{
    internal NumberSetting(ModSettings owner, string key, string label, int defaultValue, int min, int max, string? tooltip)
        : base(owner, key, label, Math.Clamp(defaultValue, Math.Min(min, max), Math.Max(min, max)), tooltip)
    {
        Min = Math.Min(min, max);
        Max = Math.Max(min, max);
    }

    public int Min { get; }
    public int Max { get; }

    private protected override int Fit(int value) => Math.Clamp(value, Min, Max);

    // (A whole number, or one saved as a slider's - 7777.0 - rounded.)
    internal override void Load(JsonElement saved)
    {
        if (saved.ValueKind == JsonValueKind.Number && saved.TryGetDouble(out double value) && !double.IsNaN(value))
            SetLoaded((int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue));
    }

    internal override void Write(Utf8JsonWriter writer) => writer.WriteNumberValue(Value);
}
