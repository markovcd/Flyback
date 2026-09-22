namespace Flyback.Core.Graph;

/// <summary>
/// The range an Auto remap's pair of knobs are fractions of, swept the way the
/// socket at the far end of the wire sweeps its own knob.
/// </summary>
public readonly record struct RemapSpan(float Min, float Max, float Knee = 0f, PortDisplay Display = PortDisplay.Number)
{
    /// <summary>What an unwired side is: fractions of 0..1, which are the numbers themselves.</summary>
    public static RemapSpan Unit => new(0f, 1f);

    /// <summary>
    /// The value <paramref name="travel"/> of the way along, unclamped so a knob
    /// past 1 carries on the way the compiled module does.
    /// </summary>
    public float At(float travel)
    {
        if (Knee <= 0f) return Min + (Max - Min) * travel;

        var low = MathF.Min(Min, Max);
        var span = MathF.Abs(Max - Min);
        var along = Max < Min ? 1f - travel : travel;

        return low + Knee * (MathF.Exp(along * MathF.Log(1f + span / Knee)) - 1f);
    }

    /// <summary>The value <paramref name="travel"/> of the way along, written the way the socket writes its own.</summary>
    public string Format(float travel) => new PortSpec("", Min: Min, Max: Max, Display: Display).Format(At(travel));

    /// <summary>How far along <paramref name="value"/> sits, 0 to 1 inside the range, the inverse of <see cref="At"/>.</summary>
    public float Travel(float value)
    {
        if (Knee <= 0f) return (value - Min) / (Max - Min);

        var low = MathF.Min(Min, Max);
        var up = MathF.Log(1f + (value - low) / Knee) / MathF.Log(1f + MathF.Abs(Max - Min) / Knee);

        return Max < Min ? 1f - up : up;
    }
}

/// <summary>
/// What an Auto remap's two pairs of knobs mean: fractions of a range, or,
/// where a side is null, numbers typed by hand, with the reason it has no range.
/// </summary>
public sealed record RemapSpans(RemapSpan? In, RemapSpan? Out, string? InWhy = null, string? OutWhy = null)
{
    public static RemapSpans Unwired { get; } = new(RemapSpan.Unit, RemapSpan.Unit);

    /// <summary>
    /// Whether everything it feeds takes a single number, so a color arriving is
    /// turned into its brightness before it is remapped rather than after.
    /// </summary>
    public bool Narrow { get; init; }
}

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
    public static IReadOnlyDictionary<Guid, RemapSpans> Resolve(Patch patch, ModuleCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var remaps = patch.Nodes.Where(n => n.TypeId == NodeCatalog.AutoRemapTypeId).ToList();
        if (remaps.Count == 0) return new Dictionary<Guid, RemapSpans>();

        var joined = Buses.Joined(patch);

        return remaps.ToDictionary(n => n.Id, n => Spans(joined, n, catalog ?? NodeCatalog.Current));
    }

    /// <summary>The ranges of the one Auto remap <paramref name="node"/>.</summary>
    public static RemapSpans Of(Patch patch, NodeInstance node, ModuleCatalog? catalog = null)
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

        var spec = def.Outputs[wire.SourcePort];
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
