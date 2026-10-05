using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Engine.Compile;

/// <summary>
/// Walks back from a sink node and lowers everything it reaches into a flat op
/// list, so a patch costs only what reaches that sink. Rooted at a sink rather
/// than at "the output", so the screen and the speakers each get their own
/// program and a module only the ear reaches costs the eye nothing.
/// </summary>
public static class PatchCompiler
{
    /// <summary>Compiles the program the screen shows, reading the Output's color.</summary>
    public static CompileResult CompileForVideo(
        this Patch patch,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool played = false) =>
        Compile(patch, NodeCatalog.Screen, modules, samples: samples, pictures: pictures, played: played);

    /// <summary>Compiles the program the speakers play, reading the Output's left and right.</summary>
    public static CompileResult CompileForAudio(
        this Patch patch,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool played = false) =>
        Compile(patch, NodeCatalog.Speakers, modules, samples: samples, pictures: pictures, played: played);

    /// <summary>
    /// Compiles the program a Probe shows, rooted at the probe rather than at the
    /// Output — which is never visited, so the picture the patch makes costs
    /// nothing while its chart is up.
    /// </summary>
    /// <param name="patch"></param>
    /// <param name="probe">
    /// Which module to root at. A node that is not in the patch compiles the
    /// ordinary picture instead, rather than a black screen with nothing to say
    /// why.
    /// </param>
    /// <param name="modules"></param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    /// <param name="played">Whether the panel's knobs are read live — see <c>Compile</c>.</param>
    public static CompileResult CompileForProbe(
        this Patch patch,
        Guid probe,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool played = false) =>
        Compile(patch, NodeCatalog.Screen, modules, probe, samples, pictures, played);

    /// <summary>
    /// Compiles one program rooted at every output of <paramref name="measured"/>,
    /// with no Output in it, so an output nothing is wired to is lowered too.
    /// </summary>
    /// <param name="patch"></param>
    /// <param name="measured">The modules whose outputs to read, or null for every module.</param>
    /// <param name="sound">The speakers' half when true, where an Image is black; the screen's otherwise.</param>
    /// <param name="modules"></param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    public static MeasureProgram CompileForMeasure(
        this Patch patch,
        IReadOnlyCollection<Guid>? measured,
        bool sound,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null)
    {
        var sockets = new List<MeasuredSocket>();
        var roots = measured ?? [.. patch.Nodes.Select(node => node.Id)];

        var compiled = Compile(
            patch,
            sound ? NodeCatalog.Speakers : NodeCatalog.Screen,
            modules,
            samples: samples,
            pictures: pictures,
            measured: (roots, sockets));

        return new MeasureProgram(compiled.Program, sockets, compiled.Issues);
    }

    /// <summary>Lowers <paramref name="patch"/> as <see cref="PatchWalk"/> describes.</summary>
    private static CompileResult Compile(
        Patch patch,
        NodeCatalog.SinkKind sink,
        ModuleCatalog? modules,
        Guid? probe = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool played = false,
        (IReadOnlyCollection<Guid> Roots, List<MeasuredSocket> Sockets)? measured = null) =>
        new PatchWalk(patch, sink, modules, probe, samples, pictures, played, measured).Run();

    /// <summary>
    /// How much of the past a Scope is asking for, in seconds: its first duration
    /// knob, which is marked in decades like every other one.
    /// </summary>
    /// <remarks>
    /// Found by <see cref="PortDisplay.Duration"/> rather than by position, so a
    /// plugin's own Scope declares its window by saying what the socket is. A
    /// module with none asks for a fiftieth of a second. The knob and not the
    /// wire: the refill runs once a frame and outside the program, and could do
    /// nothing with a value arriving per sample.
    /// </remarks>
    internal static float WindowOf(NodeInstance node, NodeDef def)
    {
        for (var port = 0; port < def.Inputs.Count; port++)
        {
            if (def.Inputs[port].Display != PortDisplay.Duration) continue;

            var decades = DefaultFor(node, port, def.Inputs[port]);
            if (!float.IsFinite(decades)) break;

            // Bounded by what there is ring to answer with and by nothing else, so
            // a window turned all the way up compiles to the time it says. See
            // DelayState.MaxWindowSeconds, which is also what sizes the ring.
            return Math.Clamp(MathF.Pow(10f, decades), 0.0001f, DelayState.MaxWindowSeconds);
        }

        return 0.02f;
    }

    /// <summary>
    /// The knob value for an unconnected input, falling back to the definition's
    /// default when a saved patch predates a change to the module's sockets.
    /// </summary>
    internal static float DefaultFor(NodeInstance node, int port, PortSpec spec) =>
        port < node.InputValues.Length ? node.InputValues[port] : spec.Default;
}
