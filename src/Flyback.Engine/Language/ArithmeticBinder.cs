using Flyback.Core.Graph;
using Flyback.Engine.Language.Ast;
using Flyback.Engine.Language.Ast.Expressions;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>Binds numeric expressions to folded values or Formula-backed Expression modules.</summary>
internal sealed class ArithmeticBinder
{
    private readonly Func<Expr, Scope, Value?> bind;
    private readonly Func<int, int, NodeDef?> expressionModule;
    private readonly Func<NodeDef, IReadOnlyList<(int Port, Value Value)>, int, int, Value> place;
    private readonly Action<Value, string, int, int> configureExpression;
    private readonly Action<string, int, int, string> complain;

    internal ArithmeticBinder(
        Func<Expr, Scope, Value?> bind,
        Func<int, int, NodeDef?> expressionModule,
        Func<NodeDef, IReadOnlyList<(int Port, Value Value)>, int, int, Value> place,
        Action<Value, string, int, int> configureExpression,
        Action<string, int, int, string> complain)
    {
        this.bind = bind;
        this.expressionModule = expressionModule;
        this.place = place;
        this.configureExpression = configureExpression;
        this.complain = complain;
    }

    internal const string Scaled = "arithmetic on a note or a duration would be done on a scale nobody meant.";

    internal Value? Bind(Expr expr, Scope scope) => Reckon(expr, scope) switch
    {
        Operand operand => operand.Figure,
        Signal signal => signal.Value,
        Operation operation => Expression(operation),
        _ => null,
    };

    private abstract record Reckoned;

    private sealed record Operand(Figure Figure) : Reckoned;

    private sealed record Signal(Value Value, (Guid Node, int Port) From) : Reckoned;

    private sealed record Operation(char Sign, Reckoned Left, Reckoned? Right, int Line, int Column) : Reckoned;

    private Reckoned? Reckon(Expr expr, Scope scope)
    {
        switch (expr)
        {
            case NegateExpr negate:
            {
                if (Reckon(negate.Value, scope) is not { } value) return null;

                // The sign belongs to the number for diagnostics and write-back.
                if (value is Operand operand)
                {
                    return new Operand(operand.Figure with
                    {
                        Amount = -operand.Figure.Amount,
                        Where = new Site(negate.Line, negate.Column),
                    });
                }

                return Fit(new Operation('-', value, null, negate.Line, negate.Column));
            }

            case BinaryExpr binary:
            {
                if (Reckon(binary.Left, scope) is not { } left) return null;
                if (Reckon(binary.Right, scope) is not { } right) return null;

                if (left is Operand a && right is Operand b) return Folded(binary, a.Figure, b.Figure);

                var sign = binary.Operator switch
                {
                    TokenKind.Plus => '+',
                    TokenKind.Minus => '-',
                    TokenKind.Star => '*',
                    TokenKind.Slash => '/',
                    _ => '%',
                };

                return Fit(new Operation(sign, left, right, binary.Line, binary.Column));
            }

            default:
            {
                if (bind(expr, scope) is not { } value) return null;

                if (value is Figure figure) return new Operand(figure);

                // Each reference to a panel knob is a distinct formula input.
                if (value is Dial) return new Signal(value, (Guid.NewGuid(), 0));

                if (Output(value) is { } from) return new Signal(value, from);

                complain(IssueCode.NotASignal, expr.Line, expr.Column, "this is not a signal, so nothing can be wired from it.");
                return null;
            }
        }
    }

    private Operand? Folded(BinaryExpr expr, Figure a, Figure b)
    {
        if (a.Style != NumberStyle.Plain || b.Style != NumberStyle.Plain)
        {
            complain(IssueCode.ScaledArithmetic, expr.Line, expr.Column, Scaled);
            return null;
        }

        var folded = expr.Operator switch
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

    /// <summary>Splits an operation until each Formula module reads at most its available sockets.</summary>
    private Reckoned? Fit(Operation operation)
    {
        while (Inputs(operation).Count > Formula.Sockets.Length)
        {
            var left = Inputs(operation.Left).Count;
            var right = operation.Right is null ? 0 : Inputs(operation.Right).Count;

            if (left >= right)
            {
                if (Settled(operation.Left) is not { } settled) return null;
                operation = operation with { Left = settled };
            }
            else
            {
                if (Settled(operation.Right!) is not { } settled) return null;
                operation = operation with { Right = settled };
            }
        }

        return operation;
    }

    private Reckoned? Settled(Reckoned reckoned) =>
        reckoned is not Operation operation ? reckoned
        : Expression(operation) is { } placed && Output(placed) is { } from ? new Signal(placed, from)
        : null;

    /// <summary>Lists each distinct signal in the order the formula first reads it.</summary>
    private static List<Signal> Inputs(Reckoned reckoned)
    {
        var found = new List<Signal>();

        Gather(reckoned);
        return found;

        void Gather(Reckoned part)
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

    /// <summary>Places an operation as an Expression module and records its formula and source location.</summary>
    private Value? Expression(Operation operation)
    {
        var inputs = Inputs(operation);

        if (Written(operation, inputs) is not { } formula) return null;
        if (expressionModule(operation.Line, operation.Column) is not { } def) return null;

        var placed = place(def, [.. inputs.Select((input, socket) => (socket, input.Value))], operation.Line, operation.Column);
        configureExpression(placed, formula, operation.Line, operation.Column);

        return placed;
    }

    private string? Written(Reckoned reckoned, IReadOnlyList<Signal> inputs)
    {
        switch (reckoned)
        {
            case Operand { Figure: var figure }:
            {
                var where = figure.Where ?? new Site(0, 0);

                if (figure.Style != NumberStyle.Plain)
                {
                    complain(IssueCode.ScaledArithmetic, where.Line, where.Column, Scaled);
                    return null;
                }

                var value = (float)figure.Amount;

                if (!float.IsFinite(value))
                {
                    complain(IssueCode.NumberTooLarge, where.Line, where.Column, "that number is too large to hold.");
                    return null;
                }

                return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }

            case Signal signal:
                return Formula.Sockets[inputs.ToList().FindIndex(input => input.From == signal.From)].ToString();

            case Operation { Right: null } negate:
            {
                if (Written(negate.Left, inputs) is not { } operand) return null;

                return Strength(negate.Left) < Strength(negate) ? $"-({operand})" : $"-{operand}";
            }

            case Operation operation:
            {
                if (Written(operation.Left, inputs) is not { } left) return null;
                if (Written(operation.Right!, inputs) is not { } right) return null;

                var strength = Strength(operation);

                if (Strength(operation.Left) < strength) left = $"({left})";
                if (Strength(operation.Right!) <= strength) right = $"({right})";

                return $"{left} {operation.Sign} {right}";
            }

            default:
                return null;
        }
    }

    private static int Strength(Reckoned reckoned) => reckoned switch
    {
        Operation { Right: null } => 3,
        Operation { Sign: '+' or '-' } => 1,
        Operation => 2,
        Operand { Figure.Amount: < 0 } => 3,
        _ => 4,
    };

    private static (Guid Node, int Port)? Output(Value value) => value switch
    {
        Placed placed => (placed.Id, 0),
        Socket socket => (socket.Id, socket.Port),
        Several { Items.Count: > 0 } several => Output(several.Items[0]),
        _ => null,
    };
}
