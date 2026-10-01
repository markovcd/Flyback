using System.Net;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>One request to a provider, and when a refusal is waited out.</summary>
public class AssistantPostTests
{
    private static readonly Uri Nowhere = new("https://nowhere.invalid/v1/chat");

    [Theory]
    [InlineData(429, true)]
    [InlineData(408, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(529, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(422, false)]
    public void Only_a_refusal_that_might_pass_is_worth_sending_again(int status, bool again) =>
        AssistantPost.Retryable(status).ShouldBe(again);

    [Fact]
    public async Task A_refusal_that_clears_soon_is_waited_out()
    {
        var answers = new Answers((HttpStatusCode.TooManyRequests, "slow down"), (HttpStatusCode.OK, "{}"));
        var transport = new KeyedTransport(null, null, new AssistantCredential("", ""), answers);

        var said = await AssistantPost.Send(
            transport, Nowhere, "{}", _ => TimeSpan.Zero, (status, _) => $"refused {status}", TestContext.Current.CancellationToken);

        said.ShouldBe("{}");
        answers.Asked.ShouldBe(2);
    }

    /// <summary>A wait is told as it starts, so whoever shows the turn can say why nothing is happening.</summary>
    [Fact]
    public async Task A_wait_is_told_before_it_is_waited()
    {
        var answers = new Answers((HttpStatusCode.TooManyRequests, "slow down"), (HttpStatusCode.OK, "{}"));
        var transport = new KeyedTransport(null, null, new AssistantCredential("", ""), answers);
        var told = new List<(TimeSpan Wait, int Status)>();

        AssistantPost.Waiting = (wait, status) => told.Add((wait, status));

        await AssistantPost.Send(
            transport, Nowhere, "{}", _ => TimeSpan.Zero, (status, _) => $"refused {status}", TestContext.Current.CancellationToken);

        told.ShouldBe([(TimeSpan.Zero, 429)]);
    }

    /// <summary>A limit that resets further out than anybody should sit and watch is a quota, told at once.</summary>
    [Fact]
    public async Task A_quota_is_told_rather_than_waited_on()
    {
        var answers = new Answers((HttpStatusCode.TooManyRequests, "quota"));
        var transport = new KeyedTransport(null, null, new AssistantCredential("", ""), answers);

        var refused = await Should.ThrowAsync<HttpRequestException>(() => AssistantPost.Send(
            transport, Nowhere, "{}", _ => TimeSpan.FromHours(1), (status, body) => $"refused {status}: {body}", TestContext.Current.CancellationToken));

        refused.Message.ShouldBe("refused 429: quota");
        answers.Asked.ShouldBe(1);
    }

    [Fact]
    public async Task A_refusal_that_will_still_be_wrong_is_not_sent_again()
    {
        var answers = new Answers((HttpStatusCode.Unauthorized, "bad key"));
        var transport = new KeyedTransport(null, null, new AssistantCredential("", ""), answers);

        await Should.ThrowAsync<HttpRequestException>(() => AssistantPost.Send(
            transport, Nowhere, "{}", _ => null, (status, _) => $"refused {status}", TestContext.Current.CancellationToken));

        answers.Asked.ShouldBe(1);
    }

    /// <summary>Answers each request with the next one given, the last repeating.</summary>
    private sealed class Answers(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        public int Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var (status, body) = answers[Math.Min(Asked++, answers.Length - 1)];

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
