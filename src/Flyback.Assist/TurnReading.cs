using Flyback.Plugins.Decide;

namespace Flyback.Assist;

/// <summary>
/// What a decision model makes of a message before the assistant is sent it, and of the
/// proposal once it comes back.
/// </summary>
internal sealed class TurnReading(Decisions decisions)
{
    /// <summary>What a message may be read as, by label.</summary>
    public static readonly IReadOnlyList<ChoiceOption> Intents =
    [
        new("edit", "Change the patch that is open"),
        new("new", "Build a new patch from nothing"),
        new("question", "A question about the patch that is open, wanting an answer rather than a change"),
        new("module", "A question about what a module does or how it is used"),
        new("preset", "Asks for one of the presets to be loaded or shown"),
        new("other", "Not about Flyback, patches, sound or pictures at all"),
    ];

    /// <summary>Below this, a proposal is said to maybe not do what was asked. Low, since doubting good work costs more than missing bad.</summary>
    public const double Doubted = 0.4;

    /// <summary>How sure the model has to be that a message wants an answer before the assistant is told so.</summary>
    public const double Sure = 0.8;

    /// <summary>What a turn read as a question is told ahead of the message.</summary>
    public const string Answering =
        "[From Flyback, not the person: this reads as a question wanting an answer. Answer it, and change and propose nothing unless the person asks for a change.]";

    /// <summary>The likeliest reading of <paramref name="message"/>, or null without a model.</summary>
    public async Task<Reading?> Read(string message, string patch, CancellationToken cancel)
    {
        var request = DecisionRequest.One(
            $"The patch: {patch}{Environment.NewLine}The message: {message}",
            "intent",
            new Question.Choice("What does the message ask for?", Intents));

        return await decisions.Ask(DecisionUse.Turns, request, cancel).ConfigureAwait(false) is { } decision
               && decision.Answers.GetValueOrDefault("intent") is Answer.Chosen chosen
            ? new Reading(
                chosen.Option,
                chosen.Probabilities.GetValueOrDefault(chosen.Option),
                chosen.Probabilities.GetValueOrDefault("module") + chosen.Probabilities.GetValueOrDefault("question"))
            : null;
    }

    /// <summary>How likely <paramref name="proposed"/> does what <paramref name="asked"/> asked, or null without a model.</summary>
    /// <remarks>The assistant's word alone: a line naming the patch's modules beside it only pulls the answer toward no.</remarks>
    public async Task<double?> Does(string asked, string proposed, CancellationToken cancel)
    {
        var request = DecisionRequest.One(
            $"Asked for: {asked}{Environment.NewLine}The assistant says it made: {proposed}",
            "does",
            new Question.YesNo("Does what was made do what was asked for?"));

        return await decisions.Ask(DecisionUse.Turns, request, cancel).ConfigureAwait(false) is { } decision
               && decision.Answers.GetValueOrDefault("does") is Answer.YesNo yes
            ? yes.Probability
            : null;
    }

    /// <summary>A message read as one intent, how likely that reading is, and how likely it wants an answer at all.</summary>
    /// <param name="Answer">A question about a module and one about the patch together: the two readings answered rather than built from.</param>
    internal sealed record Reading(string Intent, double Probability, double Answer)
    {
        /// <summary>
        /// Whether the message surely wants an answer rather than a change. The one reading acted on,
        /// as the sum of the two question readings: the model tells them apart badly and need not.
        /// </summary>
        public bool Asks => Answer >= Sure;

        /// <summary>What the transcript says it was read as.</summary>
        public string Said => Intent switch
        {
            "edit" => "a change to the patch",
            "new" => "a new patch",
            "question" => "a question about the patch",
            "module" => "a question about a module",
            "preset" => "a preset to load",
            _ => "not about Flyback",
        };
    }
}
