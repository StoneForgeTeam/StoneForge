using System.Text.Json;

namespace StoneForge;

/// <summary>One of a mod's settings (<see cref="ModContext.Settings"/>): saved for it, and shown on its page in
/// the Mods window, where the player can change it. See <see cref="ModSetting{T}"/>.</summary>
public abstract class ModSetting
{
    private protected ModSetting(ModSettings owner, string key, string label, string? tooltip)
    {
        Owner = owner;
        Key = key;
        Label = label;
        Tooltip = tooltip;
    }

    internal ModSettings Owner { get; }
    /// <summary>What it's saved as: don't change it once players have it.</summary>
    public string Key { get; }
    /// <summary>Its name on the mod's page.</summary>
    public string Label { get; }
    /// <summary>Shown in the game's hover frame on the mod's page.</summary>
    public string? Tooltip { get; }
    /// <summary>Whether it's on the mod's page (a setting that isn't is just saved).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Back to its default.</summary>
    public abstract void Reset();

    internal abstract void Load(JsonElement saved);
    internal abstract void Write(Utf8JsonWriter writer);
}
