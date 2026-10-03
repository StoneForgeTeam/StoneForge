using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StoneForge.Loader;

// What a mod's source may use, checked before it's compiled into anything that runs (ModCompiler).
//
// .NET has no in-process sandbox any more (no code access security, no AppDomains), so the line is drawn at
// compile time: mods are C# source, compiled here against a few assemblies, and every symbol the source
// touches - each name it binds to, each expression's type and converted type, each implicit call (foreach,
// operators, conversions, collection initializers, query clauses), each type it declares or derives from -
// must be on an allowlist. Anything not listed is refused: reflection and Type, interop and pointers, unsafe
// code, dynamic, threads and tasks, System.IO (files go through ModFiles), processes, networking,
// Environment, GC, Activator, expression trees, finalizers, extern / DllImport. On top of the language,
// Game.CallBuiltin refuses the GameMaker functions that reach outside the game (BuiltinPolicy).
//
// Limits (what this can't do): it doesn't bound CPU or memory - a mod can still hang or exhaust the game.
// It's an allowlist, so a gap means something useful is refused rather than something dangerous allowed,
// but the loader's own public API is part of the surface too: anything it exposes, mods can use.
internal static class ModSecurity
{
    // Namespaces whose every type is allowed.
    private static readonly HashSet<string> AllowedNamespaces = new(StringComparer.Ordinal)
    {
        "System.Collections",
        "System.Collections.Generic",
        "System.Collections.ObjectModel",
        "System.Text",
        "System.Text.RegularExpressions",
        "System.Globalization",
        "StoneForge",
        "StoneForge.Objects",
        "StoneForge.GameItems",
        "StoneForge.GameSkills",
        "StoneForge.GameDamageTypes",
    };

