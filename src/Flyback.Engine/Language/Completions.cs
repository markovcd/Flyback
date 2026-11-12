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
        var offset = Caret.Offset(source, line, column);
        var start = offset;

        while (start > 0 && Caret.IsWordCharacter(source[start - 1])) start--;

        var typed = source[start..offset];
        var list = new CompletionList(line, column - (offset - start), typed, []);

        if (typed is [var first, ..] && char.IsAsciiDigit(first)) return list;
        if (Caret.Before(source, start, modules) is not { } caret) return list;

        return list with
        {
            Items = [.. new Offer(caret).Items()
                .Where(c => c.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(c => c.Label)],
        };
    }

    /// <summary>What one place offers, worked out from the tokens before it.</summary>
    private sealed class Offer(Caret caret)
    {

        public IEnumerable<Completion> Items()
        {
            if (caret.Tokens is not [.., var last]) return Statement();

            return last.Kind switch
            {
                TokenKind.NewLine or TokenKind.OpenBrace => Statement(),
                TokenKind.Dot => AfterDot(),
                TokenKind.Pipe => Stages(),
                TokenKind.Identifier => AfterWord(last.Text),
                TokenKind.OpenParen or TokenKind.Comma when caret.Call() is { } call => Arguments(call).Concat(Values(true)),
                TokenKind.Number or TokenKind.Text or TokenKind.Block or TokenKind.CloseParen or TokenKind.CloseBrace => [],
                _ => Values(caret.Call() is not null),
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

            if (caret.Modules.Get(SourceModules.CoordTypeId) is { } coordinates)
                builtins.AddRange(coordinates.Outputs.Select(o =>
                    new Completion(SocketNames.Written(o), CompletionKind.Builtin, Caret.FirstSentence(o.Help, "one of the picture's coordinates"), SocketNames.Written(o))));

            builtins.Add(Sum());
            builtins.Add(new Completion("for", CompletionKind.Keyword, "a loop: for name in list { ... }", "for "));

            if (inCall) builtins.Add(new Completion("_", CompletionKind.Builtin, "what the pipe brings in", "_"));

            return Names().Concat(builtins).Concat(Modules());
        }

        private static Completion Sum() => new("sum", CompletionKind.Builtin, "adds up a list: sum(voices)", "sum");

        private IEnumerable<Completion> Names() =>
            caret.Names
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
            caret.Modules.All
                .Where(d => d.TypeId != OutputModule.TypeId)
                .Select(d => (Def: d, Name: caret.Written.Written(d)))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => new Completion(m.Name, CompletionKind.Module, Caret.FirstSentence(m.Def.Description, m.Def.Name), m.Name));

        /// <summary>The sockets or parameters a call has not named yet, each written with its colon.</summary>
        private IEnumerable<Completion> Arguments((string Callee, int Open) call)
        {
            var named = caret.Named(call.Open);

            // The pipe rule puts what is piped in on 'in', so the call has no need to name it.
            if (caret.Piped(call.Open)) named.Add("in");

            if (caret.Def(call.Callee) is { } def)
            {
                return def.Parameters
                    .Where(p => !named.Contains(p))
                    .Select(p => new Completion(p, CompletionKind.Parameter, $"a parameter of {def.Name}", p + ": "));
            }

            return caret.Module(call.Callee) is { } module
                ? module.Inputs
                    .Where(p => !named.Contains(SocketNames.Written(p)))
                    .Select(p => Socket(p, CompletionKind.Socket, SocketNames.Written(p) + ": "))
                : [];
        }

        /// <summary>After a dot: a module's outputs, the Output's sockets, a let's knobs, or the rest of a dotted type id.</summary>
        private IEnumerable<Completion> AfterDot()
        {
            var items = new List<Completion>();
            var selected = caret.Selected(out var path, out var startsStatement);

            if (selected is { TypeId: OutputModule.TypeId })
                items.AddRange(selected.Inputs.Select(p => Socket(p, CompletionKind.Socket, SocketNames.Written(p))));
            else if (selected is not null)
                items.AddRange(Outputs(selected));

            if (selected is not null && path is not null && path != "out" && startsStatement)
                items.AddRange(selected.Inputs.Select(p => Socket(p, CompletionKind.Socket, SocketNames.Written(p))));

            if (path is null) return items;

            var prefix = path + ".";

            items.AddRange(caret.Modules.All
                .Where(d => d.TypeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.TypeId, StringComparer.OrdinalIgnoreCase)
                .Select(d => new Completion(d.TypeId[prefix.Length..], CompletionKind.Module, Caret.FirstSentence(d.Description, d.Name), d.TypeId[prefix.Length..])));

            return items;
        }

        private static IEnumerable<Completion> Outputs(NodeDef module) =>
            module.Outputs.Select(p => Socket(p, CompletionKind.Output, SocketNames.Written(p)));

        private static Completion Socket(PortSpec port, CompletionKind kind, string insert) =>
            new(SocketNames.Written(port), kind, Caret.FirstSentence(port.Help, port.Name), insert);
    }
}
