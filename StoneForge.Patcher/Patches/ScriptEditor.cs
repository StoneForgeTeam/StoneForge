
namespace StoneForge.Patcher;

/// <summary>Editing the game's GML scripts (UndertaleModLib: decompile, edit, compile).</summary>
internal static class ScriptEditor
{
    /// <summary>Code put right after the opening brace of a script's function. Throws if the script can't be
    /// edited (no such script, or one no decompiler handles - those using try/catch).</summary>
    public static void InsertAtBodyStart(GameDataEditor editor, string name, string code)
    {
        string codeName = "gml_GlobalScript_" + name;
        string text = editor.ReadGml(codeName).Replace("\r\n", "\n");
        var lines = new List<string>(text.Split('\n'));
        int fn = lines.FindIndex(l => l.TrimStart().StartsWith("function " + name + "("));
        int brace = fn < 0 ? -1 : lines.FindIndex(fn, l => l.Trim() == "{");
        if (brace < 0)
            throw new InvalidOperationException("can't find the body of " + name);
        lines.Insert(brace + 1, code);
        editor.ReplaceGml(codeName, string.Join("\n", lines));
    }
}
