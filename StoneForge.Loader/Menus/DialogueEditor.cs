using System.Text;

namespace StoneForge.Loader;

internal static class DialogueEditor
{
    private static readonly Dictionary<string, NpcDialogueEditor> Tools = new();
    internal static string? DevMod { get; private set; }
    internal static bool CanEnable(string id) => Tools.ContainsKey(id) && (DevMod == null || DevMod == id);
    internal static void Install(ModContext context)
    {
        if (context.OptionalFiles == null) return;
        Remove(context.Id);
        Tools[context.Id] = NpcDialogueEditor.Install(context, () => DevMod == context.Id);
    }
    internal static void Toggle(string id)
    {
        if (!CanEnable(id)) return;
        Tools[id].Close(); DevMod = DevMod == id ? null : id;
    }
    internal static void Remove(string id)
    {
        if (Tools.Remove(id, out var tool)) tool.Close();
        if (DevMod == id) DevMod = null;
    }
    internal static string Encode(string text) => text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
    internal static string Decode(string text)
    {
        var result = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is 'n' or 'r' or '\\')
                result.Append(text[++i] switch { 'n' => '\n', 'r' => '\r', _ => '\\' });
            else result.Append(text[i]);
        return result.ToString();
    }
}
