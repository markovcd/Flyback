using System.Globalization;
using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// What a step block expands to: a flat list of <see cref="Step"/>, and nothing else.
/// </summary>
/// <param name="Steps">The tune, already flattened.</param>
/// <param name="RateDivisor">
/// What the sequencer's rate must be divided by for the pattern to take the same time
/// it would have. Only <c>&lt;a b&gt;</c> moves it: alternation is unrolled into a
/// longer list, which has to be read more slowly to sound the same.
/// </param>
public readonly record struct StepBlock(IReadOnlyList<Step> Steps, int RateDivisor);

/// <summary>
/// The step notation, borrowed from TidalCycles and expanded here into the list a
/// sequencer already carries.
/// </summary>
/// <remarks>
/// Every form is rewriting and none of it reaches the engine: no module is added, no
/// opcode invented, and <c>EmitSequence</c> is handed exactly the kind of list a
/// hand-built preset hands it.
/// <para>
/// Two forms change how the sequencer compiles rather than only what it plays:
/// <c>@n</c> and <c>[a b]</c> make the steps uneven, which takes the module off the
/// cheap path, where <c>!n</c>, <c>&lt;a b&gt;</c> and the Euclidean form leave the
/// lengths alone.
/// </para>
/// </remarks>
public static class StepNotation
{
    /// <summary>
    /// The block <paramref name="source"/> spells, read as notes when
    /// <paramref name="notes"/> and as plain numbers otherwise — which is the
    /// only difference between the two sequencers.
    /// </summary>
    /// <param name="line">The line the block's '[' is on.</param>
    /// <param name="column">The column the block's '[' is in.</param>
    public static StepBlock Read(string source, bool notes, int line, int column, List<LanguageIssue> issues)
    {
        var reader = new Reader(source, notes, line, column, issues);
        var terms = reader.Terms(until: '\0', opened: -1);

        // How many passes the whole block takes, which is the slowest
        // alternation in it. A <a b> beside a <c d e> needs six passes before
        // both are back where they started, and anything less would cut one of
        // them short.
        var passes = terms.Aggregate(1, (all, term) => Lcm(all, term.Passes));

        // Counted before anything is spelled out, since what is spelled out is the list itself.
        var count = 0L;

        for (var pass = 0; pass < passes && count <= NodeCatalog.MaxSteps; pass++)
            foreach (var term in terms)
                count = Math.Min(count + term.Leaves(pass), long.MaxValue / 2);

        if (count > NodeCatalog.MaxSteps)
        {
            issues.Add(new LanguageIssue(line, column, IssueCode.TooManySteps, reader.Capped || count >= long.MaxValue / 2
                ? $"this block spells more steps than a sequence holds, which is {NodeCatalog.MaxSteps}."
                : $"this block spells {count} steps, and a sequence holds at most {NodeCatalog.MaxSteps}."));

            return new StepBlock([], 1);
        }

        var steps = new List<Step>();

        for (var pass = 0; pass < passes; pass++)
            foreach (var term in terms)
                term.Write(steps, pass, 1d);

        return new StepBlock(steps, passes);
    }

    /// <summary>The pitch classes a scale block names, by letter or by number.</summary>
    /// <param name="line">The line the block's '[' is on.</param>
    /// <param name="column">The column the block's '[' is in.</param>
    internal static List<int> Classes(string block, int line, int column, List<LanguageIssue> issues)
    {
        var classes = new List<int>();

        for (var at = 0; at < block.Length;)
        {
            if (block[at] is ' ' or '\t' or '\r' or '\n' or ',')
            {
                at++;
                continue;
            }

            var start = at;
            while (at < block.Length && block[at] is not (' ' or '\t' or '\r' or '\n' or ',')) at++;

            var word = block[start..at];

            // A class is a note with no octave, so it is read as one in the
            // octave that starts at zero and then reduced.
            if (Lexer.Note(word + "0") is { } note)
            {
                classes.Add(((int)note % Pitch.Classes + Pitch.Classes) % Pitch.Classes);
                continue;
            }

            if (int.TryParse(word, out var number) && number is >= 0 and < Pitch.Classes)
            {
                classes.Add(number);
                continue;
            }

            var (wordLine, wordColumn) = Where(block, start, line, column);

            issues.Add(new LanguageIssue(wordLine, wordColumn, IssueCode.UnknownNote, $"'{word}' is not a note of the octave."));
        }

        return classes;
    }

    /// <summary>Where a character of a block stands in the file, counted from the block's '['.</summary>
    private static (int Line, int Column) Where(string block, int index, int line, int column)
    {
        var at = (Line: line, Column: column + 1);

        for (var i = 0; i < index && i < block.Length; i++)
            at = block[i] == '\n' ? (at.Line + 1, 1) : (at.Line, at.Column + 1);

        return at;
    }

