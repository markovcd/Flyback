namespace Flyback.Core.Graph;

/// <summary>
/// Works out an Auto remap's ranges from what it is wired to: its input's from
/// the output feeding it, its output's from the sockets it feeds.
/// </summary>
public static class AutoRemap
{
    public const int In = 0;
    public const int InLow = 1;
    public const int InHigh = 2;
    public const int OutLow = 3;
    public const int OutHigh = 4;

    /// <summary>Every Auto remap in <paramref name="patch"/>, by node id.</summary>
    internal static IReadOnlyDictionary<Guid, RemapSpans> Resolve(Patch patch, ModuleCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var remaps = patch.Nodes.Where(n => n.TypeId == NodeCatalog.AutoRemapTypeId).ToList();
        if (remaps.Count == 0) return new Dictionary<Guid, RemapSpans>();

        var joined = Buses.Joined(patch);

        return remaps.ToDictionary(n => n.Id, n => Spans(joined, n, catalog ?? NodeCatalog.Current));
    }

    /// <summary>The ranges of the one Auto remap <paramref name="node"/>.</summary>
    internal static RemapSpans Of(Patch patch, NodeInstance node, ModuleCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(node);

        return Spans(Buses.Joined(patch), node, catalog ?? NodeCatalog.Current);
    }

    private static RemapSpans Spans(Patch patch, NodeInstance node, ModuleCatalog catalog)
    {
        var (inSpan, inWhy) = From(patch, node, catalog);
        var (outSpan, outWhy, narrow) = Into(patch, node, catalog);

        return new RemapSpans(inSpan, outSpan, inWhy, outWhy) { Narrow = narrow };
    }

    private static (RemapSpan?, string?) From(Patch patch, NodeInstance node, ModuleCatalog catalog)
    {
        if (patch.IncomingTo(node.Id, In) is not { } wire) return (RemapSpan.Unit, null);
        if (patch.Find(wire.SourceNode) is not { } source || catalog.Get(source.TypeId) is not { } def) return (null, "its input comes from nothing it knows");
        if (wire.SourcePort >= def.Outputs.Count) return (null, "its input comes from nothing it knows");

        return Emitted(patch, source, def, wire.SourcePort);
    }

    /// <summary>
    /// Whether <paramref name="wire"/> joins two sockets whose ranges are both known
    /// and differ, which is where an Auto remap in the middle of it has work to do.
    /// </summary>
    internal static bool Offered(Patch patch, Connection wire, ModuleCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(wire);

        catalog ??= NodeCatalog.Current;

        if (patch.Find(wire.SourceNode) is not { } source || catalog.Get(source.TypeId) is not { } from) return false;
        if (patch.Find(wire.TargetNode) is not { } target || catalog.Get(target.TypeId) is not { } into) return false;
        if (source.TypeId == NodeCatalog.AutoRemapTypeId || target.TypeId == NodeCatalog.AutoRemapTypeId) return false;
        if (wire.SourcePort >= from.Outputs.Count || wire.TargetPort >= into.Inputs.Count) return false;

        return Emitted(patch, source, from, wire.SourcePort).Span is { } given
            && Declared(into.Inputs[wire.TargetPort]) is { } taken
            && (given.Min, given.Max, given.Knee) != (taken.Min, taken.Max, taken.Knee);
    }

    /// <summary>
    /// What to say about <paramref name="wire"/> where what its source puts out
    /// reaches past either end of the range its socket takes, and null where it
    /// stays inside or either end has no range.
    /// </summary>
    internal static string? Overflow(Patch patch, Connection wire, ModuleCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(wire);

        catalog ??= NodeCatalog.Current;

        if (patch.Find(wire.SourceNode) is not { } source || catalog.Get(source.TypeId) is not { } from) return null;
        if (patch.Find(wire.TargetNode) is not { } target || catalog.Get(target.TypeId) is not { } into) return null;
        if (wire.SourcePort >= from.Outputs.Count || wire.TargetPort >= into.Inputs.Count) return null;

        // A color past 0..1 is clamped by the screen, which is how colors are meant.
        if (into.Inputs[wire.TargetPort] is { Lenient: true } or { Kind: PortKind.Color }) return null;

        if (Emitted(patch, source, from, wire.SourcePort).Span is not { } given) return null;
        if (Declared(into.Inputs[wire.TargetPort]) is not { } taken) return null;

        var (low, high) = (MathF.Min(taken.Min, taken.Max), MathF.Max(taken.Min, taken.Max));
        var (lowest, highest) = (MathF.Min(given.Min, given.Max), MathF.Max(given.Min, given.Max));

        if (lowest >= low && highest <= high) return null;

        var spec = into.Inputs[wire.TargetPort];
        var socket = new PortSpec("", Min: low, Max: high, Display: spec.Display);
        var output = new PortSpec("", Min: lowest, Max: highest, Display: given.Display);

        return $"{source.Title(from)}'s '{from.Outputs[wire.SourcePort].Name}' swings {output.Format(lowest)} to {output.Format(highest)}, "
            + $"past the {socket.Format(low)} to {socket.Format(high)} {target.Title(into)}'s '{spec.Name}' takes";
    }

