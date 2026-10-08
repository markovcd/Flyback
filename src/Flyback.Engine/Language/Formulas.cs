using Flyback.Core.Graph;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>
/// A sum in the text as the formula an Expression computes (ADR-0106): what
/// folds to a number, which signals it reads, and how it is written.
/// </summary>
internal static class Formulas
{
    internal const string Scaled = "arithmetic on a note or a duration would be done on a scale nobody meant.";

    /// <summary>A sum being reckoned: a number, a signal, or an operation on terms.</summary>
    internal abstract record Term;

    internal sealed record Operand(Figure Figure) : Term;

    /// <param name="From">The output the signal is read from, which is what tells two readings of one apart.</param>
    internal sealed record Signal(Value Value, (Guid Node, int Port) From) : Term;

    /// <param name="Right">Null for a negation, which is the one operation on a single term.</param>
    internal sealed record Operation(char Sign, Term Left, Term? Right, int Line, int Column) : Term;

    /// <summary>The sign an operator is written with in a formula.</summary>
    public static char Sign(TokenKind op) => op switch
    {
        TokenKind.Plus => '+',
        TokenKind.Minus => '-',
        TokenKind.Star => '*',
        TokenKind.Slash => '/',
        _ => '%',
    };

    /// <summary>Two numbers folded into one, or null and a complaint where one is a note or a duration.</summary>
    public static Operand? Fold(TokenKind op, Figure a, Figure b, int line, int column, Issues issues)
    {
        if (a.Style != NumberStyle.Plain || b.Style != NumberStyle.Plain)
        {
            issues.Complain(IssueCode.ScaledArithmetic, line, column, Scaled);
            return null;
        }

        var folded = op switch
        {
            TokenKind.Plus => a.Amount + b.Amount,
            TokenKind.Minus => a.Amount - b.Amount,
            TokenKind.Star => a.Amount * b.Amount,
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            TokenKind.Slash => b.Amount == 0d ? 0d : a.Amount / b.Amount,
            // The remainder Modulo takes, which wraps a negative number upwards.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            _ => b.Amount == 0d ? 0d : a.Amount - b.Amount * Math.Floor(a.Amount / b.Amount),
        };

        return new Operand(new Figure(folded, NumberStyle.Plain));
    }

    /// <summary>The output a value is read from, where it is a signal.</summary>
    public static (Guid Node, int Port)? From(Value value) => value switch
    {
        Placed placed => (placed.Id, 0),
        Socket socket => (socket.Id, socket.Port),
        Several { Items.Count: > 0 } several => From(several.Items[0]),
        _ => null,
    };

    /// <summary>Whether an operation reads more signals than an Expression has sockets.</summary>
    public static bool Overflows(Operation operation) => Inputs(operation).Count > Formula.Sockets.Length;

    /// <summary>Each distinct signal a term reads, in the order the formula first reads it.</summary>
    public static List<Signal> Inputs(Term term)
    {
        var found = new List<Signal>();

        Gather(term);
        return found;

        void Gather(Term part)
        {
            switch (part)
            {
                case Signal signal when found.All(seen => seen.From != signal.From):
                    found.Add(signal);
                    break;

                case Operation operation:
                    Gather(operation.Left);
                    if (operation.Right is not null) Gather(operation.Right);
                    break;
            }
        }
    }

    /// <summary>
    /// A term as the Expression's formula says it, each signal by the socket it
    /// arrives on, or null and a complaint where a number cannot go in one.
    /// </summary>
    public static string? Written(Term term, IReadOnlyList<Signal> inputs, Issues issues) =>
        Spelled(term, inputs, issues) is { } spelled ? Infix.Write(spelled) : null;

    private static Infix.Part? Spelled(Term term, IReadOnlyList<Signal> inputs, Issues issues)
    {
        switch (term)
        {
            case Operand { Figure: var figure }:
            {
                var where = figure.Where ?? new Site(0, 0);

                if (figure.Style != NumberStyle.Plain)
                {
                    issues.Complain(IssueCode.ScaledArithmetic, where.Line, where.Column, Scaled);
                    return null;
                }

                var value = (float)figure.Amount;

                if (!float.IsFinite(value))
                {
                    issues.Complain(IssueCode.NumberTooLarge, where.Line, where.Column, "that number is too large to hold.");
                    return null;
                }

                return new Infix.Number(value);
            }

            case Signal signal:
                return new Infix.Leaf(Formula.Sockets[inputs.ToList().FindIndex(input => input.From == signal.From)].ToString());

            case Operation { Right: null } negate:
                return Spelled(negate.Left, inputs, issues) is { } operand ? new Infix.Negation(operand) : null;

            case Operation operation:
            {
                if (Spelled(operation.Left, inputs, issues) is not { } left) return null;
                if (Spelled(operation.Right!, inputs, issues) is not { } right) return null;

                return new Infix.Operation(operation.Sign, left, right);
            }

            default:
                return null;
        }
    }
}
