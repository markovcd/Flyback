using Flyback.Core.Graph;

namespace Flyback.Engine.Language;

/// <summary>
/// What may be written where the cursor is in a patch's text: the sockets of the
/// call it is in, the outputs after a dot, the names in sight and the catalog.
/// </summary>
/// <remarks>
/// Read from the tokens before the cursor rather than the syntax tree, because
/// the text being typed is never finished and a half-written call does not parse.
/// </remarks>
internal static class Completions
{
    private static readonly string[] StatementWords =
        ["let", "def", "group", "off", "panel", "requires", "keyboard", "description", "author", "tags", "length"];

    public static CompletionList At(string source, int line, int column, ModuleCatalog modules)
    {
        var offset = Offset(source, line, column);
        var start = offset;

        while (start > 0 && (char.IsAsciiLetterOrDigit(source[start - 1]) || source[start - 1] == '_')) start--;

        var typed = source[start..offset];
        var list = new CompletionList(line, column - (offset - start), typed, []);

        if (typed is [var first, ..] && char.IsAsciiDigit(first)) return list;
        if (Quiet(source, start)) return list;

        var issues = new List<LanguageIssue>();
        var scanned = Lexer.Scan(source[..start], issues);

        if (issues.Any(i => i.Code == IssueCode.UnclosedBlock)) return list;

        var tokens = StatementTokens.ForParsing(scanned).Where(t => t.Kind != TokenKind.End).ToArray();
        var offered = new Offer(tokens, VisibleNames.Before(tokens), modules).Items();

        return list with
        {
            Items = [.. offered
                .Where(c => c.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(c => c.Label)],
        };
    }

    /// <summary>Where line and column fall in the text, clamped to it.</summary>
    private static int Offset(string source, int line, int column)
    {
        var at = 0;

        for (var l = 1; l < line; l++)
        {
            var next = source.IndexOf('\n', at);
            if (next < 0) return source.Length;
            at = next + 1;
        }

        var end = source.IndexOf('\n', at) is var newline and >= 0 ? newline : source.Length;

        return Math.Clamp(at + column - 1, at, end);
    }

    /// <summary>Whether <paramref name="at"/> is inside a comment or a text, where nothing is offered.</summary>
    private static bool Quiet(string source, int at)
    {
        var inText = false;

        for (var i = source.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1; i < at; i++)
        {
            var c = source[i];

            if (c is '"' or '“' or '”') inText = !inText;
            else if (c == '#' && !inText) return true;
        }

        return inText;
    }

    /// <summary>What one place offers, worked out from the tokens before it.</summary>
    private sealed class Offer(IReadOnlyList<Token> tokens, IReadOnlyList<VisibleName> names, ModuleCatalog modules)
    {
        private readonly ModuleNames written = ModuleNames.For(modules);

        public IEnumerable<Completion> Items()
        {
            if (tokens is not [.., var last]) return Statement();

            return last.Kind switch
            {
                TokenKind.NewLine or TokenKind.OpenBrace => Statement(),
                TokenKind.Dot => AfterDot(),
                TokenKind.Pipe => Stages(),
                TokenKind.Identifier => AfterWord(last.Text),
                TokenKind.OpenParen or TokenKind.Comma when Call() is { } call => Arguments(call).Concat(Values(true)),
                TokenKind.Number or TokenKind.Text or TokenKind.Block or TokenKind.CloseParen or TokenKind.CloseBrace => [],
                _ => Values(Call() is not null),
            };
        }

        private IEnumerable<Completion> Statement() =>
            StatementWords.Select(w => new Completion(w, CompletionKind.Keyword, "starts a statement", w + " "))
                .Concat(Values(false));

        /// <summary>After a word with no operator between: only what follows a keyword.</summary>
        private IEnumerable<Completion> AfterWord(string word) => word switch
        {
            "off" => Names().Where(n => n.Kind == CompletionKind.Name),
            "in" => Values(false),
            _ => [],
        };

        /// <summary>What a pipe can land on: a module, a def, the Output or <c>sum</c>.</summary>
        private IEnumerable<Completion> Stages() =>
            Names().Where(n => n.Kind == CompletionKind.Definition)
                .Append(new Completion("out", CompletionKind.Builtin, "the Output", "out"))
                .Append(Sum())
                .Concat(Modules());

        /// <summary>Anything that is worth a signal: the names in sight, the language's own, then the catalog.</summary>
        private IEnumerable<Completion> Values(bool inCall)
        {
            var builtins = new List<Completion> { new("t", CompletionKind.Builtin, "the clock, in seconds", "t") };

            if (modules.Get(NodeCatalog.CoordTypeId) is { } coordinates)
                builtins.AddRange(coordinates.Outputs.Select(o =>
                    new Completion(SocketNames.Written(o), CompletionKind.Builtin, Line(o.Help, "one of the picture's coordinates"), SocketNames.Written(o))));

            builtins.Add(Sum());
            builtins.Add(new Completion("for", CompletionKind.Keyword, "a loop: for name in list { ... }", "for "));

            if (inCall) builtins.Add(new Completion("_", CompletionKind.Builtin, "what the pipe brings in", "_"));

            return Names().Concat(builtins).Concat(Modules());
        }

        private static Completion Sum() => new("sum", CompletionKind.Builtin, "adds up a list: sum(voices)", "sum");

        private IEnumerable<Completion> Names() =>
            names
                .Reverse()
                .DistinctBy(n => n.Name)
                .Select(n => n.Kind switch
                {
                    CompletionKind.Definition => new Completion(n.Name, n.Kind, $"def {n.Name}({string.Join(", ", n.Parameters)})", n.Name),
                    CompletionKind.Parameter => new Completion(n.Name, n.Kind, "a parameter", n.Name),
                    _ => new Completion(n.Name, n.Kind, n.Callee is { } callee ? $"{callee}, bound on line {n.Line}" : $"bound on line {n.Line}", n.Name),
                })
                .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase);

        private IEnumerable<Completion> Modules() =>
            modules.All
                .Where(d => d.TypeId != NodeCatalog.OutputTypeId)
                .Select(d => (Def: d, Name: written.Written(d)))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => new Completion(m.Name, CompletionKind.Module, Line(m.Def.Description, m.Def.Name), m.Name));

