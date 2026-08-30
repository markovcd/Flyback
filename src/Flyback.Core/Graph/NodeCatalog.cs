using System.Collections.Immutable;

namespace Flyback.Core.Graph;

/// <summary>
/// Every module the synth knows how to build. Each entry pairs a socket layout
/// with the ops it lowers to; adding a module here makes it appear in the
/// editor palette and compile with no other changes.
/// </summary>
/// <remarks>
/// The definitions below are the ones that ship in the engine. A plugin may add
/// more, so the lookups here read through <see cref="Current"/> — installed once
/// at startup, before any patch is compiled, and never changed after. Anything
/// that wants to reason about a catalogue that is not the running one should
/// take a <see cref="ModuleCatalog"/> rather than come here.
/// </remarks>
public static partial class NodeCatalog
{
    /// <summary>The provider every module in this file belongs to. Reserved.</summary>
    public static ModuleProvider BuiltInProvider { get; } = new("flyback", GlobalConstants.ApplicationName);
    
    /// <summary>RGB, so the screen reads three registers.</summary>
    public const int VideoChannels = 3;

    /// <summary>Stereo, so the speakers read two registers where the screen reads three.</summary>
    public const int AudioChannels = 2;

    /// <summary>
    /// One of the two programs a patch yields. Both root at the same Output and
    /// differ only in which of its results they read, which is what still buys
    /// ADR-0022's dead-code elimination now that there is one node rather than
    /// two: a module only the ear reaches is never visited by the screen's walk.
    /// </summary>
    /// <param name="Inputs">
    /// Which of the Output's sockets this program walks back from. The other
    /// sockets are not merely unread — they are never resolved, so nothing
    /// upstream of them emits an op. This is the whole of ADR-0022's cross-sink
    /// dead-code elimination, and with one node it has to be said here rather
    /// than falling out of there being two.
    /// </param>
    /// <param name="Results">Which of the sink's emit results this program reads.</param>
    public readonly record struct SinkKind(string Name, Range Inputs, Range Results, int Width);

    /// <summary>The screen's program, walking back from the Output's color.</summary>
    public static SinkKind Screen => new("screen", OutputColorPort..OutputLeftPort, 0..1, VideoChannels);

    /// <summary>The speakers' program, walking back from left, right and gain.</summary>
    public static SinkKind Speakers => new("speakers", OutputLeftPort..OutputScanPort, 1..3, AudioChannels);

    // Port indices on the Output, named because three separate places index it
    // and a shifted socket would otherwise be a silent change of meaning.
    public const int OutputColorPort = 0;
    public const int OutputLeftPort = 1;
    public const int OutputRightPort = 2;
    public const int OutputGainPort = 3;
    public const int OutputScanPort = 4;
    public const int OutputScanRatePort = 5;

    private const float Tau = 6.283185307179586f;

    /// <summary>Just the modules that ship in the engine, with nothing added.</summary>
    public static ModuleCatalog BuiltIn { get; }

    /// <summary>The catalogue the running program uses.</summary>
    public static ModuleCatalog Current { get; private set; }

    /// <summary>
    /// Puts a composed catalogue in place. Called once during startup, after
    /// plugins have been read and before any patch exists — a module appearing
    /// or vanishing later would leave already-compiled programs describing a
    /// catalogue that no longer matches.
    /// </summary>
    public static void Install(ModuleCatalog catalog) => Current = catalog;

    public static IReadOnlyList<NodeDef> All => Current.All;

    public static IEnumerable<string> Categories => Current.Categories;

    public static NodeDef? Get(string typeId) => Current.Get(typeId);

    public static NodeDef Require(string typeId) => Current.Require(typeId);

    /// <inheritdoc cref="ModuleCatalog.Normalled"/>
    public static string? Normalled(PortSpec spec) => Current.Normalled(spec);

    // --- port shorthands -----------------------------------------------------

    private static PortSpec Num(string name, float value = 0f, float min = -4f, float max = 4f) =>
        new(name, PortKind.Scalar, value, min, max);

    private static PortSpec Col(string name) => new(name, PortKind.Color);

    /// <summary>The hidden clock every domain socket is normalled to.</summary>
    public static PortNormal Clock => new(TimeTypeId);

    /// <summary>The hidden x and y every socket that wants a position is normalled to.</summary>
    public static PortNormal Across => new(CoordTypeId, CoordXPort);

    public static PortNormal Down => new(CoordTypeId, CoordYPort);

    /// <summary>
    /// The axis a module is read across rather than a value it uses. Named at
    /// the port because only the module knows which of its inputs that is, and
    /// the compiler has no other way to tell one input from another.
    /// </summary>
    /// <remarks>
    /// Normalled to Time, because a domain resting on a knob is a module that
    /// does not move and there is no reading of it that anybody wanted — see
    /// <see cref="PortSpec.NormalledTo"/>. Every other domain in the catalogue
    /// is built through here, so this one line is the whole of "an oscillator
    /// runs unless you say otherwise".
    /// </remarks>
    private static PortSpec Domain(string name) =>
        new(name, NormalledTo: Clock, Domain: true);

    /// <summary>
    /// Where on the screen a module is being asked about, normalled to
    /// Coordinates so that the pixel's own position is what it reads until a
    /// patch says different.
    /// </summary>
    /// <remarks>
    /// The pair is declared together because it is always a pair: a module given
    /// one of the two and left holding a knob on the other reads along a line
    /// through the picture rather than across it, which is a stranger thing than
    /// either socket on its own suggests.
    /// </remarks>
    private static PortSpec[] Position() =>
    [
        new("x", NormalledTo: Across),
        new("y", NormalledTo: Down),
    ];

    private static PortSpec Any(string name, float value = 0f, float min = -4f, float max = 4f) =>
        new(name, PortKind.Any, value, min, max);
    
    static NodeCatalog()
    {
        var modules = Output()
            .Concat(Sources())
            .Concat(Midi())
            .Concat(Oscillators())
            .Concat(Sequencers())
            .Concat(Envelopes())
            .Concat(Maths())
            .Concat(Space())
            .Concat(Patterns())
            .Concat(Color())
            .Concat(Feedback())
            .ToImmutableList();

        BuiltIn = ModuleCatalog.Of(BuiltInProvider, modules);
        Current = BuiltIn;
    }
}
