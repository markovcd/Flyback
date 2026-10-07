using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.FakeDecider;

/// <summary>
/// Answers each question from <see cref="Script"/> by its id, and anything unscripted
/// with its first option, its lowest level or a yes, every one certain.
/// </summary>
public sealed class ScriptedDecider(IReadOnlyDictionary<string, Answer>? script = null) : IDecisionModel
{
    public const string Model = "scripted";

    private readonly List<DecisionRequest> asked = [];

    public string Id => "scripted";

    public string Name => "Scripted decider";

    public int Priority => -100;

    public AssistantCredential? Credential => null;

    /// <summary>What each question id is answered with.</summary>
    public IReadOnlyDictionary<string, Answer> Script { get; } = script ?? new Dictionary<string, Answer>();

    /// <summary>Every request answered, in order.</summary>
    public IReadOnlyList<DecisionRequest> Asked
    {
        get
        {
            lock (asked) return [.. asked];
        }
    }

    public IReadOnlyList<SettingField> Form(SettingValues values) => [];

    public string? Unavailable(DecisionConfig config) => null;

    public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        lock (asked) asked.Add(request);

        var answers = request.Questions.ToDictionary(
            q => q.Key,
            q => Script.TryGetValue(q.Key, out var scripted) ? scripted : Unscripted(q.Value),
            StringComparer.Ordinal);

        return Task.FromResult(new Decision(Model, answers, DecisionUsage.None));
    }

    private static Answer Unscripted(Question question) => question switch
    {
        Question.Choice choice => new Answer.Chosen(
            choice.Options[0].Label,
            choice.Options.Select((o, i) => (o.Label, P: i == 0 ? 1.0 : 0.0)).ToDictionary(o => o.Label, o => o.P),
            1),
        Question.Score score => new Answer.Scored(0, score.Levels, [.. score.Levels.Select((_, i) => i == 0 ? 1.0 : 0.0)], 1),
        Question.YesNo => new Answer.YesNo(1),
        _ => throw new ArgumentException("A question of no kind this knows.", nameof(question)),
    };
}
