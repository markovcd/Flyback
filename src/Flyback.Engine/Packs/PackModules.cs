using System.Runtime.CompilerServices;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Engine.Packs;

/// <summary>
/// A pack's modules as a catalog holds a plugin's: one module each, with its sockets and
/// card, which a patch places like any other and the compiler sees expanded (ADR-0242).
/// </summary>
internal static class PackModules
{
    private static readonly ConditionalWeakTable<NodeDef, PackStencil> Stencils = new();
    private static readonly ConditionalWeakTable<NodeDef, PackSource> Sources = new();

    /// <summary>What placing <paramref name="def"/> copies, or null for a module no pack offers.</summary>
    public static PackStencil? StencilOf(NodeDef def) => Stencils.TryGetValue(def, out var stencil) ? stencil : null;

    /// <summary>The pack <paramref name="def"/> came from, or null for a module no pack offers.</summary>
    public static PackSource? SourceOf(NodeDef def) => Sources.TryGetValue(def, out var source) ? source : null;

    /// <summary>
    /// <paramref name="catalog"/> with the modules the pack in <paramref name="source"/> offers,
    /// each built once to check it. A pack with any mistake in it adds nothing.
    /// </summary>
    /// <returns>The catalog, and every mistake, on the pack's own lines.</returns>
    public static (ModuleCatalog Catalog, IReadOnlyList<LanguageIssue> Issues) Into(ModuleCatalog catalog, string source)
    {
        var load = PatchLanguage.Build(source, catalog);

        if (!load.Ok) return (catalog, [.. load.Issues.Where(issue => issue.IsError)]);

        if (load.Pack is not { } pack)
            return (catalog, [new LanguageIssue(1, 1, IssueCode.ModuleOutsidePack, "this text is not a pack: it opens with 'pack <id> <version>'.")]);

        var issues = new List<LanguageIssue>();
        var defs = new List<(NodeDef Def, PackStencil Stencil)>();

        foreach (var module in load.Modules)
        {
            var (stencil, said) = PackStencil.Build(source, module, catalog);

            if (stencil is null) issues.AddRange(said);
            else defs.Add((Def(module), stencil));
        }

        if (issues.Count > 0 || defs.Count == 0) return (catalog, issues);

        // The version rides in the name a saved patch keeps, so a patch says which one it was made with.
        var added = catalog.With(new ModuleProvider(pack.Id, $"{pack.Id} {pack.Version}"), [.. defs.Select(d => d.Def)]);

        if (added.Rejected.Count > 0)
            return (catalog, [.. added.Rejected.Select(said => new LanguageIssue(1, 1, IssueCode.ModuleOutsidePack, said))]);

        var read = new PackSource(pack.Id, pack.Version, source);

        foreach (var (def, stencil) in defs)
        {
            Stencils.AddOrUpdate(def, stencil);
            Sources.AddOrUpdate(def, read);
        }

        return (added.Catalog, []);
    }

    private static NodeDef Def(PackModule module) => new(
        module.TypeId,
        Title(module.Name),
        module.Card.Category ?? "Packs",
        [.. module.Inputs.Select((name, i) => new PortSpec(name, PortKind.Any, module.Defaults[i]))],
        [.. Enumerable.Range(0, module.Outputs).Select(i => new PortSpec(module.Outputs == 1 ? "out" : $"out_{i + 1}", PortKind.Any))],
        Never,
        module.Card.Help)
    {
        Words = module.Card.Words,
    };

    /// <summary><c>tape_echo</c> as a card says it: "Tape echo".</summary>
    private static string Title(string name) =>
        char.ToUpperInvariant(name[0]) + name[1..].Replace('_', ' ');

    private static Slot[] Never(Emitter emitter, EmitContext node) =>
        throw new InvalidOperationException("A pack's module is expanded before the patch is compiled.");
}
