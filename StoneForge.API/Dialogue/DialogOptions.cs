using System.Reflection;
using System.Runtime.CompilerServices;

namespace StoneForge;

// Delegates are owned by the mod, released with its hooks and collectible assembly.
internal static class DialogOptions
{
    internal sealed record Entry(string Id, ModContext Mod, DialogOptionAttribute Attribute,
        Action<DialogOptionContext> Action, Func<DialogOptionContext, bool>? Visible, Func<DialogOptionContext, bool>? Enabled, bool BuiltIn = false)
    {
        internal string Label => BuiltIn ? Localization.Get("dialog_trigger." + Attribute.Key) : Attribute.TextKey is { } key ? Mod.Localization.Get(key) : Id;
    }
    private static readonly Dictionary<string, Entry> Registered = new(StringComparer.Ordinal);
    internal static IReadOnlyList<Entry> ForMod(string mod) => Registered.Values.Where(e => e.Mod.Id == mod).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
    internal static IReadOnlyList<Entry> WithBuiltIns(string mod) => BuiltInDialogOptions.All.Values.Concat(ForMod(mod)).ToArray();
    internal static bool IsBuiltIn(string id) => BuiltInDialogOptions.All.ContainsKey(id);
    private static Entry? Find(string mod, string id) => BuiltInDialogOptions.All.GetValueOrDefault(id) ?? (Registered.TryGetValue(id, out var entry) && entry.Mod.Id == mod ? entry : null);
    internal static void Register(ModContext context, Assembly assembly) => Register(context, assembly.GetTypes());
    internal static void Register(ModContext context, IEnumerable<Type> types)
    {
        var pending = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var type in types)
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var attribute = method.GetCustomAttribute<DialogOptionAttribute>();
            if (attribute == null) continue;
            var parameters = method.GetParameters();
            if (!method.IsStatic || method.ReturnType != typeof(void) || method.ContainsGenericParameters || type.ContainsGenericParameters ||
                method.IsDefined(typeof(AsyncStateMachineAttribute)) || parameters.Length > 1 ||
                parameters.Length == 1 && parameters[0].ParameterType != typeof(DialogOptionContext))
                throw new ArgumentException($"{type.FullName}.{method.Name}: [DialogOption] requires static void with no arguments or one DialogOptionContext.");
            string id = context.ContentId(attribute.Key);
            Action<DialogOptionContext> action;
            if (parameters.Length == 0) { var run = method.CreateDelegate<Action>(); action = _ => run(); }
            else action = method.CreateDelegate<Action<DialogOptionContext>>();
            if (IsBuiltIn(id) || Registered.ContainsKey(id) || !pending.TryAdd(id, new(id, context, attribute, action, null, null)))
                throw new ArgumentException("Duplicate dialogue action: " + id);
        }
        foreach (var (id, entry) in pending) Registered.Add(id, entry);
    }
    internal static bool Contains(string mod, string id) => Find(mod, id) != null;
    internal static bool IsVisible(string mod, string id, Instance speaker, Instance panel) =>
        Find(mod, id) is { } e && Check(e, e.Visible, "visibility", new(e.Mod, id, speaker, panel));
    internal static bool IsEnabled(string mod, string id, Instance speaker, Instance panel) =>
        Find(mod, id) is { } e && Check(e, e.Enabled, "availability", new(e.Mod, id, speaker, panel));
    private static bool Check(Entry entry, Func<DialogOptionContext, bool>? check, string kind, DialogOptionContext context) =>
        check == null || Hooks.Invoke(entry.Mod.Id, "dialogue " + kind + " " + entry.Id, () => check(context), check);
    internal static bool Invoke(string mod, string id, Instance speaker, Instance panel)
    {
        if (Find(mod, id) is not { } entry) return false;
        var context = new DialogOptionContext(entry.Mod, id, speaker, panel);
        if (!Check(entry, entry.Visible, "visibility", context) || !Check(entry, entry.Enabled, "availability", context)) return false;
        return Hooks.Invoke(mod, "dialogue action " + id, () => { entry.Action(context); return true; }, entry.Action);
    }
    internal static void RemoveMod(string mod)
    { foreach (string id in Registered.Where(p => p.Value.Mod.Id == mod).Select(p => p.Key).ToArray()) Registered.Remove(id); }
}
