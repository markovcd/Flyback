using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Engine.Packs;

/// <summary>
/// One pack module built once, around stand-ins: the modules it is made of, the socket
/// standing for each input and the result taking each output. Placing the module copies it.
/// </summary>
/// <param name="Inner">The modules and wires the body builds, with the stand-ins and nothing it does not reach.</param>
/// <param name="Sockets">The stand-in for each input, in order.</param>
/// <param name="Results">The stand-in taking each output, in order.</param>
internal sealed record PackStencil(Patch Inner, IReadOnlyList<Guid> Sockets, IReadOnlyList<Guid> Results)
{
    /// <summary>
    /// Builds <paramref name="module"/> from its pack's <paramref name="source"/>, its
    /// inputs fed by stand-ins, its outputs taken by them and its settings at <paramref name="chosen"/>.
    /// </summary>
    /// <param name="chosen">One value for each of the module's choices, in order.</param>
    /// <returns>The stencil, or the issues that stopped it, reported on the pack's own lines.</returns>
    public static (PackStencil? Stencil, IReadOnlyList<LanguageIssue> Issues) Build(
        string source, PackModule module, IReadOnlyList<string> chosen, ModuleCatalog catalog)
    {
        var harness = new System.Text.StringBuilder(source).Append('\n');
        var inputs = module.Sockets.Select((_, i) => $"pack_socket_{i}__").ToList();
        var outputs = module.Outputs.Select((_, i) => $"pack_result_{i}__").ToList();

        foreach (var input in inputs) harness.Append($"let {input} = {PackStandIns.SocketTypeId}()\n");

        var arguments = string.Join(", ", [
            .. module.Sockets.Select((socket, i) => $"{socket.Name}: {inputs[i]}"),
            .. module.Choices.Select((choice, i) => $"{choice.Name}: {(choice.Texts ? $"\"{chosen[i]}\"" : chosen[i])}"),
        ]);
        var taken = outputs.Count == 1 ? outputs[0] : $"({string.Join(", ", outputs)})";

        harness.Append($"let {taken} = {module.Name}({arguments})\n");

        foreach (var output in outputs) harness.Append($"{PackStandIns.ResultTypeId}(in: {output})\n");

        var load = PatchLanguage.Build(harness.ToString(), PackStandIns.Into(catalog));

        if (!load.Ok) return (null, [.. load.Issues.Where(issue => issue.IsError)]);

        var patch = load.Patch;
        var sockets = patch.Nodes.Where(n => n.TypeId == PackStandIns.SocketTypeId).Select(n => n.Id).ToList();
        var results = patch.Nodes.Where(n => n.TypeId == PackStandIns.ResultTypeId).Select(n => n.Id).ToList();

        return (new PackStencil(Reached(patch, results), sockets, results), []);
    }

    /// <summary>What the results reach back to through the wires: the module's body, without the rest of the pack.</summary>
    private static Patch Reached(Patch patch, IReadOnlyList<Guid> results)
    {
        var kept = new HashSet<Guid>(results);
        var waiting = new Stack<Guid>(results);

        while (waiting.TryPop(out var id))
            foreach (var wire in patch.Connections.Where(c => c.TargetNode == id))
                if (kept.Add(wire.SourceNode)) waiting.Push(wire.SourceNode);

        return new Patch
        {
            Nodes = [.. patch.Nodes.Where(n => kept.Contains(n.Id))],
            Connections = [.. patch.Connections.Where(c => kept.Contains(c.TargetNode))],
        };
    }
}
