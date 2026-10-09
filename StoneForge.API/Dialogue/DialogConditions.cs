using System.Reflection;
using System.Runtime.CompilerServices;

namespace StoneForge;

internal static class DialogConditions
{
    internal sealed record Entry(string Id, ModContext Mod, Func<DialogOptionContext, DialogConditionResult> Check);
    private static readonly Dictionary<string, Entry> Registered = new(StringComparer.Ordinal);
    internal static IReadOnlyList<Entry> ForMod(string mod) => Registered.Values.Where(e => e.Mod.Id == mod).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
    private static Entry? Find(string mod, string id) => Registered.TryGetValue(id, out var entry) && entry.Mod.Id == mod ? entry : null;
    internal static bool Contains(string mod, string id) => Find(mod, id) != null;
    internal static void Register(ModContext mod, Assembly assembly) => Register(mod, assembly.GetTypes());
    internal static void Register(ModContext mod, IEnumerable<Type> types)
    {
        var pending = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var type in types)
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var attribute = method.GetCustomAttribute<DialogConditionAttribute>();
            if (attribute == null) continue;
            var parameters = method.GetParameters();
            if (!method.IsStatic || method.ReturnType != typeof(DialogConditionResult) || method.ContainsGenericParameters || type.ContainsGenericParameters ||
                method.IsDefined(typeof(AsyncStateMachineAttribute)) || parameters.Length > 1 ||
                parameters.Length == 1 && parameters[0].ParameterType != typeof(DialogOptionContext))
                throw new ArgumentException($"{type.FullName}.{method.Name}: [DialogCondition] requires static DialogConditionResult with no arguments or one DialogOptionContext.");
            string id = mod.ContentId(attribute.Key);
            Func<DialogOptionContext, DialogConditionResult> check;
            if (parameters.Length == 0) { var run = method.CreateDelegate<Func<DialogConditionResult>>(); check = _ => run(); }
            else check = method.CreateDelegate<Func<DialogOptionContext, DialogConditionResult>>();
            if (Registered.ContainsKey(id) || !pending.TryAdd(id, new(id, mod, check))) throw new ArgumentException("Duplicate dialogue condition: " + id);
        }
        foreach (var (id, entry) in pending) Registered.Add(id, entry);
    }
    internal static DialogConditionResult Evaluate(string mod, string id, Instance speaker, Instance panel)
    {
        if (Find(mod, id) is not { } entry) return DialogConditionResult.Visible;
        var result = DialogConditionResult.Visible;
        Hooks.Invoke(mod, "dialogue condition " + id, () =>
        {
            var checkedResult = entry.Check(new(entry.Mod, id, speaker, panel));
            if (!Enum.IsDefined(checkedResult)) throw new ArgumentException("Invalid dialogue condition result: " + checkedResult);
            result = checkedResult;
            return true;
        }, entry.Check);
        return result;
    }
    internal static void RemoveMod(string mod)
    { foreach (string id in Registered.Where(p => p.Value.Mod.Id == mod).Select(p => p.Key).ToArray()) Registered.Remove(id); }
}
