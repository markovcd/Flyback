using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>Which model a feature's question goes to, and that a question never takes the feature down with it.</summary>
public class DecisionsTests
{
    private static readonly DecisionRequest Asked = DecisionRequest.One("s", "q", new Question.YesNo("Is it?"));

    private static Decisions With(DecisionSettings settings, params IDecisionModel[] models) =>
        new(models, settings, new Credentials(null), new ModelStore(Path.Combine(Path.GetTempPath(), "flyback-no-models")));

    [Fact]
    public void Nobody_having_chosen_means_the_likeliest_model_that_sends_nothing_anywhere()
    {
        var local = new Model("local", priority: 0);
        var hosted = new Model("hosted", priority: 100) { Hosted = true };

        With(new DecisionSettings(), hosted, local).Chosen.ShouldBe(local);
    }

    [Fact]
    public void A_hosted_model_is_asked_only_once_somebody_chose_it()
    {
        var hosted = new Model("hosted") { Hosted = true };

        With(new DecisionSettings(), hosted).Chosen.ShouldBeNull();
        With(new DecisionSettings { Model = "hosted" }, hosted).Chosen.ShouldBe(hosted);
    }

    [Fact]
    public async Task Off_asks_nothing()
    {
        var local = new Model("local");
        var decisions = With(new DecisionSettings { Model = DecisionSettings.Off }, local);

        (await decisions.Ask(Asked, TestContext.Current.CancellationToken)).ShouldBeNull();

        local.Asked.ShouldBe(0);
        decisions.Problem.ShouldBe("No decision model is in use.");
    }

    [Fact]
    public async Task A_model_that_fails_is_an_answer_of_nothing_and_a_reason()
    {
        var decisions = With(new DecisionSettings(), new Model("local") { Throws = new InvalidOperationException("the weights are gone") });

        (await decisions.Ask(Asked, TestContext.Current.CancellationToken)).ShouldBeNull();

        decisions.Problem.ShouldBe("local: the weights are gone");
    }

    [Fact]
    public async Task A_model_that_cannot_answer_is_not_asked()
    {
        var local = new Model("local") { Refuses = "No key yet." };
        var decisions = With(new DecisionSettings(), local);

        (await decisions.Ask(Asked, TestContext.Current.CancellationToken)).ShouldBeNull();

        local.Asked.ShouldBe(0);
        decisions.Problem.ShouldBe("No key yet.");
    }

    [Fact]
    public async Task An_answer_comes_back_as_the_model_gave_it()
    {
        var decisions = With(new DecisionSettings(), new Model("local"));

        (await decisions.Ask(Asked, TestContext.Current.CancellationToken))!.Answers["q"].ShouldBe(new Answer.YesNo(0.75));
        decisions.Problem.ShouldBeNull();
    }

    [Fact]
    public void A_model_s_key_is_filed_apart_from_an_assistant_s_of_the_same_id()
    {
        Decisions.Account(new Model("openai")).ShouldNotBe("openai");
    }

    private sealed class Model(string id, int priority = 0) : IDecisionModel
    {
        public bool Hosted { get; init; }

        public string? Refuses { get; init; }

        public Exception? Throws { get; init; }

        public int Asked { get; private set; }

        public string Id => id;

        public string Name => id;

        public int Priority => priority;

        public AssistantCredential? Credential => Hosted ? new AssistantCredential("HOSTED_KEY", "") : null;

        public Uri? Endpoint(SettingValues values) => Hosted ? new Uri("https://hosted.test/v1/systemone") : null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => Refuses;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel)
        {
            Asked++;

            if (Throws is not null) throw Throws;

            return Task.FromResult(new Decision(id, new Dictionary<string, Answer> { ["q"] = new Answer.YesNo(0.75) }, DecisionUsage.None));
        }
    }
}
