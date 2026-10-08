using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Tests;

/// <summary>A decision model that favors one option by label in every choice, and says yes to nothing.</summary>
internal sealed class RankingDecider(params (string Label, double P)[] favored) : IDecisionModel
{
    public List<DecisionRequest> Asked { get; } = [];

    /// <summary>Where set, every option's probability by the question's id and the option's label, in place of <c>favored</c>.</summary>
    public Func<string, string, double>? Script { get; init; }

    public string Id => "ranking";

    public string Name => "Ranking";

    public int Priority => 0;

    public AssistantCredential? Credential => null;

    public IReadOnlyList<SettingField> Form(SettingValues values) => [];

    public string? Unavailable(DecisionConfig config) => null;

    public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel)
    {
        Asked.Add(request);

        var answers = request.Questions.ToDictionary(q => q.Key, q => q.Value switch
        {
            Question.Choice choice => Chosen(q.Key, choice),
            _ => (Answer)new Answer.YesNo(0),
        });

        return Task.FromResult(new Decision("ranking", answers, DecisionUsage.None));
    }

    private Answer Chosen(string id, Question.Choice choice)
    {
        if (Script is { } script)
        {
            var scripted = choice.Options.ToDictionary(o => o.Label, o => script(id, o.Label));
            var top = scripted.MaxBy(p => p.Value);

            return new Answer.Chosen(top.Key, scripted, top.Value);
        }

        var given = choice.Options.ToDictionary(o => o.Label, o => favored.FirstOrDefault(f => f.Label == o.Label).P);
        var rest = Math.Max(0, 1 - given.Values.Sum()) / Math.Max(1, given.Count(g => g.Value == 0));
        var probabilities = given.ToDictionary(g => g.Key, g => g.Value == 0 ? rest : g.Value);
        var best = probabilities.MaxBy(p => p.Value);

        return new Answer.Chosen(best.Key, probabilities, best.Value);
    }
}
