using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice.Speech;

/// <summary>The words a Speak says, worked out into tracks when the patch is built.</summary>
internal sealed record SpeechExtra : NodeExtra
{
    public const string StateKey = "speak";

    public const string WordsKey = "words";

    public const string Greeting = "Hello. I am Flyback.";

    public override string Key => StateKey;

    public override IReadOnlyList<ExtraField> Fields =>
    [
        new ExtraField.Text(WordsKey, "words", Greeting)
        {
            Help = "What it says, in English. A word it says wrong can be spelled in sounds between "
                + "square brackets: [hh ax l ow].",
        },
    ];

    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var state = new ExtraState(Fields, node.StateOf(StateKey));
        var (sounds, unknown) = Pronunciation.Of(state.Text(WordsKey));

        if (unknown.Count > 0)
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no sound called {string.Join(", ", unknown.Distinct().Select(u => $"'{u}'"))} "
                + $"and skips it. The sounds are {string.Join(" ", Phonemes.Symbols)}.",
                IssueSeverity.Warning));
        }

        return ctx.With(StateKey, Utterance.Of(sounds));
    }
}