        /// <summary>The sockets or parameters a call has not named yet, each written with its colon.</summary>
        private IEnumerable<Completion> Arguments((string Callee, int Open) call)
        {
            var named = Named(call.Open);

            // The pipe rule puts what is piped in on 'in', so the call has no need to name it.
            if (ChainBefore(call.Open, out var begins) is not null && begins > 0 && tokens[begins - 1].Kind == TokenKind.Pipe)
                named.Add("in");

            if (Def(call.Callee) is { } def)
            {
                return def.Parameters
                    .Where(p => !named.Contains(p))
                    .Select(p => new Completion(p, CompletionKind.Parameter, $"a parameter of {def.Name}", p + ": "));
            }

            return Module(call.Callee) is { } module
                ? module.Inputs
                    .Where(p => !named.Contains(SocketNames.Written(p)))
                    .Select(p => Socket(p, CompletionKind.Socket, SocketNames.Written(p) + ": "))
                : [];
        }

        /// <summary>After a dot: a module's outputs, the Output's sockets, a let's knobs, or the rest of a dotted type id.</summary>
        private IEnumerable<Completion> AfterDot()
        {
            var dot = tokens.Count - 1;

            if (dot >= 1 && tokens[dot - 1].Kind == TokenKind.CloseParen)
            {
                var open = Opening(dot - 1);
                return open >= 0 && CalleeBefore(open) is { } callee && Module(callee) is { } called ? Outputs(called) : [];
            }

            if (ChainBefore(dot, out var begins) is not { } path) return [];

            var startsStatement = begins == 0 || tokens[begins - 1].Kind is TokenKind.NewLine or TokenKind.OpenBrace;
            var items = new List<Completion>();

            if (path == "out" && modules.Get(NodeCatalog.OutputTypeId) is { } output)
                items.AddRange(output.Inputs.Select(p => Socket(p, CompletionKind.Socket, SocketNames.Written(p))));

            if (names.LastOrDefault(n => n.Name == path && n.Kind == CompletionKind.Name) is { Callee: { } bound } && Module(bound) is { } module)
            {
                items.AddRange(Outputs(module));

                if (startsStatement)
                    items.AddRange(module.Inputs.Select(p => Socket(p, CompletionKind.Socket, SocketNames.Written(p))));
            }

            var prefix = path + ".";

            items.AddRange(modules.All
                .Where(d => d.TypeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.TypeId, StringComparer.OrdinalIgnoreCase)
                .Select(d => new Completion(d.TypeId[prefix.Length..], CompletionKind.Module, Line(d.Description, d.Name), d.TypeId[prefix.Length..])));

            return items;
        }

