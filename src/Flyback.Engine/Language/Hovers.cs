using Flyback.Core.Graph;

namespace Flyback.Engine.Language;

/// <summary>What the word at a place in a patch's text is: a module, a socket, a name the text bound or one the language gives.</summary>
internal static class Hovers
{
    public static Hover? At(string source, int line, int column, ModuleCatalog modules)
    {
        var offset = Caret.Offset(source, line, column);
        var start = offset;
        var end = offset;

        while (start > 0 && Caret.IsWordCharacter(source[start - 1])) start--;
        while (end < source.Length && Caret.IsWordCharacter(source[end])) end++;

        if (start == end || char.IsAsciiDigit(source[start])) return null;
        if (Caret.Before(source, start, modules) is not { } caret) return null;

        var word = source[start..end];

        if (caret.Tokens is [.., { Kind: TokenKind.Dot }]) return AfterDot(caret, word);

        if (Next(source, end) == ':' && caret.Call() is var (callee, _))
            return Argument(caret, callee, word);

        // The name a let or a def is binding, as it reads once its line is written.
        if (caret.Tokens is [.., { Kind: TokenKind.Identifier, Text: "let" or "def" }]
            && Caret.Before(source, LineEnd(source, end), modules) is { } bound
            && bound.Names.LastOrDefault(n => n.Name == word) is { } declared)
            return Named(bound, declared);

        if (caret.Names.LastOrDefault(n => n.Name == word) is { } name) return Named(caret, name);

        return Builtin(caret, word) ?? Module(caret, word + Following(source, end));
    }

    private static Hover? AfterDot(Caret caret, string word)
    {
        var selected = caret.Selected(out var path, out _);

        if (selected is not null)
        {
            var outputs = selected.TypeId != NodeCatalog.OutputTypeId;

            if (outputs && Find(selected.Outputs, word) is { } output) return Socket(selected, output, input: false);
            if (Find(selected.Inputs, word) is { } input) return Socket(selected, input, input: true);
        }

        return path is not null && caret.Modules.Get($"{path}.{word}") is { } module ? About(caret, module) : null;
    }

    /// <summary>A name written before a colon in a call: a socket of the module or a parameter of the def.</summary>
    private static Hover? Argument(Caret caret, string callee, string word)
    {
        if (caret.Def(callee) is { } def)
            return def.Parameters.Contains(word) ? new Hover($"{word} · parameter of def {def.Name}", string.Empty) : null;

        return caret.Module(callee) is { } module && Find(module.Inputs, word) is { } port ? Socket(module, port, input: true) : null;
    }

    private static Hover Named(Caret caret, VisibleName name) => name.Kind switch
    {
        CompletionKind.Definition => new Hover($"def {name.Name}({string.Join(", ", name.Parameters)})", $"Written on line {name.Line}."),
        CompletionKind.Parameter => new Hover($"{name.Name} · parameter", string.Empty),
        _ when name.Callee is { } callee && caret.Module(callee) is { } module =>
            new Hover($"{name.Name} · {module.Name}, bound on line {name.Line}", module.Description),
        _ => new Hover($"{name.Name} · bound on line {name.Line}", string.Empty),
    };

    private static Hover? Builtin(Caret caret, string word)
    {
        if (word == "t") return new Hover("t · the clock", "Seconds since the patch started playing.");
        if (word == "sum") return new Hover("sum · adds up a list", "sum(voices), or voices |> sum(), is the items added up as a + b + c would be.");
        if (word == "out" && caret.Modules.Get(NodeCatalog.OutputTypeId) is { } output) return About(caret, output);
        if (word == "_") return new Hover("_ · what the pipe brings in", "Written as one named argument, it puts the pipe on that socket.");

        return caret.Modules.Get(NodeCatalog.CoordTypeId) is { } coordinates && Find(coordinates.Outputs, word) is { } coordinate
            ? new Hover($"{word} · one of the picture's coordinates", coordinate.Help)
            : null;
    }

    /// <summary>A module by the dotted name around the word, longest first, so <c>color</c> in <c>color.mix</c> is the Blend.</summary>
    private static Hover? Module(Caret caret, string dotted)
    {
        for (var name = dotted; name.Length > 0; name = name.LastIndexOf('.') is var dot and > 0 ? name[..dot] : string.Empty)
            if (caret.Module(name) is { } module) return About(caret, module);

        return null;
    }

    private static Hover About(Caret caret, NodeDef module)
    {
        var written = module.TypeId == NodeCatalog.OutputTypeId ? "out" : caret.Written.Written(module);
        var inputs = string.Join(", ", module.Inputs.Select(SocketNames.Written));
        var outputs = string.Join(", ", module.Outputs.Select(SocketNames.Written));
        var sockets = $"Inputs: {(inputs.Length > 0 ? inputs : "none")}. Outputs: {(outputs.Length > 0 ? outputs : "none")}.";

        return new Hover(
            $"{written} · {module.Name} ({module.TypeId})",
            string.IsNullOrWhiteSpace(module.Description) ? sockets : $"{module.Description}\n\n{sockets}");
    }

    private static Hover Socket(NodeDef module, PortSpec port, bool input)
    {
        var reads = Caret.Reads(port, input);
        var title = $"{SocketNames.Written(port)} · {(input ? "input" : "output")} of {module.Name}";

        return new Hover(reads.Length > 0 ? $"{title} · {reads}" : title, port.Help);
    }

    private static PortSpec? Find(IReadOnlyList<PortSpec> ports, string name) =>
        SocketNames.Find(ports, name) is var at and >= 0 ? ports[at] : null;

    /// <summary>The first character after <paramref name="at"/> that is not a space.</summary>
    private static char Next(string source, int at)
    {
        while (at < source.Length && source[at] is ' ' or '\t') at++;

        return at < source.Length ? source[at] : '\0';
    }

    private static int LineEnd(string source, int at) =>
        source.IndexOf('\n', at) is var end and >= 0 ? end : source.Length;

    /// <summary>The <c>.word</c> parts written straight after <paramref name="at"/>.</summary>
    private static string Following(string source, int at)
    {
        var end = at;

        while (end + 1 < source.Length && source[end] == '.' && Caret.IsWordCharacter(source[end + 1]))
        {
            end++;
            while (end < source.Length && Caret.IsWordCharacter(source[end])) end++;
        }

        return source[at..end];
    }
}
