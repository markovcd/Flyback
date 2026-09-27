using Flyback.Core.Graph;
using Flyback.Core.Language.Ast;
using Flyback.Core.Language.Ast.Expressions;
using Flyback.Core.Language.Ast.Statements;

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
public sealed class Parser
{
    private readonly IReadOnlyList<Token> tokens;
    private readonly List<LanguageIssue> issues;
    /// <summary>Tokens for closing brackets that have no matching opener.</summary>
    private readonly HashSet<int> unmatched;
    private readonly ExpressionParser expressions;
    private int at;

    /// <summary>
    /// How deep brackets, minus signs and arithmetic may nest. The parser and the
    /// binder both walk an expression by recursion, and past a depth like this a
    /// pasted text would run the thread out of stack, which ends the program.
    /// </summary>
    public const int MaxDepth = 128;

    internal Token Current => tokens[Math.Min(at, tokens.Count - 1)];

    internal Token Ahead(int by = 1) => tokens[Math.Min(at + by, tokens.Count - 1)];

    public Parser(IReadOnlyList<Token> tokens, List<LanguageIssue> issues)
    {
        this.tokens = tokens;
        this.issues = issues;
        unmatched = Unmatched(tokens);
        expressions = new ExpressionParser(this, issues);
    }

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
    internal string Found() => Current.Kind switch
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
    internal bool Closer()
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

    internal void Advance(int by = 1) => at += by;

    internal bool Take(TokenKind kind)
    {
        if (Current.Kind != kind) return false;
        at++;
        return true;
    }

    internal bool Expect(TokenKind kind, string what)
    {
        if (Take(kind)) return true;

        Complain(IssueCode.Syntax, $"expected {what}, but found {Found()}.");
        return false;
    }

    internal void Complain(string code, string message) =>
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

            if (expressions.Pipeline() is not { } value) return null;

            return backWire
                ? new BackWireStatement(target, value, line, column)
                : new KnobStatement(target, value, line, column);
        }

        return expressions.Pipeline() is { } pipeline ? new PipelineStatement(pipeline, line, column) : null;
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
            if (expressions.Pipeline() is not { } tuple) return null;

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
        if (expressions.Pipeline() is not { } value) return null;

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
            return expressions.Pipeline() is { } single
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

            if (expressions.Pipeline() is { } first)
            {
                items.Add(first);

                while (Take(TokenKind.Comma))
                {
                    if (expressions.Pipeline() is not { } next) { at = mark; return false; }
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

        one = expressions.Pipeline();
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

        if (expressions.Pipeline() is not { } value) return null;

        var settings = new List<Argument>();

        while (Take(TokenKind.Comma))
        {
            if (Current.Kind != TokenKind.Identifier || Ahead().Kind != TokenKind.Colon)
            {
                Complain(IssueCode.Syntax, "expected a setting such as 'cc: 21' after the comma.");
                return null;
            }

            if (expressions.Argument() is not { } setting) return null;

            settings.Add(setting);
        }

        return new PanelStatement(name, value, settings, line, column);
    }

    private void SkipToBreakOrBrace()
    {
        while (Current.Kind is not (TokenKind.NewLine or TokenKind.CloseBrace or TokenKind.End)) at++;
    }
}
