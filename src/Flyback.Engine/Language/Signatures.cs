using Flyback.Core.Graph;

namespace Flyback.Engine.Language;

/// <summary>The sockets of the call the caret is in, and which of them the caret is filling.</summary>
internal static class Signatures
{
    public static Signature? At(string source, int line, int column, ModuleCatalog modules)
    {
        if (Caret.Before(source, Caret.Offset(source, line, column), modules) is not { } caret) return null;
        if (caret.Call() is not var (callee, open)) return null;

        SignatureSocket[] sockets;
        string title;
        var def = caret.Def(callee);

        if (def is not null)
        {
            sockets = [.. def.Parameters.Select(p => new SignatureSocket(p, string.Empty, string.Empty))];
            title = $"def {def.Name}";
        }
        else if (caret.Module(callee) is { } module)
        {
            sockets = [.. module.Inputs.Select(p => new SignatureSocket(SocketNames.Written(p), Caret.Reads(p, input: true), p.Help))];
            title = $"{module.Name} ({module.TypeId})";
        }
        else
        {
            return null;
        }

        var arguments = caret.Arguments(open);
        var named = arguments.OfType<string>().Select(n => Index(sockets, n)).Where(i => i >= 0).ToHashSet();
        var piped = caret.Piped(open) ? Piped(caret, open, sockets, named, def is not null) : -1;

        var active = arguments[^1] is { } giving ? Index(sockets, giving) : Positional(sockets.Length, arguments, named, piped);

        return new Signature(callee, title, sockets, active, piped);
    }

    /// <summary>The socket a pipe into the call lands on: the one given <c>_</c>, else <c>in</c>, else a def's first parameter nothing names.</summary>
    private static int Piped(Caret caret, int open, SignatureSocket[] sockets, HashSet<int> named, bool def)
    {
        var tokens = caret.Tokens;
        var depth = 0;

        for (var i = open; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.OpenParen) depth++;
            else if (tokens[i].Kind == TokenKind.CloseParen) depth--;
            else if (depth == 1 && tokens[i].Kind == TokenKind.Identifier && tokens[i + 1].Kind == TokenKind.Colon
                && tokens[i + 2] is { Kind: TokenKind.Identifier, Text: "_" })
                return Index(sockets, tokens[i].Text);
        }

        if (Index(sockets, "in") is var into and >= 0) return into;

        return def ? Enumerable.Range(0, sockets.Length).FirstOrDefault(i => !named.Contains(i), -1) : -1;
    }

    /// <summary>Where an argument with no name goes: the next socket nothing names and the pipe does not fill.</summary>
    private static int Positional(int count, IReadOnlyList<string?> arguments, HashSet<int> named, int piped)
    {
        var free = Enumerable.Range(0, count).Where(i => !named.Contains(i) && i != piped).ToArray();
        var before = arguments.Take(arguments.Count - 1).Count(a => a is null);

        return before < free.Length ? free[before] : -1;
    }

    private static int Index(SignatureSocket[] sockets, string name) =>
        Array.FindIndex(sockets, s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
