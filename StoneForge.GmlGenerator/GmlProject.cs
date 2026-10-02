using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace StoneForge.Gml;

// Shared by the patcher, compiler and IDE generator. No filesystem access: the host supplies inputs.
// A mod's GML is its folder's GML\**\*.gml; its bindings are named after the folder: mods\ExampleMod gives
// ExampleMod.Gml (Gml.Twice(21) inside the mod's own namespace).
public sealed class GmlProject
{
    public const string BindingClass = "Gml";
    /// <summary>The mod's folder name ("ExampleMod"): what its GML is known by.</summary>
    public string Name { get; private set; } = "";
    /// <summary>The bindings' namespace: the folder name as a C# name ("my-mod" gives MyMod).</summary>
    public string Namespace { get; private set; } = "";
    public string Binding => Namespace + "." + BindingClass;
    public string Fingerprint { get; private set; } = "";
    public List<GmlFunction> Functions { get; } = new List<GmlFunction>();

    public static GmlProject Parse(string folderName, IEnumerable<KeyValuePair<string, string>> files)
    {
        if (string.IsNullOrWhiteSpace(folderName)) throw new ArgumentException("A mod's GML needs its folder's name.");
        var p = new GmlProject { Name = folderName, Namespace = NamespaceFor(folderName) };
        var ordered = files.OrderBy(f => f.Key, StringComparer.Ordinal).ToArray();
        p.Fingerprint = Hash(folderName + string.Concat(ordered.Select(f => "\n" + f.Key + "\n" + f.Value)));
        foreach (var file in ordered)
        {
            try { p.Functions.Add(ParseFunction(file.Key, file.Value)); }
            catch (Exception e) { throw new ArgumentException(file.Key + ": " + e.Message, e); }
        }
        if (p.Functions.GroupBy(f => f.Name).Any(g => g.Count() > 1)) throw new ArgumentException(folderName + ": duplicate GML export name.");
        p.CompileOrder = p.OrderByCalls();
        return p;
    }

    // A folder name as a C# namespace: its words joined, each capitalised ("my-mod" -> MyMod; "2d fx" -> _2dFx).
    public static string NamespaceFor(string folderName)
    {
        string name = string.Concat(Regex.Split(folderName, "[^A-Za-z0-9_]+").Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        if (name.Length == 0 || char.IsDigit(name[0]) || !Identifier(name)) name = "_" + name;
        return name;
    }

    // The mod folder a GML file belongs to: the folder holding its GML folder (mods\ExampleMod\GML\Add.gml ->
    // mods\ExampleMod), or null if it isn't in one.
    public static string? ModFolderOf(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        for (int i = parts.Length - 2; i >= 1; i--)
            if (string.Equals(parts[i], "GML", StringComparison.OrdinalIgnoreCase))
                return string.Join("/", parts, 0, i);
        return null;
    }

    public string InternalName(string function) => "sf_gml_" + Hash(Name.ToLowerInvariant()).Substring(0, 24) + "_" + function;
    public static string Hash(string text)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }
    private static bool Identifier(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")
        && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;
    private static bool Type(string name, bool returns) => name == "double" || name == "int" || name == "bool"
        || name == "string" || name == "GmValue" || (returns && name == "void");

    private static GmlFunction ParseFunction(string path, string source)
    {
        var tokens = Tokens(source);
        if (tokens.Count < 6 || tokens[0].Text != "function" || !Identifier(tokens[1].Text) || tokens[2].Text != "(")
            throw new ArgumentException("Expected one named function per file; top-level executable statements are not supported.");
        var f = new GmlFunction { Name = tokens[1].Text, Path = path, Source = source };
        int pos = 3;
        while (pos < tokens.Count && tokens[pos].Text != ")")
        {
            if (!Identifier(tokens[pos].Text)) throw new ArgumentException("Only plain parameter names are supported.");
            f.Parameters.Add(new KeyValuePair<string, string>(tokens[pos++].Text, ""));
            if (pos < tokens.Count && tokens[pos].Text == ")") break;
            if (pos >= tokens.Count || tokens[pos++].Text != "," || pos >= tokens.Count || tokens[pos].Text == ")")
                throw new ArgumentException("Invalid parameter list.");
        }
        if (++pos >= tokens.Count || tokens[pos].Text != "{") throw new ArgumentException("Missing function body.");
        int depth = 0;
        for (int i = pos; i < tokens.Count; i++)
        {
            if (tokens[i].Text == "{") depth++;
            if (tokens[i].Text == "}") depth--;
            if (depth == 0 && i != tokens.Count - 1) throw new ArgumentException("Only one function is allowed; no trailing top-level code.");
        }
        if (depth != 0) throw new ArgumentException("Unbalanced function braces.");
        var annotations = Regex.Matches(source.Substring(0, tokens[0].Start), @"(?m)^\s*///\s*@stoneforge\s+(return|param)\s+([^\r\n]+)");
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match annotation in annotations)
        {
            var words = annotation.Groups[2].Value.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (annotation.Groups[1].Value == "return" && words.Length == 1 && Type(words[0], true) && f.ReturnType.Length == 0) f.ReturnType = words[0];
            else if (annotation.Groups[1].Value == "param" && words.Length == 2 && Type(words[1], false)) parameters.Add(words[0], words[1]);
            else throw new ArgumentException("Invalid or duplicate @stoneforge type annotation.");
        }
        if (f.ReturnType.Length == 0 || parameters.Count != f.Parameters.Count) throw new ArgumentException("Specify @stoneforge return TYPE and @stoneforge param NAME TYPE for every parameter.");
        for (int i = 0; i < f.Parameters.Count; i++)
        {
            string name = f.Parameters[i].Key;
            if (!parameters.TryGetValue(name, out var type)) throw new ArgumentException("Missing type for " + name);
            f.Parameters[i] = new KeyValuePair<string, string>(name, type);
        }
        return f;
    }

