using Flyback.Core.Graph;
using Flyback.Engine.Language.Ast.Expressions;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>
/// Where a pipe lands on a module that does not say: <c>in</c>, the sole input,
/// an x/y position, or the only color input for a color signal.
/// </summary>
/// <remarks>An ambiguous landing is refused rather than guessed at the first free input.</remarks>
internal static class PipeLanding
{
    internal sealed record Result(
        IReadOnlyList<(int Port, Value Value)> Inputs,
        (string Code, string Message)? Issue = null);

    internal static Result Resolve(NodeDef def, Value piped, IReadOnlySet<int> taken, CallExpr expr)
    {
        var signal = SocketNames.Find(def.Inputs, "in");
        var sole = signal >= 0 ? signal : def.Inputs.Count == 1 ? 0 : -1;

        if (sole >= 0 && !taken.Contains(sole))
            return Success((sole, piped.Part(0)));

        var free = Enumerable.Range(0, def.Inputs.Count).Where(i => !taken.Contains(i)).ToList();

        if (def.Inputs.Count == 0)
            return Refused(IssueCode.NoSocketFree, $"'{def.Name}' takes nothing in, so nothing can be piped into it.");

        if (free.Count == 0)
        {
            var last = SocketNames.Written(def.Inputs[^1]);

            return Refused(IssueCode.NoSocketFree,
                $"'{def.Name}' has no socket free for what is arriving: every one is given ({SocketNames.List(def.Inputs)}). "
                + $"Write '_' for the one the pipe fills, such as '{expr.Target}({last}: _)', or drop the pipe.");
        }

        var position = def.Inputs.Count >= 2
            && SocketNames.Same(def.Inputs[0].Name, "x")
            && SocketNames.Same(def.Inputs[1].Name, "y")
            && !taken.Contains(0)
            && !taken.Contains(1)
            && piped.Width >= 2;

        var colors = Enumerable.Range(0, def.Inputs.Count).Where(i => def.Inputs[i].Kind == PortKind.Color).ToList();

        if (!position && colors is [var tint] && !taken.Contains(tint) && piped.Kind == PortKind.Color)
            return Success((tint, piped.Part(0)));

        if (!position)
        {
            var example = SocketNames.Written(def.Inputs[free[0]]);
            var why = signal >= 0 ? "its 'in' is already given" : "it has no socket called 'in'";

            return Refused(IssueCode.PipeLandsNowhere,
                $"'{def.Name}': {why}, so say where the pipe lands: "
                + $"'{expr.Target}({example}: _)'. It has {SocketNames.List(def.Inputs)}.");
        }

        // Forwarding every output would let a sequencer's gate and index land in
        // a Note's octave and cents. Only the leading x/y pair denotes a position.
        return new Result([(0, piped.Part(0)), (1, piped.Part(1))]);
    }

    private static Result Success((int Port, Value Value) input) => new([input]);

    private static Result Refused(string code, string message) => new([], (code, message));
}
