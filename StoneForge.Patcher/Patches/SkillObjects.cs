using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>Mods' skills (<see cref="ModClassDeclaration"/>) as the game's own: for each, o_skill_&lt;key&gt; - a child
/// of the game skill's object it's based on, its Create naming it by the mod's key (its row in the skills table,
/// which StoneForge adds) - and o_skill_&lt;key&gt;_ico, its icon (with nothing to unlock it but ability points): a
/// child of o_skill_ico, not of the game skill's icon - the skills menu places a skill's icon with
/// <c>with (o_skill_x_ico)</c>, which would take ours for the game's - with the game skill's icon's other events. And o_skill_category_stonemod, a category of the skills menu
/// for a mod's skills (StoneForge's ModSkill: one per mod, in a Mods group), laid out on a grid - GML in
/// GML\Skills. The game then learns, casts, saves and loads them as its own. On the native (YYC) build (native) the same
/// objects are added with no code - their GML StoneForge does in C# (its SkillData) - and an icon is a plain child of
/// o_skill_ico: the game skill's icon's events can't be copied there.</summary>
internal static class SkillObjects
{
    // The skills added (those whose base is one of the game's skills).
    public static List<ModClassDeclaration> Add(GameDataEditor editor, List<ModClassDeclaration> declarations, bool native = false)
    {
        var data = editor.Data;
        AddCategory(editor, native);
        // The game's skills by the class StoneForge.GameSkills gives each (its id in PascalCase).
        var byClass = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var obj in data.GameObjects)
        {
            string name = obj.Name.Content;
            if (name.StartsWith("o_skill_", StringComparison.Ordinal) && !name.EndsWith("_ico", StringComparison.Ordinal) && data.GameObjects.ByName(name + "_ico") != null)
                byClass.TryAdd(ClassName(name.Substring(8)), name.Substring(8));
        }
        var made = new List<ModClassDeclaration>();
        var events = new List<(string Key, string BasedOn)>();
        var passives = new List<string>();
        var passiveParent = editor.GetObject("o_skill_passive");
        // (A passive's icon until its mod gives it its own: one of the game's passives'.)
        var passiveSprite = data.GameObjects.ByName("o_pass_skill_adaptability")?.Sprite
            ?? data.GameObjects.FirstOrDefault(o => o.Name.Content.StartsWith("o_pass_skill_", StringComparison.Ordinal) && o.Sprite != null)?.Sprite;
        foreach (var declaration in declarations)
        {
            if (declaration.BaseType is "Consumable" or "GameObject")
                continue;
            // A mod's passive: o_pass_skill_<key>, one of the game's passives (child of o_skill_passive).
            if (declaration.BaseType == "ModPassive")
            {
                if (!Regex.IsMatch(declaration.Key, "^[A-Za-z0-9_]+$") || data.GameObjects.ByName("o_pass_skill_" + declaration.Key) != null)
                {
                    PatcherConsole.Log($"  passive \"{declaration.Key}\": not a key, or the game already has a passive called that - not added");
                    continue;
                }
                var passive = Child(editor, "o_pass_skill_" + declaration.Key, passiveParent);
                passive.Sprite = passiveSprite;
                passives.Add(declaration.Key);
                made.Add(declaration);
                continue;
            }
            string? basedOn = declaration.BaseType == "ModSkill" ? declaration.BasedOn : null;
            if (basedOn == null)
            {
                string plain = Regex.Replace(declaration.BaseType, "Skill[0-9]*$", "");
                if (!byClass.TryGetValue(declaration.BaseType, out basedOn) && !byClass.TryGetValue(plain, out basedOn))
                    continue;
            }
            if (!Regex.IsMatch(declaration.Key, "^[A-Za-z0-9_]+$"))
            {
                PatcherConsole.Log($"  skill \"{declaration.Key}\": a key is letters, digits and _ only - not added");
                continue;
            }
            var parent = data.GameObjects.ByName("o_skill_" + basedOn);
            var parentIcon = data.GameObjects.ByName("o_skill_" + basedOn + "_ico");
            if (parent == null || parentIcon == null)
            {
                PatcherConsole.Log($"  skill \"{declaration.Key}\": the game has no skill \"{basedOn}\" - not added");
                continue;
            }
            if (data.GameObjects.ByName("o_skill_" + declaration.Key) != null || data.GameObjects.ByName("o_skill_" + declaration.Key + "_ico") != null)
            {
                PatcherConsole.Log($"  skill \"{declaration.Key}\": the game already has a skill called that - not added");
                continue;
            }
            Child(editor, "o_skill_" + declaration.Key, parent);
            var icon = Child(editor, "o_skill_" + declaration.Key + "_ico", parentIcon);
            icon.ParentId = parentIcon.ParentId;
            if (!native)
                CopyEvents(parentIcon, icon);
            events.Add((declaration.Key, basedOn));
            made.Add(declaration);
        }
        // (Their events once all the objects are there: an icon's names its skill. None on the native build.)
        if (native)
        {
            foreach (var (key, basedOn) in events)
                PatcherConsole.Log($"  skill {key} (based on {basedOn}, no code - the native build): added");
            foreach (string key in passives)
                PatcherConsole.Log($"  passive {key} (no code - the native build): added");
            return made;
        }
        foreach (var (key, basedOn) in events)
        {
            editor.AddNewEvent("o_skill_" + key, Gml("skill_create").Replace("{key}", key), EventType.Create, 0);
            editor.AddNewEvent("o_skill_" + key + "_ico", Gml("skill_ico_create").Replace("{key}", key), EventType.Create, 0);
            PatcherConsole.Log($"  skill {key} (based on {basedOn}): added");
        }
        foreach (string key in passives)
        {
            editor.AddNewEvent("o_pass_skill_" + key, Gml("passive_create").Replace("{key}", key), EventType.Create, 0);
            PatcherConsole.Log($"  passive {key}: added");
        }
        return made;
    }

    // The skills menu's category for a mod's skills, and how StoneForge fills one.
    private static void AddCategory(GameDataEditor editor, bool native)
    {
        var category = editor.AddObject("o_skill_category_stonemod");
        category.ParentId = editor.GetObject("o_skill_category");
        category.Visible = true;
        if (native)
            return;
        editor.AddFunction(Gml("scr_stonemod_skill_category_setup"), "scr_stonemod_skill_category_setup");
        editor.AddFunction(Gml("scr_stonemod_skill_define"), "scr_stonemod_skill_define");
        editor.AddNewEvent("o_skill_category_stonemod", Gml("category_create"), EventType.Create, 0);
        editor.AddNewEvent("o_skill_category_stonemod", Gml("category_layout"), EventType.Other, 24);
    }

    // A child of a game object: its sprite, persistence and visibility.
    private static UndertaleGameObject Child(GameDataEditor editor, string name, UndertaleGameObject parent)
    {
        var obj = editor.AddObject(name);
        obj.ParentId = parent;
        obj.Sprite = parent.Sprite;
        obj.Persistent = parent.Persistent;
        obj.Visible = parent.Visible;
        return obj;
    }

    // An object's events (but its Create - ours is added after) on another: the same code.
    private static void CopyEvents(UndertaleGameObject from, UndertaleGameObject to)
    {
        for (int type = 0; type < from.Events.Count; type++)
        {
            if (type == (int)EventType.Create)
                continue;
            foreach (var ev in from.Events[type])
            {
                var copy = new UndertaleGameObject.Event { EventSubtype = ev.EventSubtype };
                foreach (var action in ev.Actions)
                    copy.Actions.Add(new UndertaleGameObject.EventAction { CodeId = action.CodeId });
                to.Events[type].Add(copy);
            }
        }
    }

    private static string Gml(string name) => LoaderGml.Read($"Skills/{name}.gml");

    // As StoneForge.Generators names a skill's class: its id's words each capitalised, joined.
    private static string ClassName(string id)
    {
        string plain = string.Concat(Regex.Split(id, "[^A-Za-z0-9]+").Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        return plain.Length == 0 || char.IsDigit(plain[0]) ? "_" + plain : plain;
    }
}
