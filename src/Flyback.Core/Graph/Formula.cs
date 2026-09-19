using System.Globalization;
using System.Text;
using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// An Expression's formula, read: a tree of the Maths modules it names, over the
/// four sockets and numbers.
/// </summary>
/// <remarks>
/// Every operator and every function is a Maths module, lowered by that module's
/// own emit function, so a formula is exactly the modules it spells — the same
/// ops in the order a patch of them would have emitted them (ADR-0095). What
/// makes it one block rather than a dozen is only that the wiring is written
/// rather than drawn.
/// <para>
/// A hand-written recursive-descent reader, for the reason the text language has
/// one (ADR-0065, ADR-0019), and a separate one because that language lives in
/// the Engine and a module has to be lowered by Core. The grammar is the
/// arithmetic half of it: <c>+ - * / %</c>, unary minus, parentheses, numbers,
/// <c>pi</c> and <c>tau</c>, the sockets <c>a b c d</c>, and a call for every
/// Maths module that is one op or a handful, named by its type id without
/// <c>math.</c>.
/// </para>
/// </remarks>
internal sealed class Formula
{
    /// <summary>The socket names, in socket order.</summary>
    public const string Sockets = "abcd";

    private readonly Term root;

    private Formula(Term root) => this.root = root;

    /// <summary>
    /// Reads <paramref name="text"/>, or says what stops it and where, counting
    /// characters from one.
    /// </summary>
    public static Formula? Read(string text, IReadOnlyDictionary<string, NodeDef> functions, out string? problem)
    {
        var reader = new Reader(text, functions);

        try
        {
            var root = reader.Whole();
            problem = null;
            return new Formula(root);
        }
        catch (FormatException unread)
        {
            problem = unread.Message;
            return null;
        }
    }

    /// <summary>
    /// The ops this formula stands for, asking <paramref name="socket"/> for
    /// <c>a</c> to <c>d</c> as it reaches each. A part written twice is lowered
    /// once, as it would be were it one module wired to two places.
    /// </summary>
    public Slot Lower(Emitter em, Func<int, Slot> socket)
    {
        var lowered = new Dictionary<string, Slot>();

        return Lower(root);

        Slot Lower(Term term)
        {
            var key = term.ToString();
            if (lowered.TryGetValue(key, out var done)) return done;

            var slot = term switch
            {
                Literal literal => em.Constant(literal.Value),
                Socket read => socket(read.Index),
                Call call => Emit(call),
                _ => throw new InvalidOperationException($"No lowering for {term.GetType().Name}."),
            };

            lowered[key] = slot;
            return slot;
        }

        // An argument left off rests on the socket's default, which is what an
        // unwired socket on the module itself would have read.
        Slot Emit(Call call)
        {
            var inputs = new Slot[call.Module.Inputs.Count];

            for (var port = 0; port < inputs.Length; port++)
            {
                var spec = call.Module.Inputs[port];
                var slot = port < call.Arguments.Count ? Lower(call.Arguments[port]) : em.Constant(spec.Default);

                inputs[port] = spec.Kind == PortKind.Any ? slot : em.Coerce(slot, spec.Width);
            }

            return call.Module.Emit(em, new EmitContext(inputs))[0];
        }
    }

    // --- spelling it as infix -----------------------------------------------------

    /// <summary>The Maths modules the text language writes as an operator.</summary>
    private static readonly Dictionary<string, char> Operators = new()
    {
        ["math.add"] = '+',
        ["math.sub"] = '-',
        ["math.mul"] = '*',
        ["math.div"] = '/',
        ["math.mod"] = '%',
    };

