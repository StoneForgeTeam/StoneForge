using System.Text.Json;

namespace StoneForge;

/// <summary>A number between <see cref="Min"/> and <see cref="Max"/> (on its <see cref="Step"/>s, if it has
/// them): a slider on the mod's page.</summary>
public sealed class SliderSetting : ModSetting<double>
{
    internal SliderSetting(ModSettings owner, string key, string label, double defaultValue, double min, double max, double step, string? tooltip)
        : base(owner, key, label, Math.Clamp(defaultValue, Math.Min(min, max), Math.Max(min, max)), tooltip)
    {
        Min = Math.Min(min, max);
        Max = Math.Max(min, max);
        Step = Math.Max(0, step);
    }

    public double Min { get; }
    public double Max { get; }
    /// <summary>The steps it moves in (0: any value).</summary>
    public double Step { get; }
    /// <summary>How its value is shown on the page (default: as it is).</summary>
    public Func<double, string>? Format { get; set; }

    private protected override double Fit(double value)
    {
        if (double.IsNaN(value))
            value = Default;
        value = Math.Clamp(value, Min, Max);
        return Step > 0 ? Math.Clamp(Min + Math.Round((value - Min) / Step) * Step, Min, Max) : value;
    }

    internal override void Load(JsonElement saved)
    {
        if (saved.ValueKind == JsonValueKind.Number && saved.TryGetDouble(out double value))
            SetLoaded(value);
    }

    internal override void Write(Utf8JsonWriter writer) => writer.WriteNumberValue(Value);
}
