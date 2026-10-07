using System.Text.Json;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// A patch an assistant may edit, and the vocabulary it edits it in. Everything
/// here is provider-neutral: naming a module, wiring a port and reading the
/// compiler's complaints are knowledge of the graph, not of any model's API.
/// </summary>
/// <remarks>
/// The working patch is the workbench's own copy (<see cref="WorkingPatch"/>),
/// which is what makes accepting a proposal a single assignment and rejecting one
/// free. What each tool says and does is in <see cref="ToolTable"/>.
/// <para>
/// The catalog arrives explicitly and <see cref="NodeCatalog.Current"/> is never
/// read (ADR-0026), which is also what lets the tests run against
/// <see cref="NodeCatalog.BuiltIn"/>.
/// </para>
/// </remarks>
public sealed class PatchWorkbench
{
    private readonly WorkingPatch bench;
    private readonly WorkbenchLimits limits;
    private readonly string startingPoint;
    private readonly PatchReports reports;
    private readonly CanvasChanges canvas;

    /// <summary>What each tool name runs, whether or not it is offered.</summary>
    private readonly Dictionary<string, ToolTable.ToolBody> bodies;

    /// <param name="vision">Whether the model may be shown a frame, which offers <c>render</c>.</param>
    /// <param name="hearing">
    /// Whether and by whom the sound may be heard, which offers <c>listen</c>
    /// (<see cref="Listener"/>). Off by default.
    /// </param>
    /// <param name="samples">Where Sample files are looked up, or null for silence.</param>
    /// <param name="prose">The briefing's prose budget, or <see cref="ProsePolicy.Default"/>.</param>
    /// <param name="presets">
    /// The presets offered through <c>describe_preset</c>, defaulting to the shipped
    /// ones; an empty list removes the tool.
    /// </param>
    internal PatchWorkbench(
        ModuleCatalog modules,
        Patch startingPoint,
        bool vision = true,
        Listener hearing = Listener.None,
        WorkbenchLimits? limits = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        ProsePolicy? prose = null,
        IReadOnlyList<PatchPreset>? presets = null)
    {
        bench = new WorkingPatch(modules, samples, pictures);
        this.limits = limits ?? new WorkbenchLimits();

        // Everything but the blank ones, which have nothing in them to learn from.
        IReadOnlyList<PatchPreset> readable = [.. (presets ?? Presets.All).Where(preset => preset.Kind != PresetKind.Blank)];

        var catalog = new CatalogReference(modules, readable);

        // Kept as text so Reset cannot hand back something an earlier edit
        // reached into, and so the starting point is provably reloadable.
        this.startingPoint = PatchIO.ToJson(startingPoint, modules);

        bench.Adopt(PatchIO.Read(this.startingPoint, modules).Patch);

        reports = new PatchReports(bench);
        canvas = new CanvasChanges(bench);

        var policy = prose ?? ProsePolicy.Default;

        Undescribed = policy.Undescribed(modules);

        var briefing = Handbook.Render(modules, Undescribed, hearing);
        Briefing = briefing + Handbook.Presets(readable, policy.Budget - briefing.Length);

        var senses = new PatchSenses(bench, this.limits);

        var table = new ToolTable(
            reports,
            new WholePatchEdits(bench, reports, this.startingPoint),
            new ModuleEdits(bench, reports, catalog),
            new GraphEdits(bench, reports),
            new PatchProposals(bench, senses, this.limits),
            senses,
            new PatchMeasurements(bench, this.limits),
            catalog,
            this.limits);

        // find_modules only where some description was left out: offered on every
        // run it would be one more tool to weigh on every turn, for searching what
        // is already in front of the model. describe_module always has what the
        // briefing leaves to it, what each socket is for.
        var vocabulary = table.Build(vision, hearing, lookups: Undescribed.Count > 0, readsPresets: readable.Count > 0);

        Tools = [.. vocabulary.Where(tool => tool.Offered).Select(tool => tool.Spec)];
        bodies = vocabulary.ToDictionary(tool => tool.Spec.Name, tool => tool.Run, StringComparer.Ordinal);
    }

    /// <summary>The conventions and the catalog, as a model should be told them.</summary>
    public string Briefing { get; }

    /// <summary>The type ids whose descriptions <see cref="Briefing"/> leaves out.</summary>
    public IReadOnlySet<string> Undescribed { get; }

    /// <summary>The tools the model is offered.</summary>
    public IReadOnlyList<PatchTool> Tools { get; }

    /// <summary>Whether the model is offered <c>listen</c>, which is the only way it hears or measures the sound.</summary>
    internal bool Hears => Tools.Any(tool => tool.Name == "listen");