    /// <summary>
    /// <paramref name="text"/> written as the text language's arithmetic, or null
    /// where that would not read back as this formula.
    /// </summary>
    /// <remarks>
    /// Only operators, numbers and sockets have a spelling there: a call is a
    /// module of its own in the language and <c>pi</c> is a word it does not
    /// know. Two numbers either side of an operator have none either, because the
    /// language would fold them into one — and the formula only leaves them
    /// unfolded where folding would not be what it computes, as with <c>%</c>.
    /// </remarks>
    /// <param name="number">How a number is written.</param>
    /// <param name="socket">What stands for a socket, which is read as a single value.</param>
    /// <param name="reads">
    /// Filled with each socket as the text reads it, and whether it stands after
    /// the operator that joins the whole — which is where the language places the
    /// module.
    /// </param>
    public static string? Infix(
        string text,
        IReadOnlyDictionary<string, NodeDef> functions,
        Func<float, string> number,
        Func<int, string> socket,
        List<(int Socket, bool After)> reads)
    {
        if (Read(text, functions, out _) is not { root: var root }) return null;

        return root is Call { Arguments.Count: 2 } whole && Operators.ContainsKey(whole.Module.TypeId)
            ? Spell(whole, after: null)
            : Spell(root, after: true);

        // 'after' is null only for the operator that joins the whole: what is on
        // its left stands before it and what is on its right after.
        string? Spell(Term term, bool? after)
        {
            switch (term)
            {
                case Literal literal:
                    return number(literal.Value);

                case Socket read:
                    reads.Add((read.Index, after ?? false));
                    return socket(read.Index);

                case Call { Module.TypeId: "math.neg", Arguments: [var operand] } when operand is not Literal:
                {
                    if (Spell(operand, after ?? true) is not { } inner) return null;

                    return Strength(operand) < Strength(term) ? $"-({inner})" : $"-{inner}";
                }

                case Call { Arguments: [var left, var right] } call
                    when Operators.TryGetValue(call.Module.TypeId, out var sign)
                        && !(left is Literal && right is Literal):
                {
                    if (Spell(left, after ?? false) is not { } l) return null;
                    if (Spell(right, after ?? true) is not { } r) return null;

                    var strength = Strength(call);

                    if (Strength(left) < strength) l = $"({l})";
                    if (Strength(right) <= strength) r = $"({r})";

                    return $"{l} {sign} {r}";
                }

                default:
                    return null;
            }
        }
    }

    /// <summary>How tightly a part holds together as the language reads it: a sum least, a value most.</summary>
    private static int Strength(Term term) => term switch
    {
        Call { Module.TypeId: "math.neg" } => 3,
        Call { Module.TypeId: "math.add" or "math.sub" } => 1,
        Call => 2,
        Literal { Value: < 0 } => 3,
        _ => 4,
    };

    // --- the tree ------------------------------------------------------------------

    /// <summary>
    /// One node of the tree. Written back out in a canonical form, which is what
    /// finds a part written twice.
    /// </summary>
    private abstract record Term;

