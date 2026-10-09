namespace StoneForge;

/// <summary>Registers a synchronous static void method as a selectable dialogue action, named modid:key.
/// The method takes no arguments, or one <see cref="DialogOptionContext"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DialogOptionAttribute : Attribute
{
    public DialogOptionAttribute(string key) { ModIdentity.CheckKey(key, "dialogue action"); Key = key; }
    public string Key { get; }
    public string? TextKey { get; set; }
}

/// <summary>The mod and native NPC conversation whose code option the player selected.</summary>
public sealed class DialogOptionContext
{
    internal DialogOptionContext(ModContext mod, string id, Instance speaker, Instance panel)
    { Mod = mod; Id = id; Speaker = speaker.Persist(); Panel = panel.Persist(); }
    public ModContext Mod { get; }
    public string Id { get; }
    public Instance Speaker { get; }
    public Instance Panel { get; }
    public Instance Player => StoneForge.Player.Instance;
    /// <summary>Opens a registered conversation in this NPC's existing dialogue window.</summary>
    public DialogueConversation? Open(RegisteredDialogue dialogue) => dialogue.StartOnPanel(Speaker, Panel);
}
