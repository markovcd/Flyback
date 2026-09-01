using System.Globalization;
using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// What a step block expands to: a flat list of <see cref="Step"/>, and nothing
/// else.
/// </summary>
/// <param name="Steps">The tune, already flattened.</param>
/// <param name="RateDivisor">
/// What the sequencer's rate must be divided by for the pattern to take the
/// same time it would have. Only <c>&lt;a b&gt;</c> moves it: alternation is
/// unrolled into a longer list, so the list has to be read more slowly to sound
/// the same.
/// </param>
public readonly record struct StepBlock(IReadOnlyList<Step> Steps, int RateDivisor);

/// <summary>
/// The step notation, borrowed from TidalCycles and expanded here into the list
/// a sequencer already carries.
/// </summary>
/// <remarks>
/// Every form is rewriting and none of it reaches the engine: no module is
/// added, no opcode invented, and <c>EmitSequence</c> is handed exactly the kind
/// of list a hand-built preset hands it. That is the whole case for having it —
/// a notation that cost the compiler something would be a much harder argument.
/// <para>
/// Two of the forms change how the sequencer compiles rather than only what it
/// plays. <c>@n</c> and <c>[a b]</c> make the steps uneven, which takes the
/// module off the cheap path <c>EmitSequence</c> takes when every step is the
/// same length; <c>!n</c>, <c>&lt;a b&gt;</c> and the Euclidean form all leave
/// the lengths alone. The reference says so, and this is where it is true.
/// </para>
/// </remarks>
public static class StepNotation
{
    /// <summary>
    /// The block <paramref name="source"/> spells, read as notes when
    /// <paramref name="notes"/> and as plain numbers otherwise — which is the
    /// only difference between the two sequencers.
    /// </summary>
    public static StepBlock Read(string source, bool notes, int line, List<LanguageIssue> issues)
    {
        var reader = new Reader(source, notes, line, issues);
        var terms = reader.Terms(until: '\0');

        // How many passes the whole block takes, which is the slowest
        // alternation in it. A <a b> beside a <c d e> needs six passes before
        // both are back where they started, and anything less would cut one of
        // them short.
        var passes = terms.Aggregate(1, (all, term) => Lcm(all, term.Passes));
        var steps = new List<Step>();

        for (var pass = 0; pass < passes; pass++)
            foreach (var term in terms)
                term.Write(steps, pass, 1d);

        return new StepBlock(steps, passes);
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
    private sealed class Reader(string source, bool notes, int line, List<LanguageIssue> issues)
    {
        private int at;

        private char Current => at < source.Length ? source[at] : '\0';

        /// <summary>Every term up to <paramref name="until"/>, which is the closing bracket or the end.</summary>
        public List<Term> Terms(char until)
        {
            var terms = new List<Term>();

            while (true)
            {
                while (char.IsWhiteSpace(Current) || Current == ',') at++;

                if (Current == '\0' || Current == until) break;

                if (Read() is { } term) terms.Add(term);
                else break;
            }

            if (until != '\0' && Current == until) at++;

            return terms;
        }

        private Term? Read()
        {
            var choices = new List<Term.Choice>();

            if (Current == '<')
            {
                // Alternation: one choice per pass, and each may itself be a
                // group or a rest.
                at++;

                foreach (var inner in Terms('>'))
                    choices.AddRange(inner.Choices);

                if (choices.Count == 0) choices.Add(new Term.Choice(0f, 0f, null));
            }
            else if (Current == '[')
            {
                at++;
                choices.Add(new Term.Choice(0f, 1f, Terms(']')));
            }
            else if (Current == '~' || Current == '_')
            {
                at++;
                choices.Add(new Term.Choice(0f, 0f, null));
            }
            else if (Value() is { } value)
            {
                choices.Add(new Term.Choice(value, 1f, null));
            }
            else
            {
                issues.Add(new LanguageIssue(line, at + 1, $"'{Current}' means nothing in a step block."));
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
                        var volume = (float)(Figure() ?? 1d);
                        term = term with
                        {
                            Choices = [.. term.Choices.Select(c => c with { Volume = volume })],
                        };
                        continue;
                    }

                    case '@':
                    {
                        at++;
                        term = term with { Length = Figure() ?? 1d };
                        continue;
                    }

                    case '!':
                    {
                        at++;
                        var times = (int)(Figure() ?? 1d);

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
            var sounding = (int)(Figure() ?? 0d);

            while (char.IsWhiteSpace(Current) || Current == ',') at++;

            var over = (int)(Figure() ?? 0d);

            while (char.IsWhiteSpace(Current)) at++;
            if (Current == ')') at++;

            if (over <= 0)
            {
                issues.Add(new LanguageIssue(line, at + 1, "a Euclidean pattern needs a length."));
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
        private float? Value()
        {
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

                issues.Add(new LanguageIssue(line, start + 1, $"'{word}' is not a note."));
                return null;
            }

            return (float?)Figure();
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
