using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Speech;

/// <summary>The words a Speak says, and how, worked out into tracks when the patch is built.</summary>
internal sealed record SpeechExtra : NodeExtra
{
    public const string StateKey = "speak";

    public const string WordsKey = "words";

    public const string ModeKey = "mode";

    public const string NotesKey = "notes";

    public const string Speak = "speak";

    public const string Sing = "sing";

    public const string Greeting = "Hello. I am Flyback.";

    public override string Key => StateKey;

    public override IReadOnlyList<ExtraField> Fields =>
    [
        new ExtraField.Text(WordsKey, "words", Greeting, Multiline: true)
        {
            Help = "What it says, in English, a line to each of the 'line' output's numbers. A word it "
                + "says wrong can be spelled in sounds between square brackets: [hh ax l ow].",
        },
        new ExtraField.Choice(ModeKey, "mode", [new ChoiceOption(Speak, "Speak"), new ChoiceOption(Sing, "Sing")], Speak)
        {
            Help = "Speak says the words with a sentence's rise and fall. Sing gives each syllable a note, "
                + "in time with 'bpm'.",
        },
        new ExtraField.Text(NotesKey, "notes")
        {
            Help = "In Sing, a note for each syllable, written as a Note Sequencer's are, with its length "
                + "in beats after an '@': G3@3 E3@3 C3. A '~' is a rest, '~@2' a longer one. Round again if "
                + "there are fewer than the syllables.",
        },
    ];

    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var state = new ExtraState(Fields, node.StateOf(StateKey));
        var sung = state.Chosen(ModeKey) == Sing;
        var (sounds, unknown) = Pronunciation.Of(state.Text(WordsKey));

        if (unknown.Count > 0)
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no sound called {string.Join(", ", unknown.Distinct().Select(u => $"'{u}'"))} "
                + $"and skips it. The sounds are {string.Join(" ", Phonemes.Symbols)}.",
                IssueSeverity.Warning));
        }

        var said = Utterance.Of(sounds, sung);
        if (!sung) return ctx.With(StateKey, new Saying(said, null));

        var (tune, unread) = Tune.Of(said, state.Text(NotesKey));

        if (unread.Count > 0)
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' cannot read {string.Join(", ", unread.Distinct().Select(u => $"'{u}'"))} as a note "
                + "and leaves it out. A note is a name and an octave, or a number, with '@' and its beats after: G3@3.",
                IssueSeverity.Warning));
        }

        return ctx.With(StateKey, new Saying(said, tune));
    }
}
