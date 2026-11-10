// Lists non-private src members that nothing in src uses and only tests do.
// Usage: dotnet run --project tools/TestOnlyMembers -- [Flyback.slnx] [out.tsv]
// Columns: project, kind, accessibility, member, container, declared at, test uses, test files.

using System.Collections.Concurrent;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

MSBuildLocator.RegisterDefaults();
await Run(args.Length > 0 ? args[0] : "Flyback.slnx", args.Length > 1 ? args[1] : "test-only-members.tsv");

static bool IsTest(string path) => path.Replace((char)92, (char)47).Contains("/tests/");

static async Task Run(string sln, string outFile)
{
    using var ws = MSBuildWorkspace.Create(new Dictionary<string, string> { { "Configuration", "Debug" } });
    ws.WorkspaceFailed += (_, e) => { if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) Console.Error.WriteLine(e.Diagnostic.Message); };
    var solution = await ws.OpenSolutionAsync(sln);
    Console.Error.WriteLine($"projects: {solution.Projects.Count()}");
    // Keyed by Key, not by symbol: a project can see another through a compilation of its own, whose symbols are not equal to the declaring one's.
    var srcUse = new ConcurrentDictionary<string, int>();
    var testUse = new ConcurrentDictionary<string, ConcurrentBag<string>>();
    var decls = new ConcurrentDictionary<string, (ISymbol sym, string proj, string loc)>();

    foreach (var project in solution.Projects)
    {
        var comp = await project.GetCompilationAsync();
        if (comp == null) { Console.Error.WriteLine("no comp " + project.Name); continue; }
        bool test = IsTest(project.FilePath ?? "");
        Console.Error.WriteLine($"{(test ? "T" : "S")} {project.Name} ({project.DefaultNamespace})");
        foreach (var tree in comp.SyntaxTrees)
        {
            var sm = comp.GetSemanticModel(tree);
            var root = await tree.GetRootAsync();
            var file = tree.FilePath;
            if (file.Replace((char)92, (char)47).Contains("/obj/")) continue;
            foreach (var node in root.DescendantNodes())
            {
                ISymbol sym = null;
                switch (node)
                {
                    case SimpleNameSyntax n:
                    {
                        var info = sm.GetSymbolInfo(n);
                        sym = info.Symbol;
                        // nameof(Type.Method), which the IL backend's reflection lookups use, names a method group.
                        if (sym == null && info.CandidateReason == CandidateReason.MemberGroup)
                            foreach (var candidate in info.CandidateSymbols) Mark(candidate);
                        // A type handed to the container as a type argument is built by it.
                        if (sym is INamedTypeSymbol built && n.Parent is TypeArgumentListSyntax)
                            foreach (var constructor in built.InstanceConstructors) Mark(constructor);
                        break;
                    }
                    case ObjectCreationExpressionSyntax o: sym = sm.GetSymbolInfo(o).Symbol; break;
                    case ImplicitObjectCreationExpressionSyntax io: sym = sm.GetSymbolInfo(io).Symbol; break;
                    case ConstructorInitializerSyntax ci: sym = sm.GetSymbolInfo(ci).Symbol; break;
                    case ElementAccessExpressionSyntax ea: sym = sm.GetSymbolInfo(ea).Symbol; break;
                    case BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or CastExpressionSyntax: sym = sm.GetSymbolInfo(node).Symbol; break;
                    case ForEachStatementSyntax fe: { var i = sm.GetForEachStatementInfo(fe); Mark(i.GetEnumeratorMethod); Mark(i.MoveNextMethod); Mark(i.CurrentProperty); break; }
                    case AttributeSyntax at: sym = sm.GetSymbolInfo(at).Symbol; break;
                }
                Mark(sym);
                void Mark(ISymbol s)
                {
                    if (s == null) return;
                    s = s.OriginalDefinition;
                    if (s is IMethodSymbol m && m.AssociatedSymbol != null) s = m.AssociatedSymbol.OriginalDefinition;
                    // self reference: inside own declaration
                    if (s.Locations.Any(l => l.IsInSource && l.SourceTree == tree && node.Span.IntersectsWith(l.SourceSpan)) && node is SimpleNameSyntax && IsDeclId(node)) return;
                    if (test) testUse.GetOrAdd(Key(s), _ => new()).Add(file + ":" + (tree.GetLineSpan(node.Span).StartLinePosition.Line + 1));
                    else
                    {
                        srcUse.AddOrUpdate(Key(s), 1, (_, v) => v + 1);
                        // A member used is its type used, as an extension method's class is.
                        if (s.ContainingType is { } owner) srcUse.AddOrUpdate(Key(owner.OriginalDefinition), 1, (_, v) => v + 1);
                    }
                }
            }
            if (!test)
            {
                foreach (var node in root.DescendantNodes())
                {
                    if (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax or EventDeclarationSyntax or IndexerDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax or VariableDeclaratorSyntax or EnumMemberDeclarationSyntax)
                    {
                        if (node is VariableDeclaratorSyntax vd && !(vd.Parent?.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax)) continue;
                        var s = sm.GetDeclaredSymbol(node);
                        if (s == null || s.IsImplicitlyDeclared) continue;
                        if (s.DeclaredAccessibility == Accessibility.Private) continue;
                        decls.TryAdd(Key(s.OriginalDefinition), (s.OriginalDefinition, project.Name, file + ":" + (tree.GetLineSpan(node.Span).StartLinePosition.Line + 1)));
                    }
                }
            }
        }
    }
    var lines = new List<string>();
    int unused = 0;
    foreach (var (key, d) in decls)
    {
        var s = d.sym;
        bool src = srcUse.ContainsKey(key);
        bool tst = testUse.TryGetValue(key, out var bag);
        if (!src && !tst) { unused++; continue; }
        if (src) continue;
        if (s is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove }) continue;
        if (s is IMethodSymbol { IsOverride: true } || s is IPropertySymbol { IsOverride: true }) continue;
        if (Implements(s)) continue;
        if (CalledFromOutside(s)) continue;
        var kind = s.Kind == SymbolKind.Method ? ((IMethodSymbol)s).MethodKind.ToString() : s.Kind.ToString();
        lines.Add($"{d.proj}\t{kind}\t{s.DeclaredAccessibility}\t{s.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}\t{s.ContainingType?.ToDisplayString() ?? s.ContainingNamespace.ToDisplayString()}\t{d.loc}\t{bag.Count}\t{string.Join(" | ", bag.Select(Path.GetFileName).Distinct().Take(3))}");
    }
    lines.Sort(StringComparer.Ordinal);
    File.WriteAllLines(outFile, lines);
    Console.Error.WriteLine($"decls {decls.Count} unusedEverywhere {unused} testOnly {lines.Count}");
}

static string Key(ISymbol s) => s.ContainingAssembly?.Name + "|" + (s.GetDocumentationCommentId() ?? s.ToDisplayString());

// What the runtime reaches without a call in src: an entry point's class, a plugin's class, an enum's values.
static bool CalledFromOutside(ISymbol s) => s switch
{
    INamedTypeSymbol type => type.GetMembers("Main").Any(m => m.IsStatic) || type.AllInterfaces.Any(i => i.Name == "IFlybackPlugin"),
    IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => true,
    _ => false,
};

static bool IsDeclId(SyntaxNode n) => n.Parent switch
{
    BaseTypeDeclarationSyntax => false,
    _ => false
};

static bool Implements(ISymbol s)
{
    if (s.ContainingType == null || s is INamedTypeSymbol) return false;
    foreach (var i in s.ContainingType.AllInterfaces)
        foreach (var m in i.GetMembers())
            if (SymbolEqualityComparer.Default.Equals(s.ContainingType.FindImplementationForInterfaceMember(m), s)) return true;
    return false;
}