        private static IEnumerable<Completion> Outputs(NodeDef module) =>
            module.Outputs.Select(p => Socket(p, CompletionKind.Output, SocketNames.Written(p)));

        private static Completion Socket(PortSpec port, CompletionKind kind, string insert) =>
            new(SocketNames.Written(port), kind, Line(port.Help, port.Name), insert);

        /// <summary>The call the cursor is inside the brackets of, by its callee and where its bracket opens.</summary>
        private (string Callee, int Open)? Call()
        {
            var depth = 0;

            for (var i = tokens.Count - 1; i >= 0; i--)
            {
                switch (tokens[i].Kind)
                {
                    case TokenKind.CloseParen:
                        depth++;
                        break;
                    case TokenKind.OpenParen when depth > 0:
                        depth--;
                        break;
                    case TokenKind.OpenParen:
                        return CalleeBefore(i) is { } callee ? (callee, i) : null;
                    case TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.NewLine when depth == 0:
                        return null;
                }
            }

            return null;
        }

        /// <summary>The socket or parameter names a call already gives, from its bracket to the cursor.</summary>
        private HashSet<string> Named(int open)
        {
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var depth = 0;

            for (var i = open; i < tokens.Count - 1; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen) depth++;
                else if (tokens[i].Kind == TokenKind.CloseParen) depth--;
                else if (depth == 1 && tokens[i].Kind == TokenKind.Identifier && tokens[i + 1].Kind == TokenKind.Colon)
                    named.Add(tokens[i].Text);
            }

            return named;
        }

        /// <summary>The index of the bracket that opens the one closing at <paramref name="close"/>, or -1.</summary>
        private int Opening(int close)
        {
            var depth = 0;

            for (var i = close; i >= 0; i--)
            {
                if (tokens[i].Kind == TokenKind.CloseParen) depth++;
                else if (tokens[i].Kind == TokenKind.OpenParen && --depth == 0) return i;
            }

            return -1;
        }

        /// <summary>The dotted name written straight before a call's bracket, where nothing is selected ahead of it.</summary>
        private string? CalleeBefore(int open) =>
            ChainBefore(open, out var begins) is { } chain && (begins == 0 || tokens[begins - 1].Kind != TokenKind.Dot) ? chain : null;

        /// <summary>The dotted name that ends just before <paramref name="end"/>, and the index it begins at.</summary>
        private string? ChainBefore(int end, out int begins)
        {
            begins = end;

            if (end < 1 || tokens[end - 1].Kind != TokenKind.Identifier) return null;

            var parts = new List<string> { tokens[end - 1].Text };
            begins = end - 1;

            while (begins >= 2 && tokens[begins - 1].Kind == TokenKind.Dot && tokens[begins - 2].Kind == TokenKind.Identifier)
            {
                parts.Insert(0, tokens[begins - 2].Text);
                begins -= 2;
            }

            return string.Join('.', parts);
        }

        private VisibleName? Def(string name) =>
            names.LastOrDefault(n => n.Name == name && n.Kind == CompletionKind.Definition);

        private NodeDef? Module(string name) =>
            Def(name) is null ? written.Find(name, out _, out _) : null;
    }

    /// <summary>The first sentence of <paramref name="text"/>, or <paramref name="otherwise"/> where it says nothing.</summary>
    private static string Line(string text, string otherwise)
    {
        if (string.IsNullOrWhiteSpace(text)) return otherwise;

        var stop = text.IndexOf(". ", StringComparison.Ordinal);

        return (stop >= 0 ? text[..(stop + 1)] : text).ReplaceLineEndings(" ").Trim();
    }
}
