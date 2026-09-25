using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// Tokens to a syntax tree, by recursive descent.
/// </summary>
/// <remarks>
/// Nothing here knows what a module is: the parser's job is shape, and every question
/// about whether a name exists or which socket a pipe lands on belongs to
/// <see cref="Binder"/> — which is what lets the catalog be the language without the
/// grammar depending on it. Recovery is by statement, so a file with three mistakes
/// says three things.
/// </remarks>
public sealed class Parser(IReadOnlyList<Token> tokens, List<LanguageIssue> issues)
{
    private int at;

    /// <summary>
    /// How deep brackets, minus signs and arithmetic may nest. The parser and the
    /// binder both walk an expression by recursion, and past a depth like this a
    /// pasted text would run the thread out of stack, which ends the program.
    /// </summary>
    public const int MaxDepth = 128;

    /// <summary>Where a ')' or '}' stands that no bracket before it is open for, counted over the whole text.</summary>
    private readonly HashSet<int> unmatched = Unmatched(tokens);

    /// <summary>How many pipelines the parser is inside now.</summary>
    private int depth;

    /// <summary>How tall each expression built so far stands, by the expression itself rather than what it equals.</summary>
    private readonly Dictionary<Expr, int> heights = new(ReferenceEqualityComparer.Instance);

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

            if (Statement() is { } statement)
            {
                statements.Add(statement);
                Finished();
            }

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

    /// <summary>
    /// Says so where a statement that read cleanly stopped short of the end of
    /// its line. What is left is about to be skipped, and text that is skipped
    /// without a word is a patch that builds and means something else.
    /// </summary>
    /// <param name="inBraces">Whether a closing brace may end the statement as well.</param>
    private void Finished(bool inBraces = false)
    {
        if (Current.Kind is TokenKind.NewLine or TokenKind.End) return;
        if (inBraces && Current.Kind == TokenKind.CloseBrace) return;
        if (Closer()) return;

        // A character the lexer already refused is what cut the statement short,
        // and that complaint is the one worth reading.
        if (issues.Any(issue => issue.Line == Current.Line)) return;

        Complain(IssueCode.UnreadTail, $"the statement ended before {Found()}, and nothing reads the rest of the line.");
    }

    /// <summary>What is at the current token, as a complaint says it.</summary>
    private string Found() => Current.Kind switch
    {
        TokenKind.End => "the end of the text",
        TokenKind.NewLine => "the end of the line",
        TokenKind.Text => $"\"{Current.Text}\"",
        TokenKind.Block => "'['",
        _ => $"'{Current.Text}'",
    };

