using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>Mods' own game objects (StoneForge's GameObject, <see cref="ModClassDeclaration"/>): for each,
/// o_&lt;key&gt; - a child of the game object it names (or of none) - with every event StoneForge runs C# for
/// (GameObjects): Create, Destroy, Clean Up, the Steps, the Draws, the 12 alarms, the 16 user events and the mouse's
/// presses and hovering. Each runs its parent's (<c>event_inherited()</c>) - or, a Draw with no parent's to run,
/// draws the sprite as GameMaker does for an object without one. Its sprite and flags are set when the game runs.</summary>
internal static class ModGameObjects
{
    private static readonly (EventType Type, uint Subtype)[] Events = BuildEvents();

    private static (EventType, uint)[] BuildEvents()
    {
        var events = new List<(EventType, uint)>
        {
            (EventType.Create, 0), (EventType.Destroy, 0), (EventType.CleanUp, 0),
            (EventType.Step, 0), (EventType.Step, 1), (EventType.Step, 2),
            (EventType.Draw, 0), (EventType.Draw, 64), (EventType.Draw, 72), (EventType.Draw, 73),
            (EventType.Mouse, 4), (EventType.Mouse, 5), (EventType.Mouse, 10), (EventType.Mouse, 11),
        };
        for (uint alarm = 0; alarm < 12; alarm++)
            events.Add((EventType.Alarm, alarm));
        for (uint user = 10; user < 26; user++)
            events.Add((EventType.Other, user));
        return events.ToArray();
    }

    // The objects added.
    public static List<ModClassDeclaration> Add(GameDataEditor editor, List<ModClassDeclaration> declarations)
    {
        var data = editor.Data;
        var made = new List<ModClassDeclaration>();
        foreach (var declaration in declarations.Where(d => d.BaseType == "GameObject"))
        {
            string name = "o_" + declaration.Key;
            if (!Regex.IsMatch(declaration.Key, "^[A-Za-z0-9_]+$") || data.GameObjects.ByName(name) != null)
            {
                PatcherConsole.Log($"  object \"{declaration.Key}\": not a key, or the game already has an object called that - not added");
                continue;
            }
            UndertaleGameObject? parent = null;
            if (!string.IsNullOrEmpty(declaration.BasedOn))
            {
                parent = data.GameObjects.ByName(declaration.BasedOn);
                if (parent == null)
                {
                    PatcherConsole.Log($"  object \"{declaration.Key}\": the game has no object \"{declaration.BasedOn}\" to be a child of - not added");
                    continue;
                }
            }
            var obj = editor.AddObject(name);
            obj.ParentId = parent;
            obj.Sprite = parent?.Sprite;
            obj.Visible = true;
            obj.Persistent = false;
            foreach (var (type, subtype) in Events)
            {
                string code = type == EventType.Draw && subtype == 0 && !Inherits(parent, EventType.Draw, 0)
                    ? "draw_self()"
                    : "event_inherited()";
                editor.AddNewEvent(obj, code, type, subtype);
            }
            PatcherConsole.Log($"  object {name} (child of {(parent == null ? "nothing" : parent.Name.Content)}): added");
            made.Add(declaration);
        }
        return made;
    }

    // Whether an object or one of its ancestors has an event (what event_inherited() would run).
    private static bool Inherits(UndertaleGameObject? obj, EventType type, uint subtype)
    {
        for (int depth = 0; obj != null && depth < 64; obj = obj.ParentId, depth++)
            if (obj.Events[(int)type].Any(e => e.EventSubtype == subtype))
                return true;
        return false;
    }
}
