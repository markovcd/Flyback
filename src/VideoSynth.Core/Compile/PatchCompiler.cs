using VideoSynth.Core.Graph;

namespace VideoSynth.Core.Compile;

/// <summary>Anything the compiler wants to tell the user about a patch.</summary>
public sealed record CompileIssue(Guid? NodeId, string Message);

public sealed record CompileResult(CompiledPatch Program, IReadOnlyList<CompileIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;
}

/// <summary>
/// Walks back from the Output node and lowers everything it reaches into a flat
/// op list. Nodes that nothing depends on are simply never visited, so a patch
/// costs only what is actually on screen.
/// </summary>
public static class PatchCompiler
{
    public static CompileResult Compile(Patch patch)
    {
        var issues = new List<CompileIssue>();
        var output = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.OutputTypeId);

        if (output is null)
        {
            issues.Add(new CompileIssue(null, "No Output node — add one to see anything."));
            return new CompileResult(CompiledPatch.Black, issues);
        }

        var emitter = new Emitter();
        var resolved = new Dictionary<Guid, Slot[]>();
        var visiting = new HashSet<Guid>();

        var result = Resolve(output);
        var colour = emitter.ToColour(result.Length > 0 ? result[0] : emitter.Constant(0f));

        return new CompileResult(
            new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, colour.Base),
            issues);

        Slot[] Resolve(NodeInstance node)
        {
            if (resolved.TryGetValue(node.Id, out var cached)) return cached;

            var def = NodeCatalog.Get(node.TypeId);
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
