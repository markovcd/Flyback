using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;

namespace Flyback.Engine.Slopes;

/// <summary>A sink's program compiled with its knobs as live inputs, and those knobs where they rest.</summary>
internal sealed class SlopeProgram
{
    private SlopeProgram(CompiledPatch program, IReadOnlyList<Knob> knobs, int[] knobOf, IReadOnlyList<CompileIssue> issues)
    {
        Program = program;
        Knobs = knobs;
        KnobOf = knobOf;
        Issues = issues;
        Live = new LiveValues(program.LiveInputs);

        foreach (var knob in knobs) Live.Set(knob.Key, knob.Value);
    }

    public CompiledPatch Program { get; }

    /// <summary>Every knob the program reads, in the order of its live inputs.</summary>
    public IReadOnlyList<Knob> Knobs { get; }

    /// <summary>For each live input, the knob it is, or -1 for one that is played rather than turned.</summary>
    public int[] KnobOf { get; }

    public IReadOnlyList<CompileIssue> Issues { get; }

    /// <summary>The knobs' values as the program reads them.</summary>
    public LiveValues Live { get; }

    /// <summary>Turns knob <paramref name="knob"/> to <paramref name="value"/>.</summary>
    public void Turn(int knob, float value) => Live.Set(Knobs[knob].Key, value);

    /// <summary>Where knob <paramref name="knob"/> is now.</summary>
    public float At(int knob) => Live.Find(Knobs[knob].Key) ?? Knobs[knob].Value;

    /// <summary>Puts every knob back where it rests.</summary>
    public void Rest()
    {
        foreach (var knob in Knobs) Live.Set(knob.Key, knob.Value);
    }

    public static SlopeProgram Compile(
        Patch patch,
        bool sound,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null)
    {
        var catalog = modules ?? NodeCatalog.Current;
        var compiled = patch.CompileForSlopes(sound, catalog, samples, pictures);
        var program = compiled.Program;

        var sockets = new Dictionary<string, (NodeInstance Node, NodeDef Def, int Port)>();

        foreach (var node in patch.Nodes)
        {
            if (catalog.Get(node.TypeId) is not { } def) continue;

            for (var port = 0; port < def.Inputs.Count; port++)
                sockets[PatchCompiler.KnobKey(node.Id, port)] = (node, def, port);
        }

        var panel = (patch.Controls ?? []).ToDictionary(control => control.Key);
        var knobs = new List<Knob>();
        var knobOf = new int[program.LiveInputs.Count];

        for (var i = 0; i < knobOf.Length; i++)
        {
            var key = program.LiveInputs[i];
            knobOf[i] = -1;

            if (sockets.TryGetValue(key, out var socket))
            {
                var spec = socket.Def.Inputs[socket.Port];
                var value = socket.Port < socket.Node.InputValues.Length ? socket.Node.InputValues[socket.Port] : spec.Default;

                knobOf[i] = knobs.Count;
                knobs.Add(new Knob(key, socket.Node.Id, socket.Port, $"{socket.Node.Title(socket.Def)}.{spec.Name}", value, spec.Min, spec.Max));
            }
            else if (panel.TryGetValue(key, out var control))
            {
                knobOf[i] = knobs.Count;
                knobs.Add(new Knob(key, control.Id, -1, $"panel.{control.Name}", control.Value, 0f, 1f));
            }
        }

        return new SlopeProgram(program, knobs, knobOf, compiled.Issues);
    }
}