    private static int Lcm(int a, int b) => a / Gcd(a, b) * b;

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    /// <summary>
    /// One thing written in a block, which may stand for several steps.
    /// </summary>
    /// <param name="Choices">
    /// What this is on each pass. One entry for everything but
    /// <c>&lt;a b&gt;</c>, which is the only form that reads differently the
    /// second time round.
    /// </param>
    /// <param name="Length">How much of the bar this takes, before subdivision.</param>
    private sealed record Term(IReadOnlyList<Term.Choice> Choices, double Length)
    {
        /// <summary>A value and a volume, or a subdivided run of them.</summary>
        internal sealed record Choice(float Value, float Volume, IReadOnlyList<Term>? Inner);

        public int Passes => Math.Max(1, Choices.Count);

        /// <summary>How many steps this spells out on <paramref name="pass"/>, without spelling them.</summary>
        public long Leaves(int pass)
        {
            var choice = Choices[pass % Choices.Count];

            if (choice.Inner is not { Count: > 0 } inner) return 1;

            var total = 0L;

            foreach (var term in inner) total = Math.Min(total + term.Leaves(pass), long.MaxValue / 2);

            return total;
        }

        /// <summary>
        /// Writes what this is worth on <paramref name="pass"/> into
        /// <paramref name="steps"/>, scaled by how much of a step it occupies.
        /// </summary>
        public void Write(List<Step> steps, int pass, double scale)
        {
            var choice = Choices[pass % Choices.Count];
            var span = Length * scale;

            if (choice.Inner is not { Count: > 0 } inner)
            {
                steps.Add(new Step(choice.Value, (float)span, choice.Volume).Sane());
                return;
            }

            // A subdivision shares out the space its parent had, in the
            // proportions its own terms asked for.
            var total = inner.Sum(t => t.Length);
            if (total <= 0d) return;

            foreach (var term in inner) term.Write(steps, pass, span / total);
        }
    }

    /// <summary>
    /// A character-at-a-time reader over one block. Small enough to be a nested
    /// type: nothing outside wants it, and it is meaningless away from the
    /// notation it reads.
    /// </summary>
    private sealed class Reader(string source, bool notes, int line, int column, List<LanguageIssue> issues)
    {
        private int at;

        /// <summary>How many brackets the reader is inside now.</summary>
        private int depth;

        /// <summary>Set once the block was too deep to read, after which nothing more is said about it.</summary>
        private bool gaveUp;

        /// <summary>Set where a repeat or a Euclidean pattern asked for more steps than were built to count.</summary>
        public bool Capped { get; private set; }

        private char Current => at < source.Length ? source[at] : '\0';

        private void Say(string code, int index, string message)
        {
            var (atLine, atColumn) = Where(source, index, line, column);

            issues.Add(new LanguageIssue(atLine, atColumn, code, message));
        }

        /// <summary>Every term up to <paramref name="until"/>, which is the closing bracket or the end.</summary>
        /// <param name="opened">Where the bracket <paramref name="until"/> closes was opened; none for the block itself.</param>
        public List<Term> Terms(char until, int opened)
        {
            // Read by recursion, so a pasted block bracketed thousands deep would take the thread's stack with it.
            if (depth >= Parser.MaxDepth)
            {
                Say(IssueCode.TooDeep, at, $"this block is nested more than {Parser.MaxDepth} deep.");
                at = source.Length;
                gaveUp = true;
                return [];
            }

            depth++;

            try
            {
                return Within(until, opened);
            }
            finally
            {
                depth--;
            }
        }

        /// <summary>The body of <see cref="Terms"/>.</summary>
        private List<Term> Within(char until, int opened)
        {
            var terms = new List<Term>();

            while (true)
            {
                while (char.IsWhiteSpace(Current) || Current == ',') at++;

                if (Current == '\0' || Current == until) break;

                if (Read() is { } term) terms.Add(term);
                else break;
            }

            if (until != '\0')
            {
                if (Current == until) at++;
                else if (!gaveUp) Say(IssueCode.StepSyntax, opened, $"this '{source[opened]}' is never closed with '{until}'.");
            }

            return terms;
        }

        private Term? Read()
        {
            var choices = new List<Term.Choice>();

            if (Current == '<')
            {
                // Alternation: one choice per pass, and each may itself be a
                // group or a rest.
                var opened = at++;

                foreach (var inner in Terms('>', opened))
                    choices.AddRange(inner.Choices);

                if (choices.Count == 0) choices.Add(new Term.Choice(0f, 0f, null));
            }
            else if (Current == '[')
            {
                var opened = at++;
                choices.Add(new Term.Choice(0f, 1f, Terms(']', opened)));
            }
            else if (Current == '~' || Current == '_')
            {
                at++;
                choices.Add(new Term.Choice(0f, 0f, null));
            }
            else if (Value(out var said) is { } value)
            {
                choices.Add(new Term.Choice(value, 1f, null));
            }
            else if (said)
            {
                return null;
            }
            else
            {
                Say(IssueCode.StepSyntax, at, $"'{Current}' means nothing in a step block.");
                at++;
                return null;
            }

            return Suffixes(new Term(choices, 1d));
        }

