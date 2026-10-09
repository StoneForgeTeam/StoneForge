using System.Text;

namespace StoneForge.Loader;

internal static class DialogueEditor
{
    private static readonly Dictionary<string, NpcDialogueEditor> Tools = new();
    private static readonly Dictionary<string, ModManifest> Manifests = new();
    internal static string? DevMod { get; private set; }
    internal static bool IsContributor(string id) => Manifests.TryGetValue(id, out var manifest) && manifest.IsContributor(Steam.AccountId);
    internal static bool CanEnable(string id) => Tools.ContainsKey(id) && IsContributor(id) && (DevMod == null || DevMod == id);
    internal static bool IsEditing(string id)
    {
        if (DevMod != id) return false;
        if (IsContributor(id)) return true;
        Tools[id].Close(); DevMod = null;
        return false;
    }
    internal static void Install(ModContext context)
    {
        if (context.OptionalFiles == null) return;
        Remove(context.Id);
        Manifests[context.Id] = context.Manifest;
        Tools[context.Id] = NpcDialogueEditor.Install(context, () => IsEditing(context.Id));
    }
    internal static void Toggle(string id)
    {
        if (!CanEnable(id)) return;
        Tools[id].Close(); DevMod = DevMod == id ? null : id;
    }
    internal static void Remove(string id)
    {
        if (Tools.Remove(id, out var tool)) tool.Close();
        Manifests.Remove(id);
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
