using System.Runtime.CompilerServices;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Language;

namespace Flyback.Engine.Packs;

/// <summary>
/// A pack's modules as a catalog holds a plugin's: one module each, with its sockets and
/// card, which a patch places like any other and the compiler sees expanded (ADR-0242).
/// </summary>
internal static class PackModules
{
    private static readonly ConditionalWeakTable<NodeDef, PackStencils> Stencils = new();
    private static readonly ConditionalWeakTable<NodeDef, PackSource> Sources = new();

    /// <summary>What the settings a pack's module keeps are filed under on each placed instance.</summary>
    public const string SettingsKey = "pack";

    /// <summary>Whether <paramref name="def"/> is a pack's module, placed by copying what its body is made of.</summary>
    public static bool Offers(NodeDef def) => Stencils.TryGetValue(def, out _);

    /// <summary>What placing <paramref name="node"/>, a <paramref name="def"/>, copies at the settings it holds; null for a module no pack offers.</summary>
    public static PackStencil? StencilOf(NodeDef def, NodeInstance node)
    {
        if (!Stencils.TryGetValue(def, out var stencils)) return null;

        var held = def.Extra<SettingsExtra>() is { } settings
            ? new ExtraState(settings.Fields, node.StateOf(settings.Key))
            : null;

        return stencils.At([.. stencils.Choices.Select(choice => held?.Chosen(choice) ?? "")]);
    }

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
        var defs = new List<(NodeDef Def, PackStencils Stencils)>();

        foreach (var module in load.Modules)
        {
            var built = new Dictionary<string, PackStencil>(StringComparer.Ordinal);
            var said = new List<LanguageIssue>();

            foreach (var chosen in Settings(module.Choices))
            {
                var (stencil, wrong) = PackStencil.Build(source, module, chosen, catalog);

                if (stencil is null) said.AddRange(wrong);
                else built[PackStencils.Key(chosen)] = stencil;
            }

            if (said.Count > 0) issues.AddRange(said.DistinctBy(issue => (issue.Line, issue.Column, issue.Code)));
            else defs.Add((Def(module), new PackStencils([.. module.Choices.Select(c => c.Name)], [.. module.Choices.Select(c => c.Default)], built)));
        }

        if (issues.Count > 0 || defs.Count == 0) return (catalog, issues);

        // The version rides in the name a saved patch keeps, so a patch says which one it was made with.
        var added = catalog.With(new ModuleProvider(pack.Id, $"{pack.Id} {pack.Version}"), [.. defs.Select(d => d.Def)]);

        if (added.Rejected.Count > 0)
            return (catalog, [.. added.Rejected.Select(said => new LanguageIssue(1, 1, IssueCode.ModuleOutsidePack, said))]);

        var read = new PackSource(pack.Id, pack.Version, source);

        foreach (var (def, stencils) in defs)
        {
            Stencils.AddOrUpdate(def, stencils);
            Sources.AddOrUpdate(def, read);
        }

        return (added.Catalog, []);
    }

    /// <summary>Every way the choices combine, each one value per choice, in order.</summary>
    private static IEnumerable<IReadOnlyList<string>> Settings(IReadOnlyList<PackChoice> choices) =>
        choices.Aggregate(
            (IEnumerable<IReadOnlyList<string>>)[[]],
            (ways, choice) => ways.SelectMany(way => choice.Options.Select(option => (IReadOnlyList<string>)[.. way, option])));

    private static NodeDef Def(PackModule module) => new(
        module.TypeId,
        Title(module.Name),
        module.Card.Category ?? "Packs",
        [.. module.Sockets.Select(Port)],
        [.. module.Outputs.Select(o => new PortSpec(o.Name, PortKind.Any) { Help = o.Help })],
        Never,
        module.Card.Help)
    {
        Words = module.Card.Words,
        Extras = module.Choices.Count == 0
            ? []
            : [new SettingsExtra(SettingsKey, [.. module.Choices.Select(Field)])],
    };

    private static PortSpec Port(PackSocket socket) => socket is { Min: { } min, Max: { } max }
        ? new(socket.Name, PortKind.Any, socket.Default, min, max)
        : new(socket.Name, PortKind.Any, socket.Default);

    private static ExtraField Field(PackChoice choice) =>
        new ExtraField.Choice(choice.Name, choice.Name, [.. choice.Options.Select(o => new ChoiceOption(o, o))], choice.Default);

    /// <summary><c>tape_echo</c> as a card says it: "Tape echo".</summary>
    private static string Title(string name) =>
        char.ToUpperInvariant(name[0]) + name[1..].Replace('_', ' ');

    private static Slot[] Never(Emitter emitter, EmitContext node) =>
        throw new InvalidOperationException("A pack's module is expanded before the patch is compiled.");
}
