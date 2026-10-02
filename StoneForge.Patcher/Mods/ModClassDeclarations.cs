using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StoneForge.Patcher;

/// <summary>A class in a mod's source that may be one of its consumables or skills: a class over StoneForge's
/// Consumable / ModSkill - <c>class Tonic : Consumable { public Tonic() : base("my_tonic", "wine") { } }</c> - or over
/// one of the game's (StoneForge.GameItems / GameSkills: <c>class Tonic : Wine { public Tonic() : base("my_tonic") { }
/// }</c>), its key (and what it's based on) given as string literals. Which it is - if either - is told once the game
/// data's read (<see cref="ConsumableObjects"/>, <see cref="SkillObjects"/>: by its base class), and each gets its
/// own objects in the game data, so the game handles it as its own. <see cref="Key"/> is the game's name for it: the
/// mod's ID (its mod.json) and the key, "examplemod__my_tonic".</summary>
internal sealed record ModClassDeclaration(string Key, string BaseType, string? BasedOn)
{
    public override string ToString() => $"{Key}|{BaseType}|{BasedOn}";

    public static ModClassDeclaration? Parse(string line)
    {
        var parts = line.Split('|');
        return parts.Length == 3 && parts[0].Length > 0 ? new ModClassDeclaration(parts[0], parts[1], parts[2].Length > 0 ? parts[2] : null) : null;
    }

    /// <summary>The mods' declarations, with every one of a kind made before (in <paramref name="knownFile"/>), whose
    /// objects stay so saves with them still load once their mod's gone. By key, the mods' winning; sorted.</summary>
    public static List<ModClassDeclaration> WithKnown(List<ModClassDeclaration> declared, string knownFile)
    {
        var byKey = new SortedDictionary<string, ModClassDeclaration>(StringComparer.Ordinal);
        if (File.Exists(knownFile))
            foreach (string line in File.ReadAllLines(knownFile))
                if (Parse(line) is { } known)
                    byKey[known.Key] = known;
        foreach (var declaration in declared)
            byKey[declaration.Key] = declaration;
        return byKey.Values.ToList();
    }

    /// <summary>The ones of a kind the game data now has objects for, kept for every build after.</summary>
    public static void Remember(string knownFile, List<ModClassDeclaration> added)
        => File.WriteAllLines(knownFile, added.Select(d => d.ToString()));

    /// <summary>Every class in the mods' source with a base(...) constructor call whose first argument is a string
    /// literal: the key (with its mod's ID) - and for StoneForge's Consumable or ModSkill itself, the second (or
    /// basedOn:), what it's based on.</summary>
    public static List<ModClassDeclaration> Declared(string modsDir)
    {
        var declared = new List<ModClassDeclaration>();
        foreach (var (_, modId, files) in ModSources.Mods(modsDir))
        foreach (string file in files)
        {
            CompilationUnitSyntax root;
            try { root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot(); }
            catch (Exception e)
            {
                PatcherConsole.Log($"  couldn't read {Path.GetFileName(file)}: {e.Message}");
                continue;
            }
            foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                string? baseType = type.BaseList?.Types.FirstOrDefault()?.Type switch
                {
                    IdentifierNameSyntax name => name.Identifier.ValueText,
                    QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                    AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
                    _ => null,
                };
                if (baseType == null)
                    continue;
                foreach (var constructor in type.Members.OfType<ConstructorDeclarationSyntax>())
                {
                    if (constructor.Initializer is not { } init || !init.ThisOrBaseKeyword.IsKind(SyntaxKind.BaseKeyword))
                        continue;
                    var args = init.ArgumentList.Arguments;
                    if (args.Count == 0 || Literal(args[0]) is not string key || key.Length == 0)
                        continue;
                    string? basedOn = null;
                    if (baseType is "Consumable" or "ModSkill")
                    {
                        var basedOnArg = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "basedOn") ?? (args.Count > 1 ? args[1] : null);
                        basedOn = basedOnArg == null ? null : Literal(basedOnArg);
                        if (basedOn == null)
                        {
                            PatcherConsole.Log($"  {Path.GetFileName(file)}: \"{key}\" - what it's based on must be a string literal");
                            continue;
                        }
                    }
                    declared.Add(new ModClassDeclaration(ModIdentity.GameKey(modId, key), baseType, basedOn));
                    break;
                }
            }
        }
        return declared;
    }

    private static string? Literal(ArgumentSyntax argument)
        => argument.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) ? literal.Token.ValueText : null;
}
