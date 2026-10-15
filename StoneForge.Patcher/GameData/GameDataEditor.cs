using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>One patch operation's game data. Uses UndertaleModLib directly, with no global state: GML is compiled
/// with its <see cref="CodeImportGroup"/> (one per edit, so each fails on its own with its name) and decompiled with
/// Underanalyzer.</summary>
internal sealed class GameDataEditor
{
    public UndertaleData Data { get; }

    public GameDataEditor(UndertaleData data)
    {
        // (The native - YYC - build has no GML in its data at all: only its objects, sprites and rooms are edited there.
        // A VM build without code entries is something else.)
        if (!data.IsYYC() && (data.Code == null || data.Code.Count == 0))
            throw new InvalidOperationException("Stoneshard's game data has no code entries and isn't the native build - restore it (Steam: Verify integrity of game files).");
        Data = data;
    }

    public UndertaleGameObject GetObject(string name) => Data.GameObjects.ByName(name)
        ?? throw new InvalidOperationException($"Game object '{name}' was not found.");
    public UndertaleSprite GetSprite(string name) => Data.Sprites.ByName(name)
        ?? throw new InvalidOperationException($"Game sprite '{name}' was not found.");

    public UndertaleGameObject AddObject(string name)
    {
        // Idempotent for the loader's shared object definitions, as in previous releases.
        if (Data.GameObjects.ByName(name) is { } existing) return existing;
        var obj = new UndertaleGameObject
        {
            Name = Data.Strings.MakeString(name), Visible = false, Persistent = false,
            Awake = false, CollisionShape = CollisionShapeFlags.Circle
        };
        Data.GameObjects.Add(obj);
        return obj;
    }

    public string ReadGml(string name)
        => new DecompileContext(new GlobalDecompileContext(Data), GetCode(name), new DecompileSettings()).DecompileToString();

    public void ReplaceGml(string name, string source)
    {
        var code = GetCode(name);
        if (code.ParentEntry != null)
            throw new InvalidOperationException($"Edit the parent script of '{name}', not its child entry.");
        Import(name, source);
    }

    /// <summary>The code entry that declares function <paramref name="name"/>: its global script - gml_GlobalScript_&lt;name&gt;,
    /// or another's for a function defined in another script's file (scr_rewards_find_guinnel_1, in
    /// gml_GlobalScript_scr_rewards_find_guinnel).</summary>
    public string ScriptFile(string name)
        => Data.Code.ByName("gml_Script_" + name)?.ParentEntry?.Name?.Content ?? "gml_GlobalScript_" + name;

    /// <summary>A global script declaring function <paramref name="name"/> (gml_GlobalScript_&lt;name&gt;, which the
    /// game runs at start to define it).</summary>
    public UndertaleCode AddFunction(string source, string name)
    {
        if (Data.Code.ByName("gml_Script_" + name) != null)
            throw new InvalidOperationException($"Function '{name}' already exists.");
        string entry = "gml_GlobalScript_" + name;
        if (Data.Code.ByName(entry) != null)
            throw new InvalidOperationException($"Code '{entry}' already exists.");
        Import(entry, source);
        var code = GetCode(entry);
        if (!code.ChildEntries.Any(c => c.Name.Content == "gml_Script_" + name))
            throw new InvalidOperationException($"Source for '{name}' did not declare that function.");
        return code;
    }

    public void AddNewEvent(string objectName, string source, EventType type, uint subtype)
        => AddNewEvent(GetObject(objectName), source, type, subtype);

    public void AddNewEvent(UndertaleGameObject obj, string source, EventType type, uint subtype)
    {
        int index = (int)type;
        if (index < 0 || index >= obj.Events.Count) throw new ArgumentOutOfRangeException(nameof(type));
        if (obj.Events[index].Any(e => e.EventSubtype == subtype))
            throw new InvalidOperationException($"Event {obj.Name.Content}.{type}_{subtype} already exists.");
        // (The import makes the code entry and links it to the object's event.)
        Import($"gml_Object_{obj.Name.Content}_{type}_{subtype}", source);
    }

    internal UndertaleCode GetCode(string name) => Data.Code.ByName(name)
        ?? throw new InvalidOperationException($"Game code '{name}' was not found.");

    /// <summary>Code compiled on its own, as top-level code (no function, no local variables - it's put into another
    /// entry's instructions, which has its own), for splicing in: its instructions, without the closing exit.</summary>
    public List<UndertaleInstruction> CompileFragment(string source)
    {
        const string scratch = "stoneforge_fragment";
        var code = UndertaleCode.CreateEmptyEntry(Data, scratch);
        try
        {
            bool shortCircuit = Data.ShortCircuit;
            try
            {
                Data.ShortCircuit = true;
                var group = new CodeImportGroup(Data) { AutoCreateAssets = true };
                group.QueueReplace(code, source);
                var result = group.Import(throwOnFailedCompile: false);
                if (!result.Successful)
                    throw new InvalidOperationException("GML compilation failed for a fragment: " + result.PrintAllErrors(false));
            }
            finally { Data.ShortCircuit = shortCircuit; }
            if (code.LocalsCount > 0 && code.FindReferencedLocalVars().Count > 0)
                throw new InvalidOperationException("A fragment to splice in can't use local variables.");
            var instructions = code.Instructions.ToList();
            if (instructions.Count > 0 && instructions[^1].Kind == UndertaleInstruction.Opcode.Exit)
                instructions.RemoveAt(instructions.Count - 1);
            return instructions;
        }
        finally
        {
            Data.Code.Remove(code);
            if (Data.CodeLocals?.ByName(scratch) is { } locals)
                Data.CodeLocals.Remove(locals);
        }
    }

    // The code entry replaced with this GML (made, and linked to its script or object event, if new).
    private void Import(string entry, string source)
    {
        // The reader infers this flag from any eager boolean instruction in the entire file.
        // MSL packages can introduce such instructions into an otherwise modern game. That must not
        // turn the decompiled source's &&/|| guards into eager reads when we recompile unrelated scripts.
        bool shortCircuit = Data.ShortCircuit;
        try
        {
            Data.ShortCircuit = true;
            var group = new CodeImportGroup(Data) { AutoCreateAssets = true };
            group.QueueReplace(entry, source);
            var result = group.Import(throwOnFailedCompile: false);
            if (!result.Successful)
                throw new InvalidOperationException($"GML compilation failed for '{entry}': {result.PrintAllErrors(false)}");
        }
        finally { Data.ShortCircuit = shortCircuit; }
    }
}
