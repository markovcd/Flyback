namespace Flyback.Core.Language;

/// <summary>
/// Tokens to a syntax tree, by recursive descent.
/// </summary>
/// <remarks>
/// Nothing here knows what a module is. The parser's whole job is shape — that
/// a call has arguments and a pipeline has stages — and every question about
/// whether a name exists, how many sockets it has or which one a pipe lands on
/// belongs to <see cref="Binder"/>. Keeping the two apart is what lets the
/// catalogue be the language without the grammar depending on it.
/// <para>
/// Recovery is by statement: a line that cannot be read is reported and skipped
/// to the next break, so a file with three mistakes says three things.
/// </para>
/// </remarks>
public sealed class Parser(IReadOnlyList<Token> tokens, List<LanguageIssue> issues)
{
    private int at;

    private Token Current => tokens[Math.Min(at, tokens.Count - 1)];

    private Token Ahead(int by = 1) => tokens[Math.Min(at + by, tokens.Count - 1)];

    /// <summary>Every statement in the file.</summary>
    public IReadOnlyList<Statement> Parse()
    {
        var statements = new List<Statement>();

        SkipBreaks();

        while (Current.Kind != TokenKind.End)
        {
            var before = at;

            if (Statement() is { } statement) statements.Add(statement);

            // Whatever happened, do not sit still: a statement parser that
            // consumed nothing would spin here forever on the token it could
            // not read.
            if (at == before) at++;

            SkipToBreak();
            SkipBreaks();
        }

        return statements;
    }

    private void SkipBreaks()
    {
        while (Current.Kind == TokenKind.NewLine) at++;
    }

    private void SkipToBreak()
    {
        while (Current.Kind is not (TokenKind.NewLine or TokenKind.End)) at++;
    }

    private bool Take(TokenKind kind)
    {
        if (Current.Kind != kind) return false;
        at++;
        return true;
    }

    private bool Expect(TokenKind kind, string what)
    {
        if (Take(kind)) return true;

        Complain($"expected {what}.");
        return false;
    }

    private void Complain(string message) =>
        issues.Add(new LanguageIssue(Current.Line, Current.Column, message));

    private bool AtWord(string word) =>
        Current.Kind == TokenKind.Identifier && Current.Text == word;

    // --- statements ---------------------------------------------------------

    private Statement? Statement()
    {
        var line = Current.Line;
        var column = Current.Column;

        if (AtWord("let")) return Let(line, column);
        if (AtWord("def")) return Def(line, column);
        if (AtWord("group")) return Group(line, column);

        // A knob or a back-wire begins the same way an ordinary pipeline does,
        // so which it is only shows up at the operator after the name.
        if (Current.Kind == TokenKind.Identifier
            && Ahead().Kind == TokenKind.Dot
            && Ahead(2).Kind == TokenKind.Identifier
            && Ahead(3).Kind is TokenKind.Assign or TokenKind.BackWire)
        {
            var target = new NameExpr(Current.Text, Ahead(2).Text, line, column);
            var backWire = Ahead(3).Kind == TokenKind.BackWire;

            at += 4;

            if (Pipeline() is not { } value) return null;

            return backWire
                ? new BackWireStatement(target, value, line, column)
                : new KnobStatement(target, value, line, column);
        }

        return Pipeline() is { } pipeline ? new PipelineStatement(pipeline, line, column) : null;
    }

    private Statement? Let(int line, int column)
    {
        at++;

        if (Take(TokenKind.OpenParen))
        {
            var names = new List<string>();

            do
            {
                if (Current.Kind != TokenKind.Identifier)
                {
                    Complain("expected a name inside the brackets.");
                    return null;
                }

                names.Add(Current.Text);
                at++;
            }
            while (Take(TokenKind.Comma));

            if (!Expect(TokenKind.CloseParen, "')' after the names")) return null;
            if (!Expect(TokenKind.Assign, "'=' after the names")) return null;
            if (Pipeline() is not { } tuple) return null;

            return new LetTupleStatement(names, tuple, line, column);
        }

        if (Current.Kind != TokenKind.Identifier)
        {
            Complain("expected a name after 'let'.");
            return null;
        }

        var name = Current.Text;
        at++;

        if (!Expect(TokenKind.Assign, "'=' after the name")) return null;
        if (Pipeline() is not { } value) return null;

        return new LetStatement(name, value, line, column);
    }