    /// <summary>The range output <paramref name="port"/> of <paramref name="source"/> puts out, or why it has none.</summary>
    private static (RemapSpan? Span, string? Why) Emitted(Patch patch, NodeInstance source, NodeDef def, int port)
    {
        var spec = def.Outputs[port];
        var name = $"{source.Title(def)}'s '{spec.Name}'";

        if (Declared(spec) is not { } declared) return (null, $"{name} has no range");

        // An oscillator's range is -1..1 before its 'amp' and 'bias', so the two
        // knobs move it, and a wire on either leaves it unknowable.
        var amp = Knob(patch, source, def, "amp");
        var bias = Knob(patch, source, def, "bias");

        if (amp is float.NaN || bias is float.NaN) return (null, $"{name} moves with a patched 'amp' or 'bias'");

        var scale = amp ?? 1f;
        var shift = bias ?? 0f;

        return (declared with { Min = shift + scale * declared.Min, Max = shift + scale * declared.Max, Knee = declared.Knee * MathF.Abs(scale) }, null);
    }

    private static (RemapSpan?, string?, bool) Into(Patch patch, NodeInstance node, ModuleCatalog catalog)
    {
        var ranged = new List<(RemapSpan Span, string Name)>();
        string? unranged = null;
        var fed = 0;
        var scalars = 0;

        foreach (var wire in patch.Connections.Where(c => c.SourceNode == node.Id && c.SourcePort == 0))
        {
            if (patch.Find(wire.TargetNode) is not { } target || catalog.Get(target.TypeId) is not { } def) continue;
            if (wire.TargetPort >= def.Inputs.Count) continue;

            var spec = def.Inputs[wire.TargetPort];
            var name = $"{target.Title(def)}'s '{spec.Name}'";

            fed++;
            if (spec.Kind == PortKind.Scalar) scalars++;

            if (Declared(spec) is { } declared) ranged.Add((declared, name));
            else unranged ??= name;
        }

        var narrow = fed > 0 && scalars == fed;

        if (ranged.Count == 0) return unranged is null ? (RemapSpan.Unit, null, narrow) : (null, $"{unranged} has no range", narrow);

        var first = ranged[0];
        var other = ranged.FirstOrDefault(r => r.Span != first.Span).Name;

        return other is null ? (first.Span, null, narrow) : (null, $"{first.Name} and {other} take different ranges", narrow);
    }

    /// <summary>
    /// The range a socket declares, or null where it declares none. A color is 0..1
    /// on every channel, which is what the screen shows, whatever its placeholder says.
    /// </summary>
    private static RemapSpan? Declared(PortSpec spec) =>
        spec.Kind == PortKind.Color ? RemapSpan.Unit
        : spec.Ranged ? new RemapSpan(spec.Min, spec.Max, spec.Knee, spec.Display)
        : null;

    /// <summary>
    /// The knob <paramref name="name"/> rests on, null where the module has no such
    /// socket, and NaN where a wire or a panel knob moves it.
    /// </summary>
    private static float? Knob(Patch patch, NodeInstance node, NodeDef def, string name)
    {
        for (var i = 0; i < def.Inputs.Count; i++)
        {
            if (def.Inputs[i].Name != name) continue;

            if (patch.IncomingTo(node.Id, i) is not null || ControlMap.Of(node, i) is not null) return float.NaN;

            return i < node.InputValues.Length ? node.InputValues[i] : def.Inputs[i].Default;
        }

        return null;
    }
}
