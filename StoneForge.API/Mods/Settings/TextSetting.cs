using System.Text.Json;

namespace StoneForge;

/// <summary>Some text (at most <see cref="MaxLength"/> characters): a text box on the mod's page.</summary>
public sealed class TextSetting : ModSetting<string>
{
    internal TextSetting(ModSettings owner, string key, string label, string defaultValue, int maxLength, string? tooltip)
        : base(owner, key, label, defaultValue.Length > maxLength ? defaultValue[..maxLength] : defaultValue, tooltip)
    {
        MaxLength = maxLength;
    }

    public int MaxLength { get; }

    private protected override string Fit(string value)
    {
        value ??= "";
        return value.Length > MaxLength ? value[..MaxLength] : value;
    }

    internal override void Load(JsonElement saved)
    {
        if (saved.ValueKind == JsonValueKind.String)
            SetLoaded(saved.GetString()!);
    }

    internal override void Write(Utf8JsonWriter writer) => writer.WriteStringValue(Value);
}
