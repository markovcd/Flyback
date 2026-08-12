using Flyback.Core.Graph;

namespace Flyback.Core.Compile;

/// <summary>Anything the compiler wants to tell the user about a patch.</summary>
public sealed record CompileIssue(Guid? NodeId, string Message);

public sealed record CompileResult(CompiledPatch Program, IReadOnlyList<CompileIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;
}

/// <summary>
/// Walks back from a sink node and lowers everything it reaches into a flat op
/// list. Nodes that nothing depends on are simply never visited, so a patch
/// costs only what actually reaches that sink.
/// </summary>
/// <remarks>
/// Compilation is rooted at a sink rather than at "the output", so one patch
/// yields one program per sink: the screen and the speakers each get their own,
/// and a module only the ear reaches costs the eye nothing.
/// </remarks>
public static class PatchCompiler
{
    /// <summary>Compiles the program the screen shows, rooted at the video sink.</summary>
    public static CompileResult CompileForVideo(this Patch patch, ModuleCatalog? modules = null) =>
        Compile(patch, NodeCatalog.VideoOutputTypeId, NodeCatalog.VideoChannels, modules);

    /// <summary>Compiles the program the speakers play, rooted at the audio sink.</summary>
    public static CompileResult CompileForAudio(this Patch patch, ModuleCatalog? modules = null) =>
        Compile(patch, NodeCatalog.AudioOutputTypeId, NodeCatalog.AudioChannels, modules);

    /// <param name="patch">The graph to lower.</param>
    /// <param name="sinkTypeId">Module type to compile backwards from.</param>
    /// <param name="width">Registers the sink consumes — 3 for RGB, 2 for stereo.</param>
    /// <param name="modules">
    /// Which catalogue the type ids mean, defaulting to the installed one. Named
    /// explicitly, a patch can be compiled against a catalogue that is not the
    /// running program's — which is what makes a plugin's modules testable.
    /// </param>
    private static CompileResult Compile(
        Patch patch,
        string sinkTypeId,
        int width,
        ModuleCatalog? modules)
    {
        var catalog = modules ?? NodeCatalog.Current;

        var issues = new List<CompileIssue>();
        var sink = patch.Nodes.FirstOrDefault(n => n.TypeId == sinkTypeId);

        if (sink is null)
        {
            // Only the video sink is worth nagging about: a patch with no audio
            // is the normal case, not a mistake.
            if (sinkTypeId == NodeCatalog.VideoOutputTypeId)
                issues.Add(new CompileIssue(null, "No Video Output node — add one to see anything."));

            return new CompileResult(CompiledPatch.Constant(width), issues);
        }

        var emitter = new Emitter();
        var resolved = new Dictionary<Guid, Slot[]>();
        var visiting = new HashSet<Guid>();

        var result = Resolve(sink);
        var value = emitter.PackChannels(result, width);

        return new CompileResult(
            new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, value.Base, width),
            issues);

        Slot[] Resolve(NodeInstance node)
        {
            if (resolved.TryGetValue(node.Id, out var cached)) return cached;

            var def = catalog.Get(node.TypeId);
            if (def is null)
            {
                issues.Add(new CompileIssue(node.Id, $"Unknown module '{node.TypeId}'."));
                return resolved[node.Id] = [emitter.Constant(0f)];
            }

            if (!visiting.Add(node.Id))
            {
                issues.Add(new CompileIssue(node.Id,
                    $"'{def.Name}' feeds back into itself. Use a Feedback module to read the previous frame."));
                return [.. def.Outputs.Select(_ => emitter.Constant(0f))];
            }

            var inputs = new Slot[def.Inputs.Count];

            for (var port = 0; port < inputs.Length; port++)
            {
                var spec = def.Inputs[port];
                var incoming = patch.IncomingTo(node.Id, port);
                Slot value;

                if (incoming is not null && patch.Find(incoming.SourceNode) is { } source)
                {
                    var outputs = Resolve(source);
                    value = incoming.SourcePort >= 0 && incoming.SourcePort < outputs.Length
                        ? outputs[incoming.SourcePort]
                        : emitter.Constant(0f);
                }
                else if (spec.NormalledFrom >= 0 && spec.NormalledFrom < port)
                {
                    // A normalled jack carries an earlier input through when
                    // nothing is patched in. Only earlier ports can be named,
                    // because inputs resolve in order.
                    value = inputs[spec.NormalledFrom];
                }
                else
                {
                    value = emitter.Constant(DefaultFor(node, port, spec));
                }

                // An Any port takes whatever arrives; typed ports coerce.
                inputs[port] = spec.Kind == PortKind.Any ? value : emitter.Coerce(value, spec.Width);
            }

            var outputsOfNode = def.Emit(emitter, inputs);
            visiting.Remove(node.Id);
            return resolved[node.Id] = outputsOfNode;
        }
    }

    /// <summary>
    /// The knob value for an unconnected input, falling back to the definition's
    /// default when a saved patch predates a change to the module's sockets.
    /// </summary>
    private static float DefaultFor(NodeInstance node, int port, PortSpec spec) =>
        port < node.InputValues.Length ? node.InputValues[port] : spec.Default;
}