    /// <summary>The functions in the order the game's compiler needs them: each after the functions it calls (it
    /// compiles a call to a function it hasn't seen yet as a call to nothing). A function may call itself; two
    /// that call each other, directly or round a loop, are rejected.</summary>
    public IReadOnlyList<GmlFunction> CompileOrder { get; private set; } = Array.Empty<GmlFunction>();

    private List<GmlFunction> OrderByCalls()
    {
        var byName = Functions.ToDictionary(f => f.Name, StringComparer.Ordinal);
        var order = new List<GmlFunction>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 visiting, 2 done
        var path = new List<string>();
        void Visit(GmlFunction f)
        {
            if (state.TryGetValue(f.Name, out int s) && s == 2) return;
            state[f.Name] = 1; path.Add(f.Name);
            foreach (string callee in Calls(f, byName.Keys))
            {
                if (callee == f.Name) continue;
                if (state.TryGetValue(callee, out int c) && c == 1)
                {
                    var loop = path.Skip(path.IndexOf(callee)).Concat(new[] { callee });
                    throw new ArgumentException(Name + ": GML functions can't call each other in a loop (" + string.Join(" -> ", loop)
                        + "); the game's compiler needs each function after the ones it calls. A function may call itself.");
                }
                Visit(byName[callee]);
            }
            path.RemoveAt(path.Count - 1); state[f.Name] = 2; order.Add(f);
        }
        foreach (var f in Functions) Visit(f);
        return order;
    }

    // The other exports a function refers to (by its readable names).
    private static IEnumerable<string> Calls(GmlFunction function, IEnumerable<string> exports)
    {
        var names = new HashSet<string>(exports, StringComparer.Ordinal);
        var tokens = Tokens(function.Source);
        var found = new List<string>();
        for (int i = 2; i < tokens.Count; i++)
            if (names.Contains(tokens[i].Text) && !IsMember(tokens, i) && !IsField(tokens, i) && !found.Contains(tokens[i].Text))
                found.Add(tokens[i].Text);
        return found;
    }

