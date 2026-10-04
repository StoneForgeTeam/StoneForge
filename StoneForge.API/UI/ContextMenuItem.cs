namespace StoneForge;

/// <summary>One of a right-click menu's options: its key (the game's: "Attack", "Talk"... - a mod's are its own), the
/// text shown, whether it can be clicked, and its hover hint ("" for none).</summary>
public readonly record struct ContextMenuItem(string Key, string Text, bool Enabled, string Hover);
