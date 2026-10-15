
using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>Editing the game's GML scripts (UndertaleModLib: decompile, edit, compile).</summary>
internal static class ScriptEditor
{
    /// <summary>Code put right after the opening brace of a script's function - a function defined in another script's
    /// file too (edited in that file). Throws if the script can't be edited (no such script, or one no decompiler
    /// handles - those using try/catch).</summary>
    public static void InsertAtBodyStart(GameDataEditor editor, string name, string code)
    {
        string codeName = editor.ScriptFile(name);
        string text = editor.ReadGml(codeName).Replace("\r\n", "\n");
        var lines = new List<string>(text.Split('\n'));
        int fn = lines.FindIndex(l => l.TrimStart().StartsWith("function " + name + "("));
        int brace = fn < 0 ? -1 : lines.FindIndex(fn, l => l.Trim() == "{");
        if (brace < 0)
            throw new InvalidOperationException("can't find the body of " + name);
        lines.Insert(brace + 1, code);
        editor.ReplaceGml(codeName, string.Join("\n", lines));
    }

    /// <summary>Code (top-level GML, no local variables) put at the start of a script's function as bytecode, without
    /// recompiling the file it's in: a file defining many functions (the game's DialogueFlow) recompiled whole bound
    /// calls between its own functions differently, and the game's dialogue broke (#21). Its compiled instructions go in
    /// at the function's first instruction; jumps across that point, the try/catch addresses the file pushes for
    /// @@try_hook@@, and the functions after it move along. A function that's its own file's only code (no parent
    /// entry) is edited as GML (InsertAtBodyStart).</summary>
    public static void InsertBytecodeAtFunctionStart(GameDataEditor editor, string name, string code)
    {
        var function = editor.GetCode("gml_Script_" + name);
        if (function?.ParentEntry is not { } file)
        {
            InsertAtBodyStart(editor, name, code);
            return;
        }
        var stub = editor.CompileFragment(code);
        uint stubSize = (uint)stub.Sum(i => i.CalculateInstructionSize());
        // Where the function starts, in the file's instructions (addresses in 4-byte units, as jumps count them).
        uint at = function.Offset / 4;
        var addresses = new List<uint>(file.Instructions.Count);
        uint address = 0;
        int index = -1;
        foreach (var instruction in file.Instructions)
        {
            if (address == at && index < 0)
                index = addresses.Count;
            addresses.Add(address);
            address += instruction.CalculateInstructionSize();
        }
        if (index < 0)
            throw new InvalidOperationException($"can't find where {name} starts in {file.Name.Content}");
        // Jumps across the start: a jump to it lands on the stub (the function's start now) - anything after moves.
        for (int i = 0; i < file.Instructions.Count; i++)
        {
            var instruction = file.Instructions[i];
            if (!Jumps(instruction))
                continue;
            long source = addresses[i], target = source + instruction.JumpOffset;
            long movedSource = source >= at ? source + stubSize : source;
            long movedTarget = target > at ? target + stubSize : target;
            instruction.JumpOffset = (int)(movedTarget - movedSource);
        }
        // try/catch: @@try_hook@@(finally, catch) - absolute byte addresses in the file, pushed just before the call.
        for (int i = 0; i < file.Instructions.Count; i++)
        {
            var call = file.Instructions[i];
            if (call.Kind != UndertaleInstruction.Opcode.Call || call.ValueFunction?.Name?.Content != "@@try_hook@@")
                continue;
            for (int j = i - 1, found = 0; j >= 0 && found < 2 && j >= i - 6; j--)
            {
                var push = file.Instructions[j];
                if (push.Kind != UndertaleInstruction.Opcode.Push || push.Type1 != UndertaleInstruction.DataType.Int32)
                    continue;
                found++;
                if (push.ValueInt > at * 4)
                    push.ValueInt += (int)(stubSize * 4);
            }
        }
        file.Instructions.InsertRange(index, stub);
        // The functions after it start further on.
        foreach (var child in file.ChildEntries)
            if (child.Offset > at * 4)
                child.Offset += stubSize * 4;
        file.UpdateLength();
    }

    // Instructions with a relative jump (b, bt, bf, pushenv, popenv - but not popenv's exit marker).
    private static bool Jumps(UndertaleInstruction instruction) => instruction.Kind switch
    {
        UndertaleInstruction.Opcode.B or UndertaleInstruction.Opcode.Bt or UndertaleInstruction.Opcode.Bf
            or UndertaleInstruction.Opcode.PushEnv => true,
        UndertaleInstruction.Opcode.PopEnv => !instruction.JumpOffsetPopenvExitMagic,
        _ => false,
    };
}