    // obj.name - a variable of something, not ours to rename.
    private static bool IsMember(List<Token> tokens, int i) => i > 0 && tokens[i - 1].Text == ".";
    // { name: value } - a struct's field (not "a ? name : b", nor "case name:").
    private static bool IsField(List<Token> tokens, int i) => i > 0 && i + 1 < tokens.Count && tokens[i + 1].Text == ":"
        && (tokens[i - 1].Text == "{" || tokens[i - 1].Text == ",");

    /// <summary>The function as the game compiles it: exports by their internal names, and its parameters as
    /// argument0, argument1... (the game's compiler reads named parameters as the caller's variables).</summary>
    public string Rewrite(GmlFunction function)
    {
        var names = new HashSet<string>(Functions.Select(f => f.Name), StringComparer.Ordinal);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int p = 0; p < function.Parameters.Count; p++) parameters[function.Parameters[p].Key] = "argument" + p;
        var tokens = Tokens(function.Source);
        var result = new StringBuilder(); int end = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i]; result.Append(function.Source, end, t.Start - end);
            bool plain = !IsMember(tokens, i) && !IsField(tokens, i);
            result.Append(plain && parameters.TryGetValue(t.Text, out var argument) ? argument
                : plain && names.Contains(t.Text) ? InternalName(t.Text) : t.Text);
            end = t.Start + t.Text.Length;
        }
        return result.Append(function.Source.Substring(end)).ToString();
    }

    public string Bindings()
    {
        var s = new StringBuilder("// <auto-generated/>\nnamespace " + Namespace + " {\n/// <summary>The " + Name
            + " mod's GML functions (its GML folder). Call them on the game thread; they run with global self.</summary>\npublic static class " + BindingClass + " {\n");
        foreach (var f in Functions)
        {
            string type = f.ReturnType == "GmValue" ? "global::StoneForge.GmValue" : f.ReturnType;
            s.Append("public static ").Append(type).Append(' ').Append(f.Name).Append('(');
            s.Append(string.Join(", ", f.Parameters.Select(p => (p.Value == "GmValue" ? "global::StoneForge.GmValue" : p.Value) + " " + p.Key)));
            s.Append(") { ");
            if (type != "void") s.Append("return ");
            s.Append("global::StoneForge.GmlScripts.Call(").Append(Literal(Name)).Append(", \"").Append(Fingerprint).Append("\", \"").Append(InternalName(f.Name)).Append('"');
            foreach (var p in f.Parameters) s.Append(", ").Append(p.Key);
            s.Append(')');
            s.Append(f.ReturnType == "int" ? ".AsInt" : f.ReturnType == "double" ? ".AsReal" : f.ReturnType == "bool" ? ".AsBool" : f.ReturnType == "string" ? ".AsString" : "");
            s.Append("; }\n");
        }
        return s.Append("} }\n").ToString();
    }

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, true);

    private sealed class Token { public int Start; public string Text = ""; }
    private static List<Token> Tokens(string source)
    {
        var result = new List<Token>();
        for (int i = 0; i < source.Length;)
        {
            if (char.IsWhiteSpace(source[i])) { i++; continue; }
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/') { while (i < source.Length && source[i] != '\n') i++; continue; }
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            { int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end < 0) throw new ArgumentException("Unterminated comment."); i = end + 2; continue; }
            int start = i; char c = source[i++];
            if (c == '@' || c == '$') throw new ArgumentException("Verbatim/interpolated strings are not supported yet; use ordinary quoted strings.");
            if (c == '\'' || c == '"')
            {
                bool closed = false;
                while (i < source.Length) { char next = source[i++]; if (next == '\\' && i < source.Length) i++; else if (next == c) { closed = true; break; } }
                if (!closed) throw new ArgumentException("Unterminated string.");
            }
            else if (char.IsLetter(c) || c == '_') while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
            result.Add(new Token { Start = start, Text = source.Substring(start, i - start) });
        }
        return result;
    }
}

public sealed class GmlFunction
{
    public string Name = "", Path = "", Source = "", ReturnType = "";
    public List<KeyValuePair<string, string>> Parameters { get; } = new List<KeyValuePair<string, string>>();
}
