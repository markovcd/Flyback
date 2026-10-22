using System.Text;

namespace Flyback.Plugins.Speech;

/// <summary>
/// English text turned into the sounds that say it: a short list of words that
/// break every rule, and letter rules for the rest.
/// </summary>
/// <remarks>
/// English spelling is too irregular for rules to get every word right, so
/// anything in square brackets is read as ARPAbet instead: <c>[hh ax l ow]</c>.
/// </remarks>
internal static class Pronunciation
{
    private const float Comma = 180f;
    private const float Stop = 380f;

    private static readonly Dictionary<string, string> Words = new()
    {
        ["a"] = "ax",
        ["an"] = "ae n",
        ["the"] = "dh ax",
        ["of"] = "ah v",
        ["to"] = "t uw",
        ["and"] = "ae n d",
        ["in"] = "ih n",
        ["is"] = "ih z",
        ["it"] = "ih t",
        ["i"] = "ay",
        ["you"] = "y uw",
        ["your"] = "y ao r",
        ["are"] = "aa r",
        ["was"] = "w ah z",
        ["were"] = "w er",
        ["be"] = "b iy",
        ["he"] = "hh iy",
        ["she"] = "sh iy",
        ["we"] = "w iy",
        ["me"] = "m iy",
        ["my"] = "m ay",
        ["by"] = "b ay",
        ["do"] = "d uw",
        ["does"] = "d ah z",
        ["done"] = "d ah n",
        ["go"] = "g ow",
        ["no"] = "n ow",
        ["so"] = "s ow",
        ["yes"] = "y eh s",
        ["oh"] = "ow",
        ["have"] = "hh ae v",
        ["give"] = "g ih v",
        ["live"] = "l ih v",
        ["love"] = "l ah v",
        ["come"] = "k ah m",
        ["some"] = "s ah m",
        ["one"] = "w ah n",
        ["two"] = "t uw",
        ["three"] = "th r iy",
        ["four"] = "f ao r",
        ["five"] = "f ay v",
        ["six"] = "s ih k s",
        ["seven"] = "s eh v ax n",
        ["eight"] = "ey t",
        ["nine"] = "n ay n",
        ["ten"] = "t eh n",
        ["zero"] = "z iy r ow",
        ["this"] = "dh ih s",
        ["that"] = "dh ae t",
        ["they"] = "dh ey",
        ["them"] = "dh eh m",
        ["then"] = "dh eh n",
        ["than"] = "dh ae n",
        ["there"] = "dh eh r",
        ["their"] = "dh eh r",
        ["these"] = "dh iy z",
        ["those"] = "dh ow z",
        ["with"] = "w ih dh",
        ["what"] = "w ah t",
        ["who"] = "hh uw",
        ["where"] = "w eh r",
        ["why"] = "w ay",
        ["how"] = "hh aw",
        ["would"] = "w uh d",
        ["could"] = "k uh d",
        ["should"] = "sh uh d",
        ["said"] = "s eh d",
        ["says"] = "s eh z",
        ["again"] = "ax g eh n",
        ["any"] = "eh n iy",
        ["many"] = "m eh n iy",
        ["people"] = "p iy p ax l",
        ["eye"] = "ay",
        ["only"] = "ow n l iy",
        ["hello"] = "hh ax l ow",
        ["world"] = "w er l d",
        ["flyback"] = "f l ay b ae k",
        ["music"] = "m y uw z ih k",
        ["machine"] = "m ax sh iy n",
        ["computer"] = "k ax m p y uw t er",
        ["robot"] = "r ow b aa t",
        ["human"] = "hh y uw m ax n",
        ["voice"] = "v oy s",
        ["picture"] = "p ih k ch er",
        ["sound"] = "s aw n d",
        ["synthesizer"] = "s ih n th ax s ay z er",
        ["speech"] = "s p iy ch",
        ["talk"] = "t ao k",
        ["walk"] = "w ao k",
        ["good"] = "g uh d",
        ["look"] = "l uh k",
        ["book"] = "b uh k",
        ["put"] = "p uh t",
        ["from"] = "f r ah m",
        ["for"] = "f ao r",
        ["or"] = "ao r",
        ["not"] = "n aa t",
        ["all"] = "ao l",
        ["can"] = "k ae n",
        ["name"] = "n ey m",
        ["very"] = "v eh r iy",
        ["every"] = "eh v r iy",
        ["water"] = "w ao t er",
        ["want"] = "w aa n t",
        ["know"] = "n ow",
        ["now"] = "n aw",
        ["new"] = "n uw",
        ["okay"] = "ow k ey",
        ["ok"] = "ow k ey",
        ["welcome"] = "w eh l k ax m",
        ["listen"] = "l ih s ax n",
        ["daisy"] = "d ey z iy",
        ["answer"] = "ae n s er",
        ["true"] = "t r uw",
        ["half"] = "hh ae f",
        ["crazy"] = "k r ey z iy",
        ["over"] = "ow v er",
        ["here"] = "hh iy r",
        ["weather"] = "w eh dh er",
        ["door"] = "d ao r",
        ["open"] = "ow p ax n",
        ["leader"] = "l iy d er",
        ["our"] = "aw er",
        ["hour"] = "aw er",
        ["friend"] = "f r eh n d",
        ["theres"] = "dh eh r z",
        ["im"] = "ay m",
        ["bicycle"] = "b ay s ih k ax l",
    };