    private Statement? Def(int line, int column)
    {
        at++;

        if (Current.Kind != TokenKind.Identifier)
        {
            Complain("expected a name after 'def'.");
            return null;
        }

        var name = Current.Text;
        at++;

        if (!Expect(TokenKind.OpenParen, "'(' after the name")) return null;

        var parameters = new List<string>();

        if (!Take(TokenKind.CloseParen))
        {
            do
            {
                if (Current.Kind != TokenKind.Identifier)
                {
                    Complain("expected a parameter name.");
                    return null;
                }

                parameters.Add(Current.Text);
                at++;
            }
            while (Take(TokenKind.Comma));

            if (!Expect(TokenKind.CloseParen, "')' after the parameters")) return null;
        }

        if (!Expect(TokenKind.Assign, "'=' after the parameters")) return null;

        // A body is either one pipeline or a block ending in what it hands back.
        if (!Take(TokenKind.OpenBrace))
        {
            return Pipeline() is { } single
                ? new DefStatement(name, parameters, [], single, null, line, column)
                : null;
        }

        var body = new List<Statement>();
        Expr? result = null;
        IReadOnlyList<Expr>? results = null;

        SkipBreaks();

        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.End))
        {
            // The last thing in the block is what the def is worth, and it is
            // an expression rather than a statement. Recognised by being the
            // last: anything followed by the closing brace.
            if (Tuple(out var several, out var one))
            {
                SkipBreaks();

                if (Current.Kind == TokenKind.CloseBrace)
                {
                    results = several;
                    result = one;
                    break;
                }

                // Not the last after all, so it was a statement in its own
                // right. Only a bare pipeline can reach here.
                if (one is not null) body.Add(new PipelineStatement(one, one.Line, one.Column));
                continue;
            }

            var before = at;

            if (Statement() is { } statement) body.Add(statement);
            if (at == before) at++;

            SkipBreaks();
        }

        if (!Expect(TokenKind.CloseBrace, "'}' to close the body")) return null;
        if (result is null && results is null) Complain("this body says nothing at the end of it.");

        return new DefStatement(name, parameters, body, result, results, line, column);
    }

    /// <summary>
    /// The thing a def's block ends with, which may be a bracketed list of
    /// several. Answers false where the next thing is plainly a statement.
    /// </summary>
    private bool Tuple(out IReadOnlyList<Expr>? several, out Expr? one)
    {
        several = null;
        one = null;

        if (AtWord("let") || AtWord("def") || AtWord("group")) return false;

        if (Current.Kind == TokenKind.OpenParen)
        {
            var mark = at;
            at++;

            var items = new List<Expr>();

            if (Pipeline() is { } first)
            {
                items.Add(first);

                while (Take(TokenKind.Comma))
                {
                    if (Pipeline() is not { } next) { at = mark; return false; }
                    items.Add(next);
                }

                if (items.Count > 1 && Take(TokenKind.CloseParen))
                {
                    several = items;
                    return true;
                }
            }

            at = mark;
        }

        one = Pipeline();
        return one is not null;
    }

    private Statement? Group(int line, int column)
    {
        at++;

        if (Current.Kind != TokenKind.Text)
        {
            Complain("expected a name in quotes after 'group'.");
            return null;
        }

        var name = Current.Text;
        at++;

        if (!Expect(TokenKind.OpenBrace, "'{' after the name")) return null;

        var body = new List<Statement>();

        SkipBreaks();

        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.End))
        {
            var before = at;

            if (Statement() is { } statement) body.Add(statement);
            if (at == before) at++;

            SkipToBreakOrBrace();
            SkipBreaks();
        }

        if (!Expect(TokenKind.CloseBrace, "'}' to close the group")) return null;

        return new GroupStatement(name, body, line, column);
    }

    private void SkipToBreakOrBrace()
    {
        while (Current.Kind is not (TokenKind.NewLine or TokenKind.CloseBrace or TokenKind.End)) at++;
    }

    // --- expressions --------------------------------------------------------

    /// <summary>
    /// The loosest thing there is. Everything else binds tighter, which is what
    /// makes <c>t * 0.2 |&gt; sine()</c> read the way it looks.
    /// </summary>
    private Expr? Pipeline()
    {
        if (Sum() is not { } left) return null;

        while (Current.Kind == TokenKind.Pipe)
        {
            var line = Current.Line;
            var column = Current.Column;

            at++;

            if (Stage() is not { } stage) return null;

            left = new PipeExpr(left, stage, line, column);
        }

        return left;
    }

    /// <summary>What may sit after a pipe: a module to place, or a socket to land in.</summary>
    private Expr? Stage()
    {
        if (Current.Kind != TokenKind.Identifier)
        {
            Complain("expected a module or a socket after '|>'.");
            return null;
        }

        return Primary();
    }

    private Expr? Sum()
    {
        if (Product() is not { } left) return null;

        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = Current.Kind;
            var line = Current.Line;
            var column = Current.Column;

            at++;

            if (Product() is not { } right) return null;

            left = new BinaryExpr(op, left, right, line, column);
        }

        return left;
    }

    private Expr? Product()
    {
        if (Ranged() is not { } left) return null;

        while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            var op = Current.Kind;
            var line = Current.Line;
            var column = Current.Column;

            at++;

            if (Ranged() is not { } right) return null;

            left = new BinaryExpr(op, left, right, line, column);
        }

        return left;
    }

    /// <summary>
    /// A value, or two of them written as a span.
    /// </summary>
    /// <remarks>
    /// Looser than the leading minus, and that is the whole point of its sitting
    /// here: <c>-2..2</c> is a range from minus two, not the negation of a range
    /// from two. The other way round parses, compiles and means something else,
    /// which two of the presets would have shown the hard way.
    /// </remarks>
    private Expr? Ranged()
    {
        if (Unary() is not { } low) return null;
        if (Current.Kind != TokenKind.Range) return low;

        var line = Current.Line;
        var column = Current.Column;

        at++;

        if (Unary() is not { } high) return null;

        return new RangeExpr(low, high, line, column);
    }

    private Expr? Unary()
    {
        if (Current.Kind != TokenKind.Minus) return Primary();

        var line = Current.Line;
        var column = Current.Column;

        at++;

        return Unary() is { } value ? new NegateExpr(value, line, column) : null;
    }

    private Expr? Primary()
    {
        var line = Current.Line;
        var column = Current.Column;

        switch (Current.Kind)
        {
            case TokenKind.Number:
            {
                var token = Current;
                at++;
                return new NumberExpr(token.Value, token.Scaled, line, column);
            }

            case TokenKind.Text:
            {
                var text = Current.Text;
                at++;
                return new TextExpr(text, line, column);
            }

            case TokenKind.OpenParen:
            {
                at++;

                if (Pipeline() is not { } inner) return null;

                return Expect(TokenKind.CloseParen, "')'") ? inner : null;
            }

            case TokenKind.Identifier:
                return NameOrCall(line, column);

            default:
                Complain("expected a value here.");
                return null;
        }
    }

    /// <summary>
    /// A dotted name, which is either a module to place or a binding to read —
    /// and the parser does not decide which. <c>space.rotate(...)</c> and
    /// <c>riff.gate</c> are the same shape until the catalogue is consulted.
    /// </summary>
    private Expr? NameOrCall(int line, int column)
    {
        var parts = new List<string> { Current.Text };
        at++;

        while (Current.Kind == TokenKind.Dot && Ahead().Kind == TokenKind.Identifier)
        {
            parts.Add(Ahead().Text);
            at += 2;
        }

        if (Current.Kind != TokenKind.OpenParen)
        {
            return parts.Count switch
            {
                1 => new NameExpr(parts[0], null, line, column),
                2 => new NameExpr(parts[0], parts[1], line, column),

                // Only a call can carry a full type id, since nothing reads an
                // output off one.
                _ => Refuse($"'{string.Join('.', parts)}' is not a name this can read."),
            };
        }

        at++;

        var arguments = new List<Argument>();

        if (!Take(TokenKind.CloseParen))
        {
            do
            {
                if (Argument() is not { } argument) return null;
                arguments.Add(argument);
            }
            while (Take(TokenKind.Comma));

            if (!Expect(TokenKind.CloseParen, "')' after the arguments")) return null;
        }

        string? block = null;

        if (Current.Kind == TokenKind.Block)
        {
            block = Current.Text;
            at++;
        }

        return new CallExpr(string.Join('.', parts), arguments, block, line, column);
    }

    private Expr? Refuse(string message)
    {
        Complain(message);
        return null;
    }

    private Argument? Argument()
    {
        var line = Current.Line;
        var column = Current.Column;

        string? name = null;

        if (Current.Kind == TokenKind.Identifier && Ahead().Kind == TokenKind.Colon)
        {
            name = Current.Text;
            at += 2;
        }

        return Pipeline() is { } value ? new Argument(name, value, line, column) : null;
    }
}
