using Flyback.Core.Graph;
using Flyback.Core.Language.Ast;
using Flyback.Core.Language.Ast.Expressions;

namespace Flyback.Core.Language;

/// <summary>Binds expression syntax, delegating calls and pipes to the patch binder.</summary>
internal sealed class ExpressionBinder
{
    private readonly Func<string, Binder.Value?> source;
    private readonly Func<Binder.Value> clock;
    private readonly Action<NameExpr> unknown;
    private readonly Action<Expr, Binder.Value> mention;
    private readonly Func<IReadOnlyList<PortSpec>, string, int> find;
    private readonly Func<IReadOnlyList<PortSpec>, string> list;
    private readonly Func<string, int, int, string, Binder.Value?> refuse;
    private readonly Func<CallExpr, Binder.Scope, Binder.Value?, Binder.Value?> call;
    private readonly Func<PipeExpr, Binder.Scope, Binder.Value?> pipe;
    private readonly Func<Expr, bool> placeholder;
    private readonly ArithmeticBinder arithmetic;

    internal sealed class Context
    {
        internal required Func<string, Binder.Value?> Source { get; init; }
        internal required Func<Binder.Value> Clock { get; init; }
        internal required Action<NameExpr> Unknown { get; init; }
        internal required Action<Expr, Binder.Value> Mention { get; init; }
        internal required Func<IReadOnlyList<PortSpec>, string, int> Find { get; init; }
        internal required Func<IReadOnlyList<PortSpec>, string> List { get; init; }
        internal required Func<string, int, int, string, Binder.Value?> Refuse { get; init; }
        internal required Func<CallExpr, Binder.Scope, Binder.Value?, Binder.Value?> Call { get; init; }
        internal required Func<PipeExpr, Binder.Scope, Binder.Value?> Pipe { get; init; }
        internal required Func<Expr, bool> Placeholder { get; init; }
        internal required Func<int, int, NodeDef?> ExpressionModule { get; init; }
        internal required Func<NodeDef, IReadOnlyList<(int Port, Binder.Value Value)>, int, int, Binder.Value> Place { get; init; }
        internal required Action<Binder.Value, string, int, int> ConfigureExpression { get; init; }
        internal required Action<string, int, int, string> Complain { get; init; }
    }

    internal ExpressionBinder(Context context)
    {
        source = context.Source;
        clock = context.Clock;
        unknown = context.Unknown;
        mention = context.Mention;
        find = context.Find;
        list = context.List;
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

    internal Binder.Value? Bind(Expr expr, Binder.Scope scope) => expr switch
    {
        NumberExpr number => new Binder.Figure(number.Value, number.Style, new Site(number.Line, number.Column)),
        TextExpr text => new Binder.Named(text.Value),
        NegateExpr or BinaryExpr => arithmetic.Bind(expr, scope),
        NameExpr name => Read(name, scope),
        CallExpr call => this.call(call, scope, null),
        SelectExpr select => Select(select, scope, piped: null),
        PipeExpr pipe => this.pipe(pipe, scope),
        RangeExpr range => refuse(IssueCode.RangeOutsideArgument, range.Line, range.Column, "a range only means something as an argument."),
        _ => null,
    };

    private Binder.Value? Read(NameExpr expr, Binder.Scope scope)
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

        if (bound is Binder.Failed) return null;

        mention(expr, bound);

        if (expr.Port is null) return bound;

        if (bound is not Binder.Placed)
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

    internal Binder.Value? Select(SelectExpr expr, Binder.Scope scope, Binder.Value? piped)
    {
        var source = expr.Source switch
        {
            CallExpr call => this.call(call, scope, piped),
            SelectExpr inner => Select(inner, scope, piped),
            _ => Bind(expr.Source, scope),
        };

        return source is null ? null : Output(source, expr.Port, expr.Line, expr.Column);
    }

    private Binder.Value? Output(Binder.Value value, string name, int line, int column)
    {
        if (value is not Binder.Placed placed)
            return refuse(IssueCode.NotAModule, line, column, $"this is not a module, so it has no output called '{name}'.");

        var port = find(placed.Def.Outputs, name);

        if (port < 0)
        {
            return refuse(IssueCode.UnknownOutput, line, column,
                $"'{placed.Def.Name}' has no output called '{name}'. It has {list(placed.Def.Outputs)}.");
        }

        return new Binder.Socket(placed.Id, port);
    }
}
