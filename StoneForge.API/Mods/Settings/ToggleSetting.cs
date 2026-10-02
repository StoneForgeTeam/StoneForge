using System.Text.Json;

namespace StoneForge;

/// <summary>A setting that's on or off: a checkbox on the mod's page.</summary>
public sealed class ToggleSetting : ModSetting<bool>
{
    internal ToggleSetting(ModSettings owner, string key, string label, bool defaultValue, string? tooltip) : base(owner, key, label, defaultValue, tooltip) { }

    internal override void Load(JsonElement saved)
    {
        if (saved.ValueKind is JsonValueKind.True or JsonValueKind.False)
            SetLoaded(saved.GetBoolean());
    }

    internal override void Write(Utf8JsonWriter writer) => writer.WriteBooleanValue(Value);
}
