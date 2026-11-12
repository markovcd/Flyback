using System.Collections.Immutable;
using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// Every module the synth knows how to build. Each entry pairs a socket layout
/// with the ops it lowers to; adding one here makes it appear in the palette and
/// compile with no other changes.
/// </summary>
/// <remarks>
/// A plugin may add more, so the lookups here read through <see cref="Current"/>
/// — installed once at startup and never changed after. Anything reasoning about
/// a catalog that is not the running one should take a
/// <see cref="ModuleCatalog"/> instead.
/// </remarks>
public static class NodeCatalog
{
    /// <summary>The provider every built-in module belongs to. Reserved.</summary>
    internal static ModuleProvider BuiltInProvider { get; } = new("flyback", GlobalConstants.ApplicationName);

    /// <summary>Just the modules that ship in the engine, with nothing added.</summary>
    internal static ModuleCatalog BuiltIn { get; }

    /// <summary>The catalog the running program uses.</summary>
    internal static ModuleCatalog Current { get; private set; }

    /// <summary>
    /// Puts a composed catalog in place. Called once during startup, after
    /// plugins have been read and before any patch exists — a module appearing
    /// or vanishing later would leave already-compiled programs describing a
    /// catalog that no longer matches.
    /// </summary>
    internal static void Install(ModuleCatalog catalog) => Current = catalog;

    /// <summary>The categories the current catalog's modules are filed under.</summary>
    internal static IEnumerable<string> Categories => Current.Categories;

    /// <summary>The module with <paramref name="typeId"/> in the current catalog, or null where there is none.</summary>
    internal static NodeDef? Get(string typeId) => Current.Get(typeId);

    /// <summary>
    /// The module with <paramref name="typeId"/> in the current catalog; throws <see
    /// cref="KeyNotFoundException"/> where there is none.
    /// </summary>
    internal static NodeDef Require(string typeId) => Current.Require(typeId);

    /// <inheritdoc cref="ModuleCatalog.Normalled"/>
    internal static string? Normalled(PortSpec spec) => Current.Normalled(spec);

    /// <inheritdoc cref="Sockets.Clock"/>
    public static PortNormal Clock => Sockets.Clock;

    /// <inheritdoc cref="Sockets.Across"/>
    public static PortNormal Across => Sockets.Across;

    /// <inheritdoc cref="Sockets.Down"/>
    public static PortNormal Down => Sockets.Down;

    /// <inheritdoc cref="Sockets.Freq"/>
    public static PortSpec Freq => Sockets.Freq;

    /// <inheritdoc cref="SequencerModules.MaxParts"/>
    public const int MaxParts = SequencerModules.MaxParts;

    /// <inheritdoc cref="DelayModule.Echoed"/>
    public static Slot DelayEchoed(Emitter em, Slot dry, Slot time, Slot feedback, Slot mix) =>
        DelayModule.Echoed(em, dry, time, feedback, mix);

    /// <inheritdoc cref="FilterModule.Responses"/>
    public static Slot[] FilterResponses(Emitter em, Slot dry, Slot cutoff, Slot resonance) =>
        FilterModule.Responses(em, dry, cutoff, resonance);

    /// <inheritdoc cref="NoiseModule.WhiteAndPink"/>
    public static (Slot White, Slot Pink) WhiteAndPink(Emitter em, Slot domain, Slot seed) =>
        NoiseModule.WhiteAndPink(em, domain, seed);

    static NodeCatalog()
    {
        var modules = new[] { OutputModule.Def() }
            .Concat(PitchModules.Defs())
            .Concat(ChordModules.Defs())
            .Concat(ChartModules.Defs())
            .Append(BeamModule.Def())
            .Append(MeterModule.Def())
            .Append(ScanModule.Def())
            .Concat(SourceModules.Defs())
            .Concat(MidiModules.Defs())
            .Concat(OscillatorModules.Defs())
            .Concat(StringModule.Defs())
            .Concat(SequencerModules.Defs())
            .Append(SongModule.Def)
            .Concat(EnvelopeModules.Defs())
            .Append(DuckModule.Def())
            .Append(FilterModule.Def())
            .Append(NoiseModule.Def())
            .Append(SlewModule.Def())
            .Append(DriveModule.Def())
            .Append(DelayModule.Def())
            .Append(ReverbModule.Def())
            .Concat(MathModules.Defs())
            .Concat(BusModules.Defs())
            .Concat(PolyphonyModules.Defs())
            .Concat(SpaceModules.Defs())
            .Concat(PatternModules.Defs())
            .Concat(ColorModules.Defs())
            .Concat(FeedbackModules.Defs())
            .ToImmutableList();

        if (modules.SelectMany(SocketHelp.Missing).ToList() is [_, ..] missing)
            throw new InvalidOperationException(string.Join(Environment.NewLine, missing));

        BuiltIn = ModuleCatalog.Of(BuiltInProvider, modules);
        Current = BuiltIn;
    }
}