    /// <summary>Words said quickly and without stress.</summary>
    private static readonly HashSet<string> Light =
    [
        "a", "an", "the", "of", "to", "and", "in", "is", "it", "are", "was", "were", "be",
        "for", "or", "at", "on", "as", "by", "but", "from", "with", "that", "than", "your",
        "our", "can", "do", "does", "has", "had", "have", "will", "would", "could", "should",
    ];

    /// <summary>
    /// The sounds that say <paramref name="text"/>, each knowing where in its line it
    /// was written, and the bracketed words that named a sound there is not.
    /// </summary>
    public static (IReadOnlyList<Spoken> Sounds, IReadOnlyList<string> Unknown) Of(string text)
    {
        var sounds = new List<Spoken>();
        var unknown = new List<string>();
        var line = 0;
        var lineStart = 0;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            var start = i;

            if (c == '[')
            {
                var close = text.IndexOf(']', i + 1);
                if (close < 0) close = text.Length;

                var symbols = text[(i + 1)..close].ToLowerInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                i = Math.Min(close + 1, text.Length);
                Say(symbols, light: false);
            }
            else if (char.IsLetter(c) || c == '\'')
            {
                while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '\'')) i++;

                var word = text[start..i].ToLowerInvariant();
                Say(Spell(word).Split(' ', StringSplitOptions.RemoveEmptyEntries), Light.Contains(word));
            }
            else if (char.IsDigit(c))
            {
                i++;
                Say(Words[Digit(c)].Split(' '), light: false);
            }
            else
            {
                i++;

                switch (c)
                {
                    case ',' or ';' or ':' or '-':
                        Pause(Comma, question: false);
                        break;
                    case '.' or '!':
                        Pause(Stop, question: false);
                        break;
                    case '?':
                        Pause(Stop, question: true);
                        break;
                    case '\n':
                        Pause(Stop, question: false);
                        line++;
                        lineStart = i;
                        break;
                }
            }

            void Say(IReadOnlyList<string> symbols, bool light)
            {
                var stressed = !light;
                var known = symbols.Where(s => Phonemes.Find(s) is not null).ToList();
                unknown.AddRange(symbols.Where(s => Phonemes.Find(s) is null));

                for (var k = 0; k < known.Count; k++)
                {
                    var phoneme = Phonemes.Find(known[k])!;

                    // A word leans on its first full vowel; a schwa never takes it.
                    var stress = stressed && phoneme.Syllabic && phoneme.Symbol != "ax";
                    if (stress) stressed = false;

                    // The caption reaches across the word as its sounds are said.
                    var shown = start - lineStart + (i - start) * (k + 1) / known.Count;
                    sounds.Add(new Spoken(phoneme, stress) { Line = line, Shown = shown });
                }
            }
        }

        // Each sound learns how long its line is, so the caption can be a fraction of it,
        // and the line's last word shows the stop after it.
        var lengths = text.Split('\n').Select(l => Math.Max(1, l.TrimEnd().Length)).ToArray();
        var ended = -1;
        for (var k = sounds.Count - 1; k >= 0; k--)
        {
            var length = lengths[Math.Min(sounds[k].Line, lengths.Length - 1)];
            var last = ended != sounds[k].Line;
            if (sounds[k].Phoneme is not null) ended = sounds[k].Line;
            sounds[k] = sounds[k] with { LineLength = length, Shown = last ? length : sounds[k].Shown };
        }

        return (sounds, unknown);

        void Pause(float milliseconds, bool question)
        {
            if (sounds.Count == 0) return;

            var last = sounds[^1];

            if (last.Phoneme is null)
            {
                sounds[^1] = last with { Pause = Math.Max(last.Pause, milliseconds), Question = last.Question || question };
                return;
            }

            sounds.Add(new Spoken(null, Pause: milliseconds, Question: question) { Line = last.Line, Shown = last.Shown });
        }
    }

    private static string Digit(char c) =>
        new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" }[c - '0'];

    /// <summary>A word's sounds by its letters, as ARPAbet separated by spaces.</summary>
    internal static string Spell(string word)
    {
        word = word.Replace("'", "");
        if (word.Length == 0) return "";
        if (Words.TryGetValue(word, out var known)) return known;

        // Endings that change how the rest is read, taken off first.
        if (word.Length > 4 && word.EndsWith("ing")) return $"{Spell(word[..^3])} ih ng";
        if (word.Length > 3 && word.EndsWith("ed") && !IsVowel(word[^3]))
        {
            var stem = Spell(word[..^2]);
            var last = stem.Split(' ')[^1];
            var ending = last is "t" or "d" ? "ih d" : last is "p" or "k" or "s" or "sh" or "ch" or "f" or "th" ? "t" : "d";
            return $"{stem} {ending}";
        }

        if (word.Length > 3 && word[^1] == 's' && word[^2] != 's' && word[^2] != 'u' && word[^2] != 'i')
        {
            var stem = Spell(word[..^1]);
            var last = stem.Split(' ')[^1];
            var ending = last is "s" or "z" or "sh" or "zh" or "ch" or "jh" ? "ih z"
                : last is "p" or "t" or "k" or "f" or "th" ? "s" : "z";
            return $"{stem} {ending}";
        }

        return Letters(word);
    }

    private static string Letters(string word)
    {
        var phones = new StringBuilder();
        var magic = Magic(word);
        var n = word.Length;
        var i = 0;

        while (i < n)
        {
            var (sounds, used) = Rule(word, i, magic);
            if (sounds.Length > 0) phones.Append(sounds).Append(' ');
            i += used;
        }

        return phones.ToString().Trim();
    }

    /// <summary>Where a silent 'e' makes the vowel two letters before it long, as in "made" and "time".</summary>
    private static int Magic(string word)
    {
        var n = word.Length;
        if (n < 4 || word[^1] != 'e') return -1;
        if (IsVowel(word[^2]) || word[^2] is 'r' or 'w') return -1;
        if (!IsVowel(word[^3]) || (n > 3 && IsVowel(word[^4]))) return -1;

        return n - 3;
    }

    private static (string Sounds, int Used) Rule(string w, int i, int magic)
    {
        var n = w.Length;
        var rest = w[i..];
        var atEnd = i == n - 1;
        var next = i + 1 < n ? w[i + 1] : '\0';
        var prev = i > 0 ? w[i - 1] : '\0';

        // A final 'e' is silent once the word has another vowel.
        if (atEnd && w[i] == 'e' && n > 2) return ("", 1);
        if (i == n - 2 && rest == "le" && !IsVowel(prev)) return ("ax l", 2);

        foreach (var (pattern, sounds) in Patterns)
        {
            if (!rest.StartsWith(pattern)) continue;

            var after = i + pattern.Length < n ? w[i + pattern.Length] : '\0';
            var final = i + pattern.Length == n;

            switch (pattern)
            {
                case "kn" or "wr" or "gn" when i != 0: continue;
                case "gh" when i == 0: return ("g", 2);
                case "mb" when !final: continue;
                case "ow" or "ie" or "ey" when final:
                    return (pattern switch { "ow" => "ow", "ie" => "ay", _ => "iy" }, 2);
                case "er" or "ir" or "ur" or "ar" or "or" when IsVowel(after) && after != 'e':
                    continue;
            }

            return (sounds, pattern.Length);
        }

        var c = w[i];
        var soft = next is 'e' or 'i' or 'y';

        if (IsVowel(c) && c != 'y')
        {
            if (i == magic) return (Long(c), 1);
            if (atEnd) return (c switch { 'a' => "ax", 'o' => "ow", 'i' => "iy", 'u' => "uw", _ => "iy" }, 1);

            // A vowel at the end of a syllable before one consonant and a vowel is
            // usually long: "paper", "open", "music".
            var open = i + 2 < n && !IsVowel(next) && next != 'r' && IsVowel(w[i + 2]) && w[i + 2] != 'e' && i == FirstVowel(w);
            if (open) return (Long(c), 1);

            // A vowel after the first is mostly unstressed, and an unstressed one is a schwa.
            return (i != FirstVowel(w) && c is not 'i' ? "ax" : Short(c), 1);
        }

        return (c switch
        {
            'b' => "b",
            'c' => soft ? "s" : "k",
            'd' => "d",
            'f' => "f",
            'g' => soft && i > 0 ? "jh" : "g",
            'h' => "hh",
            'j' => "jh",
            'k' => "k",
            'l' => "l",
            'm' => "m",
            'n' => "n",
            'p' => "p",
            'q' => "k",
            'r' => "r",
            's' => i > 0 && IsVowel(prev) && IsVowel(next) && next != 'y' ? "z" : "s",
            't' => "t",
            'v' => "v",
            'w' => "w",
            'x' => i == 0 ? "z" : "k s",
            'y' => i == 0 ? "y" : atEnd ? (FirstVowel(w) == i ? "ay" : "iy") : "ih",
            'z' => "z",
            _ => "",
        }, 1);
    }

    private static readonly (string Pattern, string Sounds)[] Patterns =
    [
        ("tion", "sh ax n"), ("sion", "zh ax n"), ("ture", "ch er"), ("ough", "ao"), ("augh", "ao"),
        ("eigh", "ey"), ("igh", "ay"), ("tch", "ch"), ("dge", "jh"), ("sch", "s k"), ("all", "ao l"),
        ("alk", "ao k"), ("ch", "ch"), ("sh", "sh"), ("th", "th"), ("ph", "f"), ("wh", "w"), ("gh", ""),
        ("ck", "k"), ("ng", "ng"), ("nk", "ng k"), ("qu", "k w"), ("kn", "n"), ("wr", "r"), ("gn", "n"),
        ("mb", "m"), ("ee", "iy"), ("ea", "iy"), ("ie", "iy"), ("ei", "iy"), ("oo", "uw"), ("ou", "aw"),
        ("ow", "aw"), ("oa", "ow"), ("oe", "ow"), ("oi", "oy"), ("oy", "oy"), ("ai", "ey"), ("ay", "ey"),
        ("au", "ao"), ("aw", "ao"), ("ey", "ey"), ("ew", "uw"), ("ue", "uw"), ("ui", "uw"), ("ar", "aa r"),
        ("er", "er"), ("ir", "er"), ("ur", "er"), ("or", "ao r"), ("bb", "b"), ("dd", "d"), ("ff", "f"),
        ("gg", "g"), ("ll", "l"), ("mm", "m"), ("nn", "n"), ("pp", "p"), ("rr", "r"), ("ss", "s"),
        ("tt", "t"), ("zz", "z"), ("cc", "k"),
    ];

    private static int FirstVowel(string w)
    {
        for (var i = 0; i < w.Length; i++)
            if (IsVowel(w[i]) && !(w[i] == 'y' && i == 0)) return i;
        return -1;
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static string Long(char c) => c switch { 'a' => "ey", 'e' => "iy", 'i' => "ay", 'o' => "ow", _ => "uw" };

    private static string Short(char c) => c switch { 'a' => "ae", 'e' => "eh", 'i' => "ih", 'o' => "aa", _ => "ah" };
}
