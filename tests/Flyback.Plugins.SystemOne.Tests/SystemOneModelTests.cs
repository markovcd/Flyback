using System.Net;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.SystemOne.Tests;

public class SystemOneModelTests
{
    private const string Answered = """
        {"model": "hosted-1.13.0", "answers": {"money": {"type": "noul", "noul": 0.97}}, "usage": {"input_tokens": 12, "output_tokens": 0}}
        """;

    private static readonly DecisionRequest Asked =
        DecisionRequest.One("we were billed twice", "money", new Question.YesNo("Is this about money?"));

    private readonly SystemOneModel model = new();

    private static readonly SettingValues Served = SettingValues.None.With(SystemOneModel.EndpointKey, "https://decisions.test");

    private DecisionConfig Config(Canned canned, string? key = "test-key-not-real", SettingValues? values = null)
    {
        values ??= Served;
        var origin = KeyedTransport.OriginOf(model.Endpoint(values)!);

        return new DecisionConfig(new KeyedTransport(key, origin, model.Credential!, canned), values, null);
    }

    [Fact]
    public async Task It_posts_the_questions_to_the_endpoint_with_its_key_and_reads_the_answer()
    {
        var canned = new Canned((HttpStatusCode.OK, Answered));

        var decision = await model.DecideAsync(Asked, Config(canned), TestContext.Current.CancellationToken);

        decision.Answers["money"].ShouldBe(new Answer.YesNo(0.97));
        decision.Usage.InputTokens.ShouldBe(12);

        var request = canned.Requests.ShouldHaveSingleItem();
        request.RequestUri.ShouldBe(new Uri("https://decisions.test/v1/systemone"));
        request.Headers.Authorization!.ToString().ShouldBe("Bearer test-key-not-real");

        var sent = JsonNode.Parse(canned.Bodies[0])!;
        sent["model"].ShouldBeNull("with no model set, the endpoint chooses");
        sent["questions"]!["money"]!["type"]!.GetValue<string>().ShouldBe("noul");
    }

    [Fact]
    public async Task A_laya_serve_of_your_own_is_asked_without_a_key()
    {
        var canned = new Canned((HttpStatusCode.OK, Answered));
        var values = SettingValues.None.With(SystemOneModel.EndpointKey, "http://localhost:8000/").With(SystemOneModel.ModelKey, "english");

        var config = Config(canned, key: null, values);

        model.Unavailable(config).ShouldBeNull();

        await model.DecideAsync(Asked, config, TestContext.Current.CancellationToken);

        canned.Requests.ShouldHaveSingleItem().RequestUri.ShouldBe(new Uri("http://localhost:8000/v1/systemone"));
        canned.Requests[0].Headers.Authorization.ShouldBeNull();
        JsonNode.Parse(canned.Bodies[0])!["model"]!.GetValue<string>().ShouldBe("english");
    }

    [Fact]
    public void With_no_endpoint_set_it_asks_a_laya_serve_on_this_machine()
    {
        model.Endpoint(SettingValues.None).ShouldBe(new Uri("http://localhost:8000/v1/systemone"));
        model.Unavailable(DecisionConfig.Unset).ShouldBeNull();
    }

    [Fact]
    public void An_endpoint_that_is_no_address_says_so()
    {
        var values = SettingValues.None.With(SystemOneModel.EndpointKey, "ftp://example.test");

        model.Unavailable(new DecisionConfig(KeyedTransport.None, values, null)).ShouldNotBeNull().ShouldContain("not an http");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"detail":"Invalid API key"}""", "The key was refused. It said: Invalid API key")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"detail":[{"msg":"field required"}]}""", "could not read the questions")]
    public async Task A_refusal_is_a_sentence(HttpStatusCode status, string body, string said)
    {
        var canned = new Canned((status, body));

        var refused = await Should.ThrowAsync<HttpRequestException>(
            model.DecideAsync(Asked, Config(canned), TestContext.Current.CancellationToken));

        refused.Message.ShouldContain(said);
        canned.Requests.Count.ShouldBe(1, "a 4xx other than 429 will still be wrong a second later");
    }

    [Fact]
    public void A_rate_limit_and_an_overload_say_what_they_are()
    {
        SystemOneModel.Complaint(429, "").ShouldContain("Too many questions");
        SystemOneModel.Complaint(529, "<html>").ShouldBe("The endpoint is overloaded.");
    }

    [Fact]
    public async Task An_answer_that_is_not_a_decision_is_a_sentence_rather_than_a_crash()
    {
        var canned = new Canned((HttpStatusCode.OK, """{"choices":[]}"""));

        var refused = await Should.ThrowAsync<InvalidOperationException>(
            model.DecideAsync(Asked, Config(canned), TestContext.Current.CancellationToken));

        refused.Message.ShouldContain("not a decision");
    }

    [Fact]
    public async Task A_request_too_big_is_never_sent()
    {
        var canned = new Canned((HttpStatusCode.OK, Answered));
        var huge = DecisionRequest.One(new string('x', DecisionRequest.LongestState + 1), "q", new Question.YesNo("Is it?"));

        await Should.ThrowAsync<ArgumentException>(model.DecideAsync(huge, Config(canned), TestContext.Current.CancellationToken));

        canned.Requests.ShouldBeEmpty();
    }
}
