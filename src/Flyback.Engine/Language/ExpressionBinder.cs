using Flyback.Core.Graph;
using Flyback.Engine.Language.Ast;
using Flyback.Engine.Language.Ast.Expressions;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>Binds expression syntax, delegating calls and pipes to the patch binder.</summary>
internal sealed class ExpressionBinder
{
    private readonly Func<string, Value?> source;
    private readonly Func<Value> clock;
    private readonly Action<NameExpr> unknown;
    private readonly Action<Expr, Value> mention;
    private readonly Func<string, int, int, string, Value?> refuse;
    private readonly Func<CallExpr, Scope, Value?, Value?> call;
    private readonly Func<PipeExpr, Scope, Value?> pipe;
    private readonly Func<Expr, bool> placeholder;
    private readonly ArithmeticBinder arithmetic;

    internal sealed class Context
    {
        internal required Func<string, Value?> Source { get; init; }
        internal required Func<Value> Clock { get; init; }
        internal required Action<NameExpr> Unknown { get; init; }
        internal required Action<Expr, Value> Mention { get; init; }
        internal required Func<string, int, int, string, Value?> Refuse { get; init; }
        internal required Func<CallExpr, Scope, Value?, Value?> Call { get; init; }
        internal required Func<PipeExpr, Scope, Value?> Pipe { get; init; }
        internal required Func<Expr, bool> Placeholder { get; init; }
        internal required Func<int, int, NodeDef?> ExpressionModule { get; init; }
        internal required Func<NodeDef, IReadOnlyList<(int Port, Value Value)>, int, int, Value> Place { get; init; }
        internal required Action<Value, string, int, int> ConfigureExpression { get; init; }
        internal required Action<string, int, int, string> Complain { get; init; }
    }

    internal ExpressionBinder(Context context)
    {
        source = context.Source;
        clock = context.Clock;
        unknown = context.Unknown;
        mention = context.Mention;
        refuse = context.Refuse;
        call = context.Call;
        pipe = context.Pipe;
        placeholder = context.Placeholder;
        arithmetic = new ArithmeticBinder(
            Bind,
            context.ExpressionModule,
            context.Place,
            context.ConfigureExpression,
            context.Complain);
    }

    internal Value? Bind(Expr expr, Scope scope) => expr switch
    {
        NumberExpr number => new Figure(number.Value, number.Style, new Site(number.Line, number.Column)),
        TextExpr text => new Named(text.Value),
        NegateExpr or BinaryExpr => arithmetic.Bind(expr, scope),
        NameExpr name => Read(name, scope),
        CallExpr call => this.call(call, scope, null),
        SelectExpr select => Select(select, scope, piped: null),
        PipeExpr pipe => this.pipe(pipe, scope),
        RangeExpr range => refuse(IssueCode.RangeOutsideArgument, range.Line, range.Column, "a range only means something as an argument."),
        _ => null,
    };

    private Value? Read(NameExpr expr, Scope scope)
    {
        if (expr.Port is null && source(expr.Name) is { } value) return value;

        if (expr is { Name: "t", Port: { } port }) return Output(clock(), port, expr.Line, expr.Column);

        if (expr.Name == "out")
        {
            return refuse(IssueCode.OutputIsNotASource, expr.Line, expr.Column,
                "the Output has nothing to read. Pipe something into 'out.color' or 'out.left'.");
        }

        if (placeholder(expr))
            return refuse(IssueCode.PlaceholderMisplaced, expr.Line, expr.Column, "'_' stands for what is piped in, as a call's argument: 'socket: _'.");

        if (scope.Find(expr.Name) is not { } bound)
        {
            unknown(expr);
            return null;
        }

        if (bound is Failed) return null;

        mention(expr, bound);

        if (expr.Port is null) return bound;

        if (bound is not Placed)
            return refuse(IssueCode.NotAModule, expr.Line, expr.Column, $"'{expr.Name}' is not a module, so it has no sockets.");

        return Output(bound, expr.Port, expr.Line, expr.Column);
    }

    internal static string? StatementWord(string name) => name switch
    {
        "requires" => "requires flyback.picture",
        "keyboard" => "keyboard piano",
        "off" => "off drone",
        "description" => "description \"A slow drone\"",
        "author" => "author \"Ada\"",
        "tags" => "tags \"drone\" \"slow\"",
        "panel" => "panel level = 0.5",
        _ => null,
    };

    internal Value? Select(SelectExpr expr, Scope scope, Value? piped)
    {
        var source = expr.Source switch
        {
            CallExpr call => this.call(call, scope, piped),
            SelectExpr inner => Select(inner, scope, piped),
            _ => Bind(expr.Source, scope),
        };

        return source is null ? null : Output(source, expr.Port, expr.Line, expr.Column);
    }

    private Value? Output(Value value, string name, int line, int column)
    {
        if (value is not Placed placed)
            return refuse(IssueCode.NotAModule, line, column, $"this is not a module, so it has no output called '{name}'.");

        var port = SocketNames.Find(placed.Def.Outputs, name);

        if (port < 0)
        {
            return refuse(IssueCode.UnknownOutput, line, column,
                $"'{placed.Def.Name}' has no output called '{name}'. It has {SocketNames.List(placed.Def.Outputs)}.");
        }

        return new Socket(placed.Id, placed.Def, port);
    }
}