        /// <summary>The modifiers that may follow a term, in any order.</summary>
        private Term Suffixes(Term term)
        {
            while (true)
            {
                switch (Current)
                {
                    case '%':
                    {
                        at++;
                        var volume = (float)(Wanted('%', "a volume, such as %0.5") ?? 1d);
                        term = term with
                        {
                            Choices = [.. term.Choices.Select(c => c with { Volume = volume })],
                        };
                        continue;
                    }

                    case '@':
                    {
                        var sign = at++;
                        var length = Wanted('@', "a length, such as @2") ?? 1d;

                        if (length <= 0d)
                        {
                            Say(IssueCode.StepSyntax, sign, "'@' takes a length above nought.");
                            length = 1d;
                        }

                        term = term with { Length = length };
                        continue;
                    }

                    case '!':
                    {
                        var sign = at++;
                        var count = Wanted('!', "a count, such as !3") ?? 1d;

                        if (count < 1d)
                        {
                            Say(IssueCode.StepSyntax, sign, "'!' repeats a step once or more.");
                            count = 1d;
                        }

                        // Past what a sequence holds the block is refused whole, so
                        // nothing larger than that is ever built to find out.
                        var times = (int)Math.Min(count, NodeCatalog.MaxSteps + 1);
                        Capped |= count > times;

                        // Repetition is the one form that makes several terms
                        // out of one, so it is folded into a subdivision of the
                        // same total length — which keeps every step the same
                        // size and the module on its fast path.
                        if (times > 1)
                        {
                            var copies = Enumerable.Repeat(term with { Length = 1d }, times).ToList();
                            term = new Term([new Term.Choice(0f, 1f, copies)], term.Length * times);
                        }

                        continue;
                    }

                    case '(':
                    {
                        at++;
                        term = Euclid(term);
                        continue;
                    }

                    default:
                        return term;
                }
            }
        }

        /// <summary>
        /// <c>a(3,8)</c> — three sounding steps spread as evenly as eight will
        /// allow, which is Bjorklund's pattern and the reason a Euclidean rhythm
        /// sounds like one.
        /// </summary>
        private Term Euclid(Term term)
        {
            var opened = at - 1;
            var sounding = (int)Math.Min(Figure() ?? 0d, NodeCatalog.MaxSteps + 1);

            while (char.IsWhiteSpace(Current) || Current == ',') at++;

            var asked = Figure() ?? 0d;
            var over = (int)Math.Min(asked, NodeCatalog.MaxSteps + 1);
            Capped |= asked > over;

            while (char.IsWhiteSpace(Current)) at++;

            if (Current == ')') at++;
            else
            {
                Say(IssueCode.StepSyntax, opened, "this '(' is never closed: a Euclidean pattern is written C4(3,8).");
                return term;
            }

            if (over <= 0)
            {
                Say(IssueCode.EuclidNeedsLength, opened, "a Euclidean pattern needs a length.");
                return term;
            }

            var quiet = term.Choices[0] with { Volume = 0f };
            var copies = new List<Term>(over);

            for (var i = 0; i < over; i++)
            {
                // The classic test: a step sounds where the running count of
                // sounding steps ticks over, which spreads them without ever
                // needing the list built first.
                var sounds = sounding > 0 && i * sounding % over < sounding;

                copies.Add(new Term([sounds ? term.Choices[0] : quiet], 1d));
            }

            return new Term([new Term.Choice(0f, 1f, copies)], term.Length * over);
        }

        /// <summary>A step's value: a note where the sequencer takes notes, a number where it does not.</summary>
        /// <param name="said">Whether what stood there was refused, and said so.</param>
        private float? Value(out bool said)
        {
            said = false;

            if (notes && char.IsAsciiLetter(Current))
            {
                var start = at;
                while (char.IsAsciiLetterOrDigit(Current) || Current == '#') at++;

                if (Current == '-' && at + 1 < source.Length && char.IsAsciiDigit(source[at + 1]))
                {
                    at++;
                    while (char.IsAsciiDigit(Current)) at++;
                }

                var word = source[start..at];

                if (Lexer.Note(word) is { } note) return (float)note;

                Say(IssueCode.UnknownNote, start, $"'{word}' is not a note.");
                said = true;
                return null;
            }

            return (float?)Figure();
        }

        /// <summary>The number after <paramref name="sign"/>, said as missing where there is none.</summary>
        private double? Wanted(char sign, string example)
        {
            if (Figure() is { } figure) return figure;

            Say(IssueCode.StepSyntax, at - 1, $"expected {example}, after '{sign}'.");
            return null;
        }

        /// <summary>A bare number, wherever the notation wants one.</summary>
        private double? Figure()
        {
            var start = at;

            if (Current == '-') at++;

            while (char.IsAsciiDigit(Current)) at++;

            if (Current == '.' && at + 1 < source.Length && char.IsAsciiDigit(source[at + 1]))
            {
                at++;
                while (char.IsAsciiDigit(Current)) at++;
            }

            if (at == start) return null;

            return double.TryParse(source[start..at], CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }
    }
}
