using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>Mods' consumables (<see cref="ModClassDeclaration"/>) as the game's own: for each, o_inv_&lt;key&gt; - a
/// child of the game consumable's o_inv_ object it's based on, with its sprite - and o_loot_&lt;key&gt; likewise
/// (on the ground). The game then makes, saves, loads, stacks and drops them as its own, by those names; StoneForge
/// gives them their stats row, name, pictures and what they do (its Consumable).</summary>
internal static class ConsumableObjects
{
    // The consumables added (those whose base is one of the game's consumables).
    public static List<ModClassDeclaration> Add(GameDataEditor editor, List<ModClassDeclaration> declarations)
    {
        var data = editor.Data;
        // The game's consumables by the class StoneForge.GameItems gives each (its id in PascalCase:
        // "potion02_water" -> Potion02Water).
        var byClass = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var obj in data.GameObjects)
        {
            string name = obj.Name.Content;
            if (name.StartsWith("o_inv_", StringComparison.Ordinal))
                byClass.TryAdd(ClassName(name.Substring(6)), name.Substring(6));
        }
        var made = new List<ModClassDeclaration>();
        foreach (var declaration in declarations)
        {
            if (declaration.BaseType is "ModSkill" or "ModPassive")
                continue;
            string? basedOn = declaration.BaseType == "Consumable" ? declaration.BasedOn : null;
            if (basedOn == null)
            {
                // (A clash in the generator's names gets "Consumable" and a number added.)
                string plain = Regex.Replace(declaration.BaseType, "Consumable[0-9]*$", "");
                if (!byClass.TryGetValue(declaration.BaseType, out basedOn) && !byClass.TryGetValue(plain, out basedOn))
                    continue;
            }
            if (!Regex.IsMatch(declaration.Key, "^[A-Za-z0-9_]+$"))
            {
                PatcherConsole.Log($"  consumable \"{declaration.Key}\": a key is letters, digits and _ only - not added");
                continue;
            }
            var parent = data.GameObjects.ByName("o_inv_" + basedOn);
            if (parent == null)
            {
                PatcherConsole.Log($"  consumable \"{declaration.Key}\": the game has no consumable \"{basedOn}\" - not added");
                continue;
            }
            if (data.GameObjects.ByName("o_inv_" + declaration.Key) != null)
            {
                PatcherConsole.Log($"  consumable \"{declaration.Key}\": the game already has an item called that - not added");
                continue;
            }
            Child(editor, "o_inv_" + declaration.Key, parent);
            if (data.GameObjects.ByName("o_loot_" + basedOn) is { } loot)
                Child(editor, "o_loot_" + declaration.Key, loot);
            PatcherConsole.Log($"  consumable {declaration.Key} (based on {basedOn}): added");
            made.Add(declaration);
        }
        return made;
    }

    // A child of a game object: its sprite, persistence and visibility.
    private static void Child(GameDataEditor editor, string name, UndertaleGameObject parent)
    {
        var obj = editor.AddObject(name);
        obj.ParentId = parent;
        obj.Sprite = parent.Sprite;
        obj.Persistent = parent.Persistent;
        obj.Visible = parent.Visible;
    }

    // As StoneForge.Generators names a consumable's class: its id's words (split at anything not a letter or a
    // digit) each capitalised, joined - "_" before one starting with a digit.
    private static string ClassName(string id)
    {
        string plain = string.Concat(Regex.Split(id, "[^A-Za-z0-9]+").Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        return plain.Length == 0 || char.IsDigit(plain[0]) ? "_" + plain : plain;
    }
}
