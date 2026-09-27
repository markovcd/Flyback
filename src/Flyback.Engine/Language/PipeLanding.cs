using Flyback.Core.Graph;
using Flyback.Core.Language.Ast.Expressions;

namespace Flyback.Core.Language;

internal sealed class PipeLanding
{
    internal sealed record Result(
        IReadOnlyList<(int Port, Binder.Value Value)> Inputs,
        (string Code, string Message)? Issue = null);

    private readonly Func<IReadOnlyList<PortSpec>, string, int> find;
    private readonly Func<IReadOnlyList<PortSpec>, string> list;
    private readonly Func<Binder.Value, PortKind?> kindOf;

    internal PipeLanding(
        Func<IReadOnlyList<PortSpec>, string, int> find,
        Func<IReadOnlyList<PortSpec>, string> list,
        Func<Binder.Value, PortKind?> kindOf)
    {
        this.find = find;
        this.list = list;
        this.kindOf = kindOf;
    }

    /// <summary>
    /// Resolves a pipe's implicit destination: <c>in</c>, the sole input, an
    /// x/y position, or the only color input for a color signal.
    /// </summary>
    /// <remarks>Ambiguous destinations are an error rather than a guess at the first free input.</remarks>
    internal Result Resolve(NodeDef def, Binder.Value piped, IReadOnlySet<int> taken, CallExpr expr)
    {
        var signal = find(def.Inputs, "in");
        var sole = signal >= 0 ? signal : def.Inputs.Count == 1 ? 0 : -1;

        if (sole >= 0 && !taken.Contains(sole))
            return Success((sole, Part(piped, 0)));

        var free = Enumerable.Range(0, def.Inputs.Count).Where(i => !taken.Contains(i)).ToList();

        if (free.Count == 0)
            return Refused(IssueCode.NoSocketFree, $"'{def.Name}' has no socket free for what is arriving.");

        var position = def.Inputs.Count >= 2
            && Binder.Same(def.Inputs[0].Name, "x")
            && Binder.Same(def.Inputs[1].Name, "y")
            && !taken.Contains(0)
            && !taken.Contains(1)
            && Width(piped) >= 2;

        var colors = Enumerable.Range(0, def.Inputs.Count).Where(i => def.Inputs[i].Kind == PortKind.Color).ToList();

        if (!position && colors is [var tint] && !taken.Contains(tint) && kindOf(piped) == PortKind.Color)
            return Success((tint, Part(piped, 0)));

        if (!position)
        {
            var example = def.Inputs[free[0]].Name.Replace(' ', '_');
            var why = signal >= 0 ? "its 'in' is already given" : "it has no socket called 'in'";

            return Refused(IssueCode.PipeLandsNowhere,
                $"'{def.Name}': {why}, so say where the pipe lands: "
                + $"'{expr.Target}({example}: _)'. It has {list(def.Inputs)}.");
        }

        // Forwarding every output would let a sequencer's gate and index land in
        // a Note's octave and cents. Only the leading x/y pair denotes a position.
        return new Result([(0, Part(piped, 0)), (1, Part(piped, 1))]);
    }

    private static Result Success((int Port, Binder.Value Value) input) => new([input]);

    private static Result Refused(string code, string message) => new([], (code, message));

    private static int Width(Binder.Value value) => value switch
    {
        Binder.Placed placed => placed.Def.Outputs.Count,
        Binder.Several several => several.Items.Count,
        _ => 1,
    };

    private static Binder.Value Part(Binder.Value value, int index) => value switch
    {
        Binder.Placed placed => new Binder.Socket(placed.Id, index),
        Binder.Several several => several.Items[index],
        _ => value,
    };
}
