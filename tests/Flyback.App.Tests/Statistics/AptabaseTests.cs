using System.Globalization;
using System.Net;
using System.Text.Json;
using Flyback.App.Statistics;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Statistics;

/// <summary>
/// The wire: which key is carried, where it is taken, and what one event looks like
/// when it lands (ADR-0094). Against an Aptabase that answers from memory.
/// </summary>
public sealed class AptabaseTests
{
    private static readonly Version Running = new(1, 2, 3);

    private sealed class Service : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public HttpStatusCode Answer { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancel);

            return new HttpResponseMessage(Answer) { Content = new StringContent("{}") };
        }
    }

    private static UsageEvent Started =>
        new("started", new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["platform"] = "win-x64",
            ["flyback.voice"] = true,
        });

    [Theory]
    [InlineData("A-US-1234567890", "https://us.aptabase.com/")]
    [InlineData("A-EU-1234567890", "https://eu.aptabase.com/")]
    public void A_key_says_where_its_events_are_taken(string key, string host)
    {
        Aptabase.ReadKey(key)!.Value.Host.AbsoluteUri.ShouldBe(host);
    }

    [Fact]
    public void The_lines_that_say_what_the_file_is_are_not_the_key()
    {
        Aptabase.ReadKey("# nothing but a comment\n\n# and another\n").ShouldBeNull();
    }

    [Theory]
    [InlineData("not a key")]
    [InlineData("A-MARS-1234567890")]
    [InlineData("A-SH-1234567890")]
    public void A_key_that_says_nowhere_counts_nothing(string text)
    {
        Aptabase.ReadKey(text).ShouldBeNull();
    }

    [Fact]
    public void A_service_somebody_runs_themselves_is_named_on_the_line_after()
    {
        var read = Aptabase.ReadKey("A-SH-1234567890\nhttps://stats.example.com")!.Value;

        read.Host.AbsoluteUri.ShouldBe("https://stats.example.com/");
    }

    [Fact]
    public void This_build_counts_under_Flybacks_own_key()
    {
        var read = Aptabase.EmbeddedKey();

        read.ShouldNotBeNull();
        read!.Value.Key.ShouldStartWith("A-EU-");
        read.Value.Host.AbsoluteUri.ShouldBe("https://eu.aptabase.com/");
    }

    [Fact]
    public async Task An_event_arrives_with_the_key_in_the_header_and_nothing_else_about_the_machine()
    {
        var service = new Service();

        using var aptabase = Aptabase.Open(Running, service)!;

        await aptabase.SendAsync(Started, CancellationToken.None);

        service.Request!.RequestUri!.AbsoluteUri.ShouldBe("https://eu.aptabase.com/api/v0/event");
        service.Request.Headers.GetValues("App-Key").ShouldHaveSingleItem().ShouldStartWith("A-EU-");

        using var body = JsonDocument.Parse(service.Body);
        var sent = body.RootElement;

        sent.GetProperty("eventName").GetString().ShouldBe("started");
        sent.GetProperty("props").GetProperty("platform").GetString().ShouldBe("win-x64");
        sent.GetProperty("props").GetProperty("flyback.voice").GetBoolean().ShouldBeTrue();

        var about = sent.GetProperty("systemProps");
        about.GetProperty("appVersion").GetString().ShouldBe("1.2.3");
        about.GetProperty("sdkVersion").GetString().ShouldBe("flyback@1.2.3");
        about.GetProperty("isDebug").GetBoolean().ShouldBeFalse();
        about.TryGetProperty("locale", out _).ShouldBeFalse("what language somebody works in is theirs");
    }

    [Fact]
    public async Task A_build_made_on_a_developers_machine_is_sent_as_debug_under_its_own_version()
    {
        var service = new Service();

        using var aptabase = Aptabase.Open("0.1.0-dev+37fc87f", debug: true, service)!;

        await aptabase.SendAsync(Started, CancellationToken.None);

        using var body = JsonDocument.Parse(service.Body);
        var about = body.RootElement.GetProperty("systemProps");

        about.GetProperty("isDebug").GetBoolean().ShouldBeTrue();
        about.GetProperty("appVersion").GetString().ShouldBe("0.1.0-dev+37fc87f");
    }

    /// <summary>
    /// The session is the run: the second it began and a random number after it,
    /// which is the shape the service reads and the whole of what joins one event to
    /// another.
    /// </summary>
    [Fact]
    public async Task The_run_is_named_by_when_it_began_and_nothing_else()
    {
        var service = new Service();

        using var aptabase = Aptabase.Open(Running, service)!;

        await aptabase.SendAsync(Started, CancellationToken.None);
        var first = Session(service);

        await aptabase.SendAsync(Started, CancellationToken.None);
        Session(service).ShouldBe(first, "one run, one session");

        var began = DateTimeOffset.FromUnixTimeSeconds(long.Parse(first, CultureInfo.InvariantCulture) / 100_000_000);

        began.ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task A_refusal_is_thrown_for_the_caller_to_note()
    {
        var service = new Service { Answer = HttpStatusCode.BadRequest };

        using var aptabase = Aptabase.Open(Running, service)!;

        await Should.ThrowAsync<HttpRequestException>(
            () => aptabase.SendAsync(Started, CancellationToken.None));
    }

    private static string Session(Service service)
    {
        using var body = JsonDocument.Parse(service.Body);

        return body.RootElement.GetProperty("sessionId").GetString()!;
    }
}
