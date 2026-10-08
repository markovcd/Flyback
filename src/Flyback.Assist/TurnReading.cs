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

    /// <summary>Below this, a proposal is said to maybe not do what was asked.</summary>
    public const double Doubted = 0.5;

    /// <summary>How sure the model has to be that a message asks about a module before the assistant is told so.</summary>
    public const double Sure = 0.8;

    /// <summary>What a turn read as a question about a module is told ahead of the message.</summary>
    public const string Answering =
        "[From Flyback, not the person: this reads as a question about a module. Answer it, and change and propose nothing unless the person asks for a change.]";

    /// <summary>The likeliest reading of <paramref name="message"/>, or null without a model.</summary>
    public async Task<Reading?> Read(string message, string patch, CancellationToken cancel)
    {
        var request = DecisionRequest.One(
            $"The patch: {patch}{Environment.NewLine}The message: {message}",
            "intent",
            new Question.Choice("What does the message ask for?", Intents));

        return await decisions.Ask(DecisionUse.Turns, request, cancel).ConfigureAwait(false) is { } decision
               && decision.Answers.GetValueOrDefault("intent") is Answer.Chosen chosen
            ? new Reading(chosen.Option, chosen.Probabilities.GetValueOrDefault(chosen.Option))
            : null;
    }

    /// <summary>How likely <paramref name="proposed"/> does what <paramref name="asked"/> asked, or null without a model.</summary>
    /// <param name="patch">The proposed patch itself, so the answer is not the assistant's word alone.</param>
    public async Task<double?> Does(string asked, string proposed, string patch, CancellationToken cancel)
    {
        var request = DecisionRequest.One(
            $"Asked for: {asked}{Environment.NewLine}The assistant says it made: {proposed}{Environment.NewLine}What it made: {patch}",
            "does",
            new Question.YesNo("Does what was made do what was asked for?"));

        return await decisions.Ask(DecisionUse.Turns, request, cancel).ConfigureAwait(false) is { } decision
               && decision.Answers.GetValueOrDefault("does") is Answer.YesNo yes
            ? yes.Probability
            : null;
    }

    /// <summary>A message read as one intent, and how likely that reading is.</summary>
    internal sealed record Reading(string Intent, double Probability)
    {
        /// <summary>
        /// Whether the message surely asks what a module does. The only reading acted on: a question
        /// about the patch and one not about Flyback at all are too often taken for each other.
        /// </summary>
        public bool Asks => Intent == "module" && Probability >= Sure;

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
