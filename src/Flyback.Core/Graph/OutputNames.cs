using Flyback.Core.Graph.Extras;

namespace Flyback.Core.Graph;

/// <summary>What one module's outputs are called: the definition's names, or the Song's part names.</summary>
internal static class OutputNames
{
    /// <summary>What output <paramref name="port"/> of <paramref name="node"/> is called.</summary>
    public static string Of(NodeInstance node, NodeDef def, int port) =>
        def.Extra<SongExtra>() is not null && SongExtra.OutputName(node, port) is { } part ? part : def.Outputs[port].Name;

    /// <summary>The output of <paramref name="node"/> called <paramref name="name"/> by one of its own names, or -1.</summary>
    public static int Own(NodeInstance node, NodeDef def, string name) =>
        def.Extra<SongExtra>() is not null ? SongExtra.OutputOf(node, name) : -1;
}