    private static HashSet<int> Unmatched(IReadOnlyList<Token> tokens)
    {
        var found = new HashSet<int>();
        var parens = 0;
        var braces = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenParen: parens++; break;
                case TokenKind.OpenBrace: braces++; break;
                case TokenKind.CloseParen when parens == 0: found.Add(i); break;
                case TokenKind.CloseParen: parens--; break;
                case TokenKind.CloseBrace when braces == 0: found.Add(i); break;
                case TokenKind.CloseBrace: braces--; break;
            }
        }

        return found;
    }

    /// <summary>Says so where the current token closes a bracket nobody opened.</summary>
    private bool Closer()
    {
        if (!unmatched.Contains(at)) return false;

        var (opener, closer) = Current.Kind switch
        {
            TokenKind.CloseParen => ("(", ")"),
            TokenKind.CloseBrace => ("{", "}"),
            _ => (null, null),
        };

        if (opener is null) return false;

        Complain(IssueCode.UnmatchedCloser, $"'{closer}' closes nothing: no '{opener}' is open before it.");
        return true;
    }

    /// <summary>What an unclosed bracket is waiting for, with where it was opened.</summary>
    private static string Closing(string closer, string opener, Token open) =>
        $"'{closer}' to close the '{opener}' on line {open.Line}, column {open.Column}";

    /// <summary>Whether the current token could begin a value, which is what says a comma was left out before it.</summary>
    private bool AtValue() => Current.Kind
        is TokenKind.Number or TokenKind.Text or TokenKind.Identifier or TokenKind.OpenParen or TokenKind.Minus;

    private bool Take(TokenKind kind)
    {
        if (Current.Kind != kind) return false;
        at++;
        return true;
    }

    private bool Expect(TokenKind kind, string what)
    {
        if (Take(kind)) return true;

        Complain(IssueCode.Syntax, $"expected {what}, but found {Found()}.");
        return false;
    }

    private void Complain(string code, string message) =>
        issues.Add(new LanguageIssue(Current.Line, Current.Column, code, message));

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

        // Only with the layout after it, so a binding somebody called 'keyboard'
        // still starts a pipeline the way any other name does.
        if (AtWord("keyboard") && Ahead().Kind == TokenKind.Identifier) return Keyboard(line, column);

        // And a 'length' only with the time after it.
        if (AtWord("length") && Ahead().Kind == TokenKind.Number) return Length(line, column);

        // One string, or several running on, each line's a space apart from the last's.
        if (AtWord("description") && Ahead().Kind == TokenKind.Text)
            return new DescriptionStatement(string.Join(' ', Strings()), line, column);

        if (AtWord("author") && Ahead().Kind == TokenKind.Text)
            return new AuthorStatement(string.Join(' ', Strings()), line, column);

        if (AtWord("tags") && Ahead().Kind == TokenKind.Text)
        {
            var tags = Strings();

            if (Current.Kind != TokenKind.Comma) return new TagsStatement(tags, line, column);

            Complain(IssueCode.Syntax, "tags are written one after another without commas: tags \"drone\" \"slow\".");
            return null;
        }

        // The same rule again: what follows a module being switched off is the
        // name of one, and anything else here is a pipeline that begins with a
        // binding somebody happened to call 'off'.
        if (AtWord("off") && Ahead().Kind == TokenKind.Identifier) return Off(line, column);

        // And again: 'requires' names plugins only where one follows it.
        if (AtWord("requires") && Ahead().Kind is TokenKind.Identifier or TokenKind.Text) return Requires(line, column);

        // And again: 'panel' is a knob only when a name and an '=' follow it.
        if (AtWord("panel") && Ahead().Kind == TokenKind.Identifier && Ahead(2).Kind == TokenKind.Assign)
            return Panel(line, column);

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
                    Complain(IssueCode.Syntax, "expected a name inside the brackets.");
                    return null;
                }

                names.Add(Current.Text);
                at++;
            }
            while (Take(TokenKind.Comma) && Current.Kind != TokenKind.CloseParen);

            if (!Expect(TokenKind.CloseParen, "')' after the names")) return null;
            if (!Expect(TokenKind.Assign, "'=' after the names")) return null;
            if (Pipeline() is not { } tuple) return null;

            return new LetTupleStatement(names, tuple, line, column);
        }

        if (Current.Kind != TokenKind.Identifier)
        {
            Complain(IssueCode.Syntax, "expected a name after 'let'.");
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
            Complain(IssueCode.Syntax, "expected a name after 'def'.");
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
                    Complain(IssueCode.Syntax, "expected a parameter name.");
                    return null;
                }

                parameters.Add(Current.Text);
                at++;
            }
            while (Take(TokenKind.Comma) && Current.Kind != TokenKind.CloseParen);

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
        if (result is null && results is null) Complain(IssueCode.EmptyBody, "this body says nothing at the end of it.");

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

        if (AtWord("let") || AtWord("def") || AtWord("group") || AtWord("panel")) return false;

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

        // A group with no name goes straight to its brace.
        string? name = null;

        if (Current.Kind == TokenKind.Text)
        {
            name = Current.Text;
            at++;
        }
        else if (Current.Kind != TokenKind.OpenBrace)
        {
            Complain(IssueCode.Syntax, "expected a name in quotes, or '{', after 'group'.");
            return null;
        }

        if (!Expect(TokenKind.OpenBrace, "'{' after the name")) return null;

        var body = new List<Statement>();

        SkipBreaks();

        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.End))
        {
            var before = at;

            if (Statement() is { } statement)
            {
                body.Add(statement);
                Finished(inBraces: true);
            }

            if (at == before) at++;

            SkipToBreakOrBrace();
            SkipBreaks();
        }

        if (!Expect(TokenKind.CloseBrace, "'}' to close the group")) return null;

        return new GroupStatement(name, body, line, column);
    }

    /// <summary>The strings after the word that opens a statement, in order.</summary>
    private List<string> Strings()
    {
        var parts = new List<string>();

        for (at++; Current.Kind == TokenKind.Text; at++) parts.Add(Current.Text);

        return parts;
    }

    /// <summary>
    /// <c>length 2:30.50</c>, minutes and seconds as the status bar tells the time, or
    /// <c>length 90s</c>, a duration.
    /// </summary>
    private Statement? Length(int line, int column)
    {
        at++;

        var first = Current;
        at++;

        double? seconds;
        string said;

        if (first.Scaled == NumberStyle.Duration)
        {
            said = first.Text;
            seconds = Math.Round(Math.Pow(10, first.Value), 2);
            if (seconds is < PatchLength.Shortest or > PatchLength.Longest) seconds = null;
        }
        else if (Current.Kind == TokenKind.Colon && Ahead().Kind == TokenKind.Number && Ahead().Scaled == NumberStyle.Plain)
        {
            said = first.Text + ":" + Ahead().Text;
            seconds = PatchLength.Read(said);
            at += 2;
        }
        else
        {
            said = first.Text;
            seconds = PatchLength.Read(said);
        }

        if (seconds is { } kept) return new LengthStatement(kept, line, column);

        issues.Add(new LanguageIssue(first.Line, first.Column, IssueCode.BadLength,
            $"'{said}' is not a length. Write minutes and seconds, as in 'length 2:30.50', or a duration, as in 'length 90s', "
            + "from a tenth of a second to a day."));

        return null;
    }

    private Statement? Keyboard(int line, int column)
    {
        at++;

        var layout = Current.Text;
        at++;

        if (layout == "piano") return new KeyboardStatement(null, line, column);

        if (layout != "scale")
        {
            Complain(IssueCode.UnknownLayout, $"'{layout}' is not a layout. The keyboard is 'piano' or 'scale [ ... ]'.");
            return null;
        }

        if (Current.Kind != TokenKind.Block)
        {
            Complain(IssueCode.Syntax, "expected the notes of the scale in brackets after 'keyboard scale'.");
            return null;
        }

        var block = Current;
        at++;

        return new KeyboardStatement(block.Text, line, column, block.Line, block.Column);
    }

    /// <summary>
    /// <c>off name</c>. One name and no port: a socket cannot be switched off,
    /// only the module it is on.
    /// </summary>
    private Statement Off(int line, int column)
    {
        at++;

        var target = new NameExpr(Current.Text, null, Current.Line, Current.Column);
        at++;

        return new OffStatement(target, line, column);
    }

    /// <summary><c>requires</c> and the plugins after it, each a dotted name or, where it cannot be one, a string.</summary>
    private Statement? Requires(int line, int column)
    {
        at++;

        var plugins = new List<string>();

        do
        {
            if (Current.Kind == TokenKind.Text)
            {
                plugins.Add(Current.Text);
                at++;
                continue;
            }

            if (Current.Kind != TokenKind.Identifier)
            {
                Complain(IssueCode.Syntax, "expected the name of a plugin, such as 'flyback.picture'.");
                return null;
            }

            var name = Current.Text;
            at++;

            while (Current.Kind == TokenKind.Dot && Ahead().Kind == TokenKind.Identifier)
            {
                name += "." + Ahead().Text;
                at += 2;
            }

            if (Current.Kind == TokenKind.Range)
            {
                Complain(IssueCode.Syntax, "a plugin's name has one '.' between its parts, not '..'.");
                return null;
            }

            if (Take(TokenKind.Dot))
            {
                Complain(IssueCode.Syntax, $"expected the rest of the plugin's name after '{name}.', but found {Found()}.");
                return null;
            }

            plugins.Add(name);
        }
        while (Take(TokenKind.Comma));

        return new RequiresStatement(plugins, line, column);
    }

    /// <summary><c>panel name = value</c>, then its settings as named arguments.</summary>
    private Statement? Panel(int line, int column)
    {
        var name = Ahead().Text;
        at += 3;

        if (Pipeline() is not { } value) return null;

        var settings = new List<Argument>();

        while (Take(TokenKind.Comma))
        {
            if (Current.Kind != TokenKind.Identifier || Ahead().Kind != TokenKind.Colon)
            {
                Complain(IssueCode.Syntax, "expected a setting such as 'cc: 21' after the comma.");
                return null;
            }

            if (Argument() is not { } setting) return null;

            settings.Add(setting);
        }

        return new PanelStatement(name, value, settings, line, column);
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
        if (depth >= MaxDepth)
        {
            Complain(IssueCode.TooDeep, Deep);
            return null;
        }

        depth++;

        try
        {
            return Chain();
        }
        finally
        {
            depth--;
        }
    }

    /// <summary>Stages joined by pipes, the body of <see cref="Pipeline"/>.</summary>
    private Expr? Chain()
    {
        if (Sum() is not { } left) return null;

        var piped = false;

        while (Current.Kind == TokenKind.Pipe)
        {
            var line = Current.Line;
            var column = Current.Column;

            at++;

            if (Stage() is not { } stage) return null;

            if (Built(new PipeExpr(left, stage, line, column), left, stage) is not { } joined) return null;

            left = joined;
            piped = true;
        }

        // Arithmetic on what a pipe just produced, which is a thing people write
        // and this cannot read. The pipe is the loosest operator there is —
        // `t * 0.2 |> sine()` needs it to be — so `s |> fract * 2` would have to
        // mean piping into `fract * 2`, which is nothing. Said as the fix rather
        // than as the rule, because the rule is not what anybody wanted to know.
        if (piped && Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent
            or TokenKind.Plus or TokenKind.Minus)
        {
            Complain(IssueCode.ArithmeticAfterPipeline,
                $"'{Current.Text}' cannot follow a pipeline. Put the pipeline in brackets to do "
                + $"arithmetic on what it made: (a |> b) {Current.Text} 2.");

            return null;
        }

        return left;
    }

    /// <summary>What may sit after a pipe: a module to place, or a socket to land in.</summary>
    private Expr? Stage()
    {
        if (Current.Kind != TokenKind.Identifier)
        {
            Complain(IssueCode.Syntax, "expected a module or a socket after '|>'.");
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

            if (Built(new BinaryExpr(op, left, right, line, column), left, right) is not { } joined) return null;

            left = joined;
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

            if (Built(new BinaryExpr(op, left, right, line, column), left, right) is not { } joined) return null;

            left = joined;
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

        return Built(new RangeExpr(low, high, line, column), low, high);
    }

    /// <summary>A value with the minus signs before it, counted rather than recursed into.</summary>
    private Expr? Unary()
    {
        var signs = new List<Token>();

        while (Current.Kind == TokenKind.Minus)
        {
            signs.Add(Current);
            at++;
        }

        if (Primary() is not { } value) return null;

        for (var i = signs.Count - 1; i >= 0; i--)
        {
            if (Built(new NegateExpr(value, signs[i].Line, signs[i].Column), value) is not { } negated) return null;

            value = negated;
        }

        return value;
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
                var open = Current;
                at++;

                if (Pipeline() is not { } inner) return null;

                return Expect(TokenKind.CloseParen, Closing(")", "(", open)) ? Selected(inner) : null;
            }

            case TokenKind.Identifier:
                return NameOrCall(line, column);

            case TokenKind.Pipe:
                Complain(IssueCode.Syntax, "'|>' has nothing before it to pipe. Write what it carries first: sine() |> out.left.");
                return null;

            case TokenKind.OpenBrace:
                Complain(IssueCode.Syntax, "'{' opens nothing here: it follows 'group \"name\"' or a def's '='.");
                return null;

            default:
                if (!Closer()) Complain(IssueCode.Syntax, $"expected a value here, but found {Found()}.");
                return null;
        }
    }

    /// <summary>
    /// A dotted name, which is either a module to place or a binding to read —
    /// and the parser does not decide which. <c>space.rotate(...)</c> and
    /// <c>riff.gate</c> are the same shape until the catalog is consulted.
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

        // Not while a call is still to come: 'x(...).out' is the selector's to read.
        if (Current.Kind == TokenKind.Dot && Ahead().Kind != TokenKind.OpenParen)
        {
            at++;
            return Refuse(IssueCode.Syntax, $"expected the name of a socket or an output after '{string.Join('.', parts)}.', but found {Found()}.");
        }

        if (Current.Kind != TokenKind.OpenParen)
        {
            return parts.Count switch
            {
                1 => new NameExpr(parts[0], null, line, column),
                2 => new NameExpr(parts[0], parts[1], line, column),

                // Only a call can carry a full type id, since nothing reads an
                // output off one.
                _ => Refuse(IssueCode.Syntax, $"'{string.Join('.', parts)}' is not a name this can read."),
            };
        }

        var open = Current;
        at++;

        var arguments = new List<Argument>();

        if (!Take(TokenKind.CloseParen))
        {
            do
            {
                if (Argument() is not { } argument) return null;
                arguments.Add(argument);
            }
            while (Take(TokenKind.Comma) && Current.Kind != TokenKind.CloseParen);

            if (!Take(TokenKind.CloseParen))
            {
                // Two values side by side: a bare socket name wanted its colon, anything else a comma.
                if (AtValue() && arguments[^1] is { Name: null, Value: NameExpr { Port: null } bare })
                    Complain(IssueCode.Syntax, $"expected ':' after '{bare.Name}' to give it a value.");
                else if (AtValue())
                    Complain(IssueCode.Syntax, "expected ',' between the arguments.");
                else
                    Complain(IssueCode.Syntax, $"expected {Closing(")", "(", open)}, but found {Found()}.");

                return null;
            }
        }

        Token? block = null;

        if (Current.Kind == TokenKind.Block)
        {
            block = Current;
            at++;
        }

        var call = new CallExpr(
            string.Join('.', parts), arguments, block?.Text, line, column, block?.Line ?? 0, block?.Column ?? 0);

        return Built(call, [.. arguments.Select(argument => argument.Value)]) is { } built ? Selected(built) : null;
    }

    /// <summary>
    /// The outputs taken off what was just read, if any are:
    /// <c>tempo(bpm: 104).beats</c>.
    /// </summary>
    /// <remarks>
    /// Only a call and a bracketed pipeline come here. A binding's selector is
    /// read as part of its name, because <c>riff.gate</c> and <c>midi.in</c> are
    /// the same shape until the brackets after one of them say which it was.
    /// </remarks>
    private Expr? Selected(Expr source)
    {
        while (Take(TokenKind.Dot))
        {
            if (Current.Kind != TokenKind.Identifier)
                return Refuse(IssueCode.Syntax, "expected the name of an output after '.'.");

            if (Built(new SelectExpr(source, Current.Text, Current.Line, Current.Column), source) is not { } selected) return null;

            source = selected;
            at++;
        }

        return source;
    }

    private int Height(Expr expr) => heights.TryGetValue(expr, out var height) ? height : 1;

    /// <summary><paramref name="node"/>, standing on <paramref name="parts"/>, or null where that makes it too tall.</summary>
    private Expr? Built(Expr node, params Expr[] parts)
    {
        var height = 1 + parts.Select(Height).DefaultIfEmpty(0).Max();

        if (height > MaxDepth)
        {
            issues.Add(new LanguageIssue(node.Line, node.Column, IssueCode.TooDeep, Deep));
            return null;
        }

        heights[node] = height;
        return node;
    }

    private static string Deep => $"this is nested more than {MaxDepth} deep. Break it up with 'let'.";

    private Expr? Refuse(string code, string message)
    {
        Complain(code, message);
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