    /// <summary>Whether the provider's settings offer a switch that would let it hear, where it does not.</summary>
    internal bool EarOffered { get; set; }

    /// <summary>Whether the model has put a patch forward as its answer.</summary>
    public bool HasProposal => bench.Proposal is not null;

    /// <summary>
    /// Takes the proposal back down and starts the turn's counts again, leaving
    /// everything built so far in place.
    /// </summary>
    /// <remarks>
    /// A conversation carries on from a proposal — "now make it slower" is the
    /// second thing anybody says — so only the offer is withdrawn. An adapter calls
    /// this as a turn begins; one that did not would hand the same patch over again
    /// as this turn's answer.
    /// </remarks>
    public void Reopen()
    {
        bench.Proposal = null;
        bench.Edits = 0;
        ToolCalls = 0;
    }

    /// <summary>What the model said of its proposal, or nothing where there is none.</summary>
    public string ProposalSummary => bench.Proposal ?? string.Empty;

    /// <summary>How many edits this turn has made.</summary>
    public int Edits => bench.Edits;

    /// <summary>How many tools this turn has called, against <see cref="WorkbenchLimits.MaxToolCalls"/>.</summary>
    public int ToolCalls { get; private set; }

    /// <summary>
    /// The working patch, laid out and deep-copied by writing it out and reading
    /// it back. The round trip is the copy, and it is also the last gate: what
    /// comes out is exactly what would have been saved, stamped with the plugins
    /// it needs, or it does not come out at all.
    /// </summary>
    public Patch Snapshot()
    {
        bench.Arrange();
        return PatchIO.Read(PatchIO.ToJson(bench.Patch, bench.Modules), bench.Modules).Patch;
    }

    /// <summary>What <see cref="Restore"/> needs to put this workbench back as it stands.</summary>
    internal WorkbenchState Save() => new(
        startingPoint,
        PatchIO.ToJson(bench.Patch, bench.Modules),
        bench.ByHandle.ToDictionary(pair => pair.Key, pair => pair.Value.Id, StringComparer.OrdinalIgnoreCase),
        Edits,
        ToolCalls);

    /// <summary>
    /// Puts back the patch being built, the names its modules answer to and what
    /// the run has spent, from a workbench that was <see cref="Save"/>d.
    /// </summary>
    /// <remarks>
    /// Not the starting point: that is what this workbench was built over, so
    /// whoever carries a conversation on builds it over
    /// <see cref="WorkbenchState.Start"/>. A handle naming a module that is not
    /// there is dropped and a module with no handle is given one, so every module
    /// can still be named whatever the state says.
    /// </remarks>
    internal void Restore(WorkbenchState state)
    {
        bench.Adopt(PatchIO.Read(state.Working, bench.Modules).Patch, state.Handles);

        bench.Proposal = null;
        bench.Edits = state.Edits;
        ToolCalls = state.ToolCalls;
    }

    /// <summary>What <c>describe_patch</c> answers, for the first message of a conversation to open with.</summary>
    internal string Described => reports.Describe();

    /// <inheritdoc cref="CanvasChanges.Follow(Patch, Patch)"/>
    internal List<Retuned> Follow(Patch was, Patch now) => canvas.Follow(was, now);

    /// <inheritdoc cref="CanvasChanges.Follow(Patch)"/>
    internal List<Retuned> Follow(Patch now) => canvas.Follow(now);

    /// <inheritdoc cref="CanvasChanges.Told"/>
    internal string Told(IReadOnlyCollection<Retuned> carried) => canvas.Told(carried);

    /// <summary>Each tool call as it arrives, before it runs, for a host that shows the calls themselves.</summary>
    internal event Action<string, JsonElement>? Calling;

    /// <summary>
    /// Runs one tool call. Never throws, whatever it is handed — see
    /// <see cref="ToolOutcome"/> for why that is a rule rather than a courtesy.
    /// </summary>
    public async Task<ToolOutcome> InvokeAsync(string tool, JsonElement arguments, CancellationToken cancel)
    {
        Calling?.Invoke(tool, arguments);

        // Propose is never refused for the count, since it is what the refusal asks for.
        if (ToolCalls >= limits.MaxToolCalls && tool != "propose")
            return ToolOutcome.Refused(
                $"you have used all {limits.MaxToolCalls} tool calls for this turn. "
                + "Finish with 'propose' if the patch is usable, or say what is left undone.");

        ToolCalls++;

        if (!bodies.TryGetValue(tool, out var run))
            return ToolOutcome.Refused($"there is no tool called '{tool}'.");

        try
        {
            return await run(arguments, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolOutcome.Refused($"'{tool}' failed: {ex.Message}");
        }
    }
}