    private sealed record Literal(float Value) : Term
    {
        public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture);
    }

    private sealed record Socket(int Index) : Term
    {
        public override string ToString() => Sockets[Index].ToString();
    }

    private sealed record Call(NodeDef Module, IReadOnlyList<Term> Arguments) : Term
    {
        public override string ToString() => $"{Module.TypeId}({string.Join(", ", Arguments)})";
    }

    // --- reading -------------------------------------------------------------------

    private sealed class Reader(string text, IReadOnlyDictionary<string, NodeDef> functions)
    {
        private int at;

        public Term Whole()
        {
            Skip();
            if (at >= text.Length) throw Fault("there is nothing in it");

            var whole = Sum();

            Skip();
            if (at < text.Length) throw Fault($"'{text[at]}' is not expected here");

            return whole;
        }

        private Term Sum()
        {
            var left = Product();

            while (Take('+', '-') is { } sign)
                left = Operator(sign == '+' ? "add" : "sub", left, Product());

            return left;
        }

        private Term Product()
        {
            var left = Unary();

            while (Take('*', '/', '%') is { } sign)
                left = Operator(sign switch { '*' => "mul", '/' => "div", _ => "mod" }, left, Unary());

            return left;
        }

        private Term Unary()
        {
            if (Take('-') is null) return Primary();

            // A minus on a number is a number, as it would be on a knob.
            var operand = Unary();
            return operand is Literal literal ? new Literal(-literal.Value) : new Call(functions["neg"], [operand]);
        }

        private Term Primary()
        {
            Skip();
            if (at >= text.Length) throw Fault("it ends where a value was expected");

            var next = text[at];

            if (next == '(')
            {
                at++;
                var inner = Sum();
                Expect(')');
                return inner;
            }

            if (char.IsDigit(next) || next == '.') return Number();

            if (char.IsLetter(next)) return Name();

            throw Fault($"'{next}' is not expected here");
        }

        private Literal Number()
        {
            var start = at;

            while (at < text.Length && (char.IsDigit(text[at]) || text[at] == '.')) at++;

            if (at < text.Length && text[at] is 'e' or 'E')
            {
                var mark = at++;
                if (at < text.Length && text[at] is '+' or '-') at++;

                if (at < text.Length && char.IsDigit(text[at]))
                    while (at < text.Length && char.IsDigit(text[at])) at++;
                else
                    at = mark;
            }

            var written = text[start..at];

            if (!float.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !float.IsFinite(value))
            {
                at = start;
                throw Fault($"'{written}' is not a number");
            }

            return new Literal(value);
        }

        private Term Name()
        {
            var start = at;

            while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;

            var name = text[start..at].ToLowerInvariant();

            Skip();
            if (at < text.Length && text[at] == '(')
            {
                if (!functions.TryGetValue(name, out var module))
                {
                    at = start;
                    throw Fault($"there is no function '{name}'");
                }

                at++;
                return new Call(module, Arguments(module, name, start));
            }

            if (name.Length == 1 && Sockets.IndexOf(name[0]) is >= 0 and var socket) return new Socket(socket);

            if (name == "pi") return new Literal(MathF.PI);
            if (name == "tau") return new Literal(MathF.Tau);

            at = start;
            throw Fault(functions.ContainsKey(name)
                ? $"'{name}' is a function, and wants its arguments in brackets"
                : $"'{name}' is not a socket — the sockets are a, b, c and d");
        }

        private List<Term> Arguments(NodeDef module, string name, int start)
        {
            var arguments = new List<Term>();

            Skip();
            if (Take(')') is not null) throw Fault($"'{name}' wants at least one argument", start);

            do arguments.Add(Sum());
            while (Take(',') is not null);

            Expect(')');

            if (arguments.Count > module.Inputs.Count)
            {
                throw Fault(
                    $"'{name}' takes {module.Inputs.Count} — "
                    + string.Join(", ", module.Inputs.Select(p => p.Name)),
                    start);
            }

            return arguments;
        }

        /// <summary>
        /// Folds two numbers into one, on floats, for the reason a knob holds a
        /// float: <c>1 / 45</c> should be the number a knob set to it holds, not
        /// a Divide.
        /// </summary>
        private Term Operator(string name, Term left, Term right)
        {
            if (left is Literal l && right is Literal r)
            {
                float? folded = name switch
                {
                    "add" => l.Value + r.Value,
                    "sub" => l.Value - r.Value,
                    "mul" => l.Value * r.Value,
                    "div" => r.Value == 0f ? 0f : l.Value / r.Value,
                    _ => null,
                };

                if (folded is { } value && float.IsFinite(value)) return new Literal(value);
            }

            return new Call(functions[name], [left, right]);
        }

        private char? Take(params char[] wanted)
        {
            Skip();
            if (at >= text.Length || Array.IndexOf(wanted, text[at]) < 0) return null;

            return text[at++];
        }

        private void Expect(char wanted)
        {
            if (Take(wanted) is not null) return;

            Skip();
            throw Fault(at >= text.Length ? $"it ends where '{wanted}' was expected" : $"'{wanted}' was expected here");
        }

        private void Skip()
        {
            while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        }

        private FormatException Fault(string reason, int? where = null) =>
            new($"{reason}, at character {(where ?? at) + 1}");
    }

    /// <summary>
    /// The functions a formula may call, keyed by the name it calls them by: every
    /// Maths module that lowers to ops alone, which is all of them but those with
    /// a row of channels.
    /// </summary>
    public static IReadOnlyDictionary<string, NodeDef> Functions(IEnumerable<NodeDef> maths)
    {
        var named = new Dictionary<string, NodeDef>();

        foreach (var def in maths)
            named[def.TypeId["math.".Length..]] = def;

        return named;
    }

    /// <summary>The functions as a person reads them, for the module's description.</summary>
    public static string Listed(IReadOnlyDictionary<string, NodeDef> functions)
    {
        var listed = new StringBuilder();

        foreach (var (name, def) in functions)
        {
            if (name is "add" or "sub" or "mul" or "div" or "neg") continue;

            if (listed.Length > 0) listed.Append(", ");
            listed.Append(name).Append('(').Append(string.Join(", ", def.Inputs.Select(p => p.Name))).Append(')');
        }

        return listed.ToString();
    }
}