    // Single types allowed in otherwise closed namespaces (by metadata name, generic arity as `n).
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        // System: values, text, maths, time, delegates, exceptions, small helpers.
        "System.Object", "System.ValueType", "System.Enum", "System.String", "System.Boolean", "System.Char",
        "System.Byte", "System.SByte", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal", "System.Half",
        "System.Void", "System.Nullable`1", "System.Array", "System.Math", "System.MathF", "System.Random",
        "System.Convert", "System.BitConverter", "System.HashCode", "System.Guid", "System.DateTime",
        "System.DateTimeKind", "System.DateTimeOffset", "System.TimeSpan", "System.DayOfWeek",
        "System.MidpointRounding", "System.StringComparer", "System.StringComparison", "System.StringSplitOptions",
        "System.Index", "System.Range", "System.Span`1", "System.ReadOnlySpan`1", "System.Memory`1",
        "System.ReadOnlyMemory`1", "System.MemoryExtensions", "System.Lazy`1", "System.Tuple",
        "System.FormattableString", "System.IFormattable", "System.IFormatProvider", "System.IComparable",
        "System.IComparable`1", "System.IEquatable`1", "System.IDisposable", "System.ICloneable",
        "System.Attribute", "System.AttributeUsageAttribute", "System.AttributeTargets", "System.FlagsAttribute",
        "System.ObsoleteAttribute", "System.EventArgs", "System.EventHandler", "System.EventHandler`1",
        "System.Predicate`1", "System.Comparison`1", "System.Converter`2",
        "System.Exception", "System.SystemException", "System.ArgumentException", "System.ArgumentNullException",
        "System.ArgumentOutOfRangeException", "System.InvalidOperationException", "System.NotSupportedException",
        "System.NotImplementedException", "System.IndexOutOfRangeException", "System.FormatException",
        "System.OverflowException", "System.DivideByZeroException", "System.NullReferenceException",
        "System.InvalidCastException", "System.ObjectDisposedException",
        // Delegates are types too (their dangerous members are denied below).
        "System.Delegate", "System.MulticastDelegate",
        // LINQ to objects (not Queryable: that's expression trees).
        "System.Linq.Enumerable", "System.Linq.IGrouping`2", "System.Linq.ILookup`2", "System.Linq.Lookup`2",
        "System.Linq.IOrderedEnumerable`1",
        // System.IO.Path only: string helpers for paths (reading and writing go through ModFiles).
        "System.IO.Path",
        // What the compiler itself uses for interpolated strings and init-only setters.
        "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler",
        "System.Runtime.CompilerServices.IsExternalInit",
        "System.Runtime.CompilerServices.ITuple",
    };

    // Allowed type families: every System.Func / Action / ValueTuple / Tuple arity.
    private static readonly string[] AllowedTypePrefixes = { "System.Func`", "System.Action", "System.ValueTuple", "System.Tuple`" };

    // Members of allowed types that are still refused (by containing type and name).
    private static readonly HashSet<string> DeniedMembers = new(StringComparer.Ordinal)
    {
        "System.Delegate.DynamicInvoke", "System.Delegate.Method", "System.Delegate.Target",
        "System.Delegate.CreateDelegate", "System.Delegate.GetInvocationList", "System.MulticastDelegate.GetInvocationList",
        "System.Object.GetType", "System.Exception.TargetSite", "System.Exception.GetType",
        "System.Array.CreateInstance", "System.Array.CreateInstanceFromArrayType",
        "System.Enum.GetUnderlyingType", "System.Convert.ChangeType",
        // (Trusted mods' only.)
        "StoneForge.Game.CallBuiltinUnrestricted",
    };

    // The loader itself (StoneForge.Loader - a separate assembly mods aren't even compiled against): never theirs.
    private const string LoaderNamespace = "StoneForge.Loader.";

    // The only attributes a mod may put on its assembly or module.
    private static readonly HashSet<string> AllowedAssemblyAttributes = new(StringComparer.Ordinal) { "StoneForge.HookScriptAttribute" };

    /// <summary>Every violation in the compilation's source (empty: allowed).</summary>
    internal static List<string> Check(CSharpCompilation compilation)
    {
        var problems = new List<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: false);
            new Walker(compilation, model, problems).Visit(tree.GetRoot());
        }
        return problems;
    }

    private sealed class Walker : CSharpSyntaxWalker
    {
        private readonly CSharpCompilation _compilation;
        private readonly SemanticModel _model;
        private readonly List<string> _problems;
        private readonly HashSet<(string, int)> _seen = new();

        internal Walker(CSharpCompilation compilation, SemanticModel model, List<string> problems)
        {
            _compilation = compilation;
            _model = model;
            _problems = problems;
        }

        private void Deny(SyntaxNode node, string what)
        {
            var span = node.GetLocation().GetLineSpan();
            int line = span.StartLinePosition.Line + 1;
            if (_seen.Add((what, line)))
                _problems.Add($"{Path.GetFileName(span.Path)}({line}): {what}");
        }

        public override void Visit(SyntaxNode? node)
        {
            if (node == null)
                return;
            CheckSyntax(node);
            CheckSemantics(node);
            base.Visit(node);
        }

        // Language features refused outright.
        private void CheckSyntax(SyntaxNode node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.UnsafeStatement:
                case SyntaxKind.FixedStatement:
                case SyntaxKind.PointerType:
                case SyntaxKind.FunctionPointerType:
                case SyntaxKind.AddressOfExpression:
                case SyntaxKind.PointerIndirectionExpression:
                case SyntaxKind.PointerMemberAccessExpression:
                    Deny(node, "unsafe code and pointers aren't allowed");
                    return;
                case SyntaxKind.ArgListExpression:
                case SyntaxKind.MakeRefExpression:
                case SyntaxKind.RefTypeExpression:
                case SyntaxKind.RefValueExpression:
                    Deny(node, "__arglist / __makeref aren't allowed");
                    return;
                case SyntaxKind.DestructorDeclaration:
                    Deny(node, "finalizers aren't allowed (they run off the game's thread)");
                    return;
                case SyntaxKind.TypeOfExpression:
                    Deny(node, "typeof isn't allowed (no reflection)");
                    return;
                case SyntaxKind.AwaitExpression:
                    Deny(node, "async / await isn't allowed (mods run on the game's thread)");
                    return;
            }
            if (node is MemberDeclarationSyntax member)
            {
                foreach (var modifier in member.Modifiers)
                {
                    if (modifier.IsKind(SyntaxKind.UnsafeKeyword) || modifier.IsKind(SyntaxKind.ExternKeyword))
                        Deny(node, $"'{modifier.Text}' isn't allowed");
                    if (modifier.IsKind(SyntaxKind.AsyncKeyword))
                        Deny(node, "async isn't allowed (mods run on the game's thread)");
                }
            }
            if (node is LocalFunctionStatementSyntax local)
            {
                foreach (var modifier in local.Modifiers)
                    if (modifier.IsKind(SyntaxKind.UnsafeKeyword) || modifier.IsKind(SyntaxKind.ExternKeyword) || modifier.IsKind(SyntaxKind.AsyncKeyword))
                        Deny(node, $"'{modifier.Text}' isn't allowed");
            }
            if (node is AnonymousFunctionExpressionSyntax lambda && lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
                Deny(node, "async isn't allowed (mods run on the game's thread)");
            // A mod's own types go in its own namespace, not System's or the loader's.
            if (node is BaseNamespaceDeclarationSyntax ns)
            {
                string name = ns.Name.ToString();
                if (name == "System" || name.StartsWith("System.", StringComparison.Ordinal) || name == "Microsoft" || name.StartsWith("Microsoft.", StringComparison.Ordinal)
                    || name == "StoneForge" || name.StartsWith("StoneForge.", StringComparison.Ordinal))
                    Deny(node, $"a mod can't declare types in namespace {name}");
            }
            // Assembly / module attributes: only HookScript.
            if (node is AttributeListSyntax list && list.Target != null
                && (list.Target.Identifier.IsKind(SyntaxKind.AssemblyKeyword) || list.Target.Identifier.IsKind(SyntaxKind.ModuleKeyword)))
            {
                foreach (var attribute in list.Attributes)
                {
                    var type = _model.GetSymbolInfo(attribute).Symbol?.ContainingType;
                    if (type == null || !AllowedAssemblyAttributes.Contains(FullName(type)))
                        Deny(attribute, $"assembly attribute {attribute.Name} isn't allowed (only HookScript)");
                }
            }
        }

        // Whatever the node binds to, its types, and the implicit calls it makes.
        private void CheckSemantics(SyntaxNode node)
        {
            if (node is ExpressionSyntax or TypeSyntax or AttributeSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax)
            {
                var info = _model.GetSymbolInfo(node);
                if (info.Symbol != null)
                    CheckSymbol(node, info.Symbol);
                foreach (var candidate in info.CandidateSymbols)
                    CheckSymbol(node, candidate);
            }
            if (node is ExpressionSyntax expression)
            {
                var type = _model.GetTypeInfo(expression);
                CheckType(node, type.Type);
                CheckType(node, type.ConvertedType);
                var conversion = _model.GetConversion(expression);
                if (conversion.MethodSymbol != null)
                    CheckSymbol(node, conversion.MethodSymbol);
                if (expression is InitializerExpressionSyntax init && init.IsKind(SyntaxKind.CollectionInitializerExpression))
                    foreach (var element in init.Expressions)
                    {
                        var add = _model.GetCollectionInitializerSymbolInfo(element);
                        if (add.Symbol != null)
                            CheckSymbol(element, add.Symbol);
                    }
            }
            switch (node)
            {
                case CommonForEachStatementSyntax forEach:
                {
                    var info = _model.GetForEachStatementInfo(forEach);
                    CheckSymbol(node, info.GetEnumeratorMethod);
                    CheckSymbol(node, info.MoveNextMethod);
                    CheckSymbol(node, info.CurrentProperty);
                    CheckSymbol(node, info.DisposeMethod);
                    CheckType(node, info.ElementType);
                    if (info.IsAsynchronous)
                        Deny(node, "await foreach isn't allowed");
                    break;
                }
                case UsingStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword }:
                case LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword }:
                    Deny(node, "await using isn't allowed");
                    break;
                case QueryClauseSyntax clause:
                {
                    var info = _model.GetQueryClauseInfo(clause);
                    CheckSymbol(node, info.CastInfo.Symbol);
                    CheckSymbol(node, info.OperationInfo.Symbol);
                    break;
                }
                case SelectOrGroupClauseSyntax selectOrGroup:
                    CheckSymbol(node, _model.GetSymbolInfo(selectOrGroup).Symbol);
                    break;
                case AssignmentExpressionSyntax { Left: TupleExpressionSyntax or DeclarationExpressionSyntax } deconstruct:
                    CheckDeconstruction(node, _model.GetDeconstructionInfo(deconstruct));
                    break;
            }
            if (node is ForEachVariableStatementSyntax deconstructLoop)
                CheckDeconstruction(node, _model.GetDeconstructionInfo(deconstructLoop));
            // What it declares: its own types (their bases and interfaces), members (their types), locals.
            var declared = node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or MemberDeclarationSyntax
                or VariableDeclaratorSyntax or ParameterSyntax or SingleVariableDesignationSyntax or LocalFunctionStatementSyntax
                ? _model.GetDeclaredSymbol(node) : null;
            switch (declared)
            {
                case INamedTypeSymbol type:
                    CheckType(node, type.BaseType);
                    foreach (var iface in type.Interfaces)
                        CheckType(node, iface);
                    break;
                case IMethodSymbol method:
                    CheckType(node, method.ReturnType);
                    foreach (var parameter in method.Parameters)
                        CheckType(node, parameter.Type);
                    break;
                case IPropertySymbol property:
                    CheckType(node, property.Type);
                    break;
                case IFieldSymbol field:
                    CheckType(node, field.Type);
                    break;
                case IEventSymbol ev:
                    CheckType(node, ev.Type);
                    break;
                case ILocalSymbol localSymbol:
                    CheckType(node, localSymbol.Type);
                    break;
                case IParameterSymbol parameterSymbol:
                    CheckType(node, parameterSymbol.Type);
                    break;
            }
            if (declared != null)
                foreach (var attribute in declared.GetAttributes())
                    CheckType(node, attribute.AttributeClass);
        }

        private void CheckDeconstruction(SyntaxNode node, DeconstructionInfo info)
        {
            CheckSymbol(node, info.Method);
            if (info.Conversion?.MethodSymbol != null)
                CheckSymbol(node, info.Conversion.Value.MethodSymbol);
            foreach (var nested in info.Nested)
                CheckDeconstruction(node, nested);
        }

        private void CheckSymbol(SyntaxNode node, ISymbol? symbol)
        {
            switch (symbol)
            {
                case null:
                case INamespaceSymbol:
                case ILabelSymbol:
                case IDiscardSymbol:
                case IRangeVariableSymbol:
                case IPreprocessingSymbol:
                    return;
                case IAliasSymbol alias:
                    CheckSymbol(node, alias.Target);
                    return;
                case ITypeSymbol type:
                    CheckType(node, type);
                    return;
                case ILocalSymbol local:
                    CheckType(node, local.Type);
                    return;
                case IParameterSymbol parameter:
                    CheckType(node, parameter.Type);
                    return;
            }
            // A member: of its own source, fine (its types are checked where declared); else its type must be
            // allowed, it mustn't be denied itself, and nothing it takes or gives back may be refused.
            var member = symbol.OriginalDefinition;
            if (FromSource(member))
                return;
            var containing = member.ContainingType;
            if (containing != null)
            {
                if (!CheckType(node, containing))
                    return;
                string key = FullName(containing.OriginalDefinition) + "." + member.Name;
                if (DeniedMembers.Contains(key) || DeniedMembers.Contains(FullName(containing.OriginalDefinition)))
                {
                    Deny(node, $"{containing.Name}.{member.Name} isn't allowed");
                    return;
                }
            }
            switch (symbol)
            {
                case IMethodSymbol method:
                    CheckType(node, method.ReturnType);
                    foreach (var parameter in method.Parameters)
                        CheckType(node, parameter.Type);
                    foreach (var typeArgument in method.TypeArguments)
                        CheckType(node, typeArgument);
                    if (method.IsExtern)
                        Deny(node, $"{method.Name} is extern");
                    break;
                case IPropertySymbol property:
                    CheckType(node, property.Type);
                    break;
                case IFieldSymbol field:
                    CheckType(node, field.Type);
                    break;
                case IEventSymbol ev:
                    CheckType(node, ev.Type);
                    break;
            }
        }

        // True if allowed (a refusal is recorded otherwise).
        private bool CheckType(SyntaxNode node, ITypeSymbol? type)
        {
            switch (type)
            {
                case null:
                case ITypeParameterSymbol:
                case IErrorTypeSymbol:
                    return true;
                case IDynamicTypeSymbol:
                    Deny(node, "dynamic isn't allowed");
                    return false;
                case IPointerTypeSymbol:
                case IFunctionPointerTypeSymbol:
                    Deny(node, "pointers aren't allowed");
                    return false;
                case IArrayTypeSymbol array:
                    return CheckType(node, array.ElementType);
                case INamedTypeSymbol named:
                {
                    bool ok = true;
                    foreach (var argument in named.TypeArguments)
                        ok &= CheckType(node, argument);
                    if (named.IsTupleType)
                        return ok;
                    var definition = named.OriginalDefinition;
                    if (FromSource(definition))
                        return ok;
                    if (named.ContainingType != null && !CheckType(node, named.ContainingType))
                        return false;
                    if (!TypeAllowed(definition))
                    {
                        Deny(node, $"{FullName(definition)} isn't allowed");
                        return false;
                    }
                    return ok;
                }
                default:
                    Deny(node, $"{type.ToDisplayString()} isn't allowed");
                    return false;
            }
        }

        private bool FromSource(ISymbol symbol) => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, _compilation.Assembly);
    }

    private static bool TypeAllowed(INamedTypeSymbol type)
    {
        string name = FullName(type);
        if (name.StartsWith(LoaderNamespace, StringComparison.Ordinal))
            return false;
        if (AllowedTypes.Contains(name))
            return true;
        foreach (string prefix in AllowedTypePrefixes)
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        // (Nested types: as their outer type, checked by the caller.)
        if (type.ContainingType != null)
            return true;
        string ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return AllowedNamespaces.Contains(ns);
    }

    // Namespace.Name`arity (the metadata name), nested types as Outer.Inner.
    private static string FullName(INamedTypeSymbol type)
    {
        string name = type.MetadataName;
        for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
            name = outer.MetadataName + "." + name;
        string ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns.Length == 0 || type.ContainingNamespace!.IsGlobalNamespace ? name : ns + "." + name;
    }
}
