using System.Net;
using System.Reflection;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>The key goes on requests to the origin it was entered for, and on nothing else.</summary>
public sealed class KeyedTransportTests
{
    private static readonly AssistantCredential Bearer = new("KEY", "");

    /// <summary>
    /// A conversation carries on only while its configuration is the one it began with,
    /// and the transport is rebuilt for every message, so two built alike must be equal.
    /// </summary>
    [Fact]
    public void Transports_built_alike_are_the_same_configuration()
    {
        var values = Settings.SettingValues.None;

        new AssistantConfig(new KeyedTransport("sk-secret", "https://a.test", Bearer), values)
            .ShouldBe(new AssistantConfig(new KeyedTransport("sk-secret", "https://a.test", Bearer), values));

        new AssistantConfig(new KeyedTransport(null, null, Bearer), values)
            .ShouldBe(new AssistantConfig(new KeyedTransport(null, null, Bearer), values));

        new AssistantConfig(new KeyedTransport("sk-secret", "https://a.test", Bearer), values)
            .ShouldNotBe(new AssistantConfig(new KeyedTransport("sk-other", "https://a.test", Bearer), values));

        new AssistantConfig(new KeyedTransport("sk-secret", "https://a.test", Bearer), values)
            .ShouldNotBe(new AssistantConfig(new KeyedTransport("sk-secret", "https://b.test", Bearer), values));
    }

    [Fact]
    public async Task The_key_goes_to_the_origin_it_is_bound_to_and_no_other()
    {
        var network = new Recorder();
        var transport = new KeyedTransport("sk-secret", "https://api.example.test", Bearer, network);

        foreach (var address in new[]
                 {
                     "https://api.example.test/v1/models",
                     "https://elsewhere.test/v1/models",
                     "http://api.example.test/v1/models",
                     "https://api.example.test:8443/v1/models",
                 })
        {
            await transport.Send(new Uri(address), null, TestContext.Current.CancellationToken);
        }

        network.Authorizations.ShouldBe(["Bearer sk-secret", null, null, null]);
    }

    [Fact]
    public async Task A_header_of_the_providers_naming_carries_the_key_alone()
    {
        var network = new Recorder("x-goog-api-key");
        var credential = new AssistantCredential("KEY", "") { Header = "x-goog-api-key", Scheme = null };
        var transport = new KeyedTransport("g-secret", "https://generativelanguage.googleapis.com", credential, network);

        await transport.Send(new Uri("https://generativelanguage.googleapis.com/v1beta/models"), null, TestContext.Current.CancellationToken);

        network.Authorizations.ShouldBe(["g-secret"]);
    }

    [Fact]
    public async Task A_body_is_posted_as_json_and_no_body_is_a_get()
    {
        var network = new Recorder();
        var transport = new KeyedTransport("sk-secret", "https://api.example.test", Bearer, network);

        await transport.Send(new Uri("https://api.example.test/v1/chat"), """{"a":1}""", TestContext.Current.CancellationToken);
        await transport.Send(new Uri("https://api.example.test/v1/models"), null, TestContext.Current.CancellationToken);

        network.Sent.ShouldBe(["POST application/json {\"a\":1}", "GET"]);
    }

    [Fact]
    public async Task What_came_back_is_its_status_body_and_headers()
    {
        var network = new Recorder { Answer = (HttpStatusCode.TooManyRequests, "slow down", TimeSpan.FromSeconds(3)) };
        var transport = new KeyedTransport("sk-secret", "https://api.example.test", Bearer, network);

        var answer = await transport.Send(new Uri("https://api.example.test/v1/chat"), "{}", TestContext.Current.CancellationToken);

        answer.Status.ShouldBe(429);
        answer.Succeeded.ShouldBeFalse();
        answer.Body.ShouldBe("slow down");
        answer.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// A request the key went on, or anything that sends one, would let an assistant read
    /// the key back off it. So nothing an assistant is handed is one.
    /// </summary>
    [Fact]
    public void An_assistant_is_handed_nothing_that_holds_or_signs_a_request()
    {
        Type[] leaks = [typeof(HttpMessageHandler), typeof(HttpMessageInvoker), typeof(HttpRequestMessage), typeof(HttpResponseMessage)];

        foreach (var handed in new[] { typeof(IAssistantTransport), typeof(AssistantResponse), typeof(AssistantConfig) })
        {
            foreach (var type in Reached(handed))
                leaks.ShouldNotContain(leak => leak.IsAssignableFrom(type), $"{handed.Name} hands out a {type.Name}");
        }
    }

    [Fact]
    public async Task An_admin_key_is_never_sent()
    {
        var network = new Recorder();
        var transport = new KeyedTransport("sk-admin-abcdefghijklmnop", "https://api.example.test", Bearer, network);

        await transport.Send(new Uri("https://api.example.test/v1/models"), null, TestContext.Current.CancellationToken);

        transport.HasKey.ShouldBeFalse();
        network.Authorizations.ShouldBe([null]);
    }

    [Fact]
    public void The_key_is_found_in_text_and_taken_out_of_it()
    {
        var transport = new KeyedTransport("sk-proj-abcdefghijklmnop", "https://api.example.test", Bearer);

        transport.Holds("my key is sk-proj-abcdefghijklmnop, keep it safe").ShouldBeTrue();
        transport.Holds("nothing here").ShouldBeFalse();
        transport.Scrubbed("my key is sk-proj-abcdefghijklmnop").ShouldBe("my key is [key]");
        transport.Scrubbed("nothing here").ShouldBe("nothing here");
    }

    [Fact]
    public async Task A_key_pasted_with_a_trailing_space_is_found_in_text_and_sent_without_it()
    {
        var network = new Recorder();
        var transport = new KeyedTransport("sk-proj-abcdefghijklmnopqrstuvwx ", "https://api.example.test", Bearer, network);

        transport.Holds("my key is sk-proj-abcdefghijklmnopqrstuvwx, make something").ShouldBeTrue();

        await transport.Send(new Uri("https://api.example.test/v1/models"), null, TestContext.Current.CancellationToken);

        network.Authorizations.ShouldBe(["Bearer sk-proj-abcdefghijklmnopqrstuvwx"]);
    }

    /// <summary>A local runtime takes any value as a key, and a short one is in every sentence.</summary>
    [Fact]
    public void A_key_too_short_to_be_a_secret_is_never_found()
    {
        var transport = new KeyedTransport("x", "http://localhost:11434", Bearer);

        transport.Holds("make a box").ShouldBeFalse();
        transport.Scrubbed("make a box").ShouldBe("make a box");
    }

    [Fact]
    public void No_key_is_no_key_wherever_it_would_go()
    {
        var transport = new KeyedTransport("  ", "https://api.example.test", Bearer);

        transport.HasKey.ShouldBeFalse();
        transport.Origin.ShouldBeNull();
        KeyedTransport.None.HasKey.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_that_prints_it_shows_the_key()
    {
        new KeyedTransport("sk-secret", "https://api.example.test", Bearer).ToString().ShouldNotContain("sk-secret");
        new BoundKey("sk-secret", "https://api.example.test").ToString().ShouldNotContain("sk-secret");
    }

    [Theory]
    [InlineData("https://API.Example.test/v1", "https://api.example.test")]
    [InlineData("https://api.example.test:443/v1", "https://api.example.test")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434")]
    public void An_origin_is_scheme_host_and_any_port_of_its_own(string address, string origin) =>
        KeyedTransport.OriginOf(new Uri(address)).ShouldBe(origin);

    /// <summary>Every type a public member of <paramref name="type"/> returns or takes, with a task unwrapped.</summary>
    private static IEnumerable<Type> Reached(Type type)
    {
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            switch (member)
            {
                case PropertyInfo property:
                    yield return Unwrapped(property.PropertyType);
                    break;
                case MethodInfo method:
                    yield return Unwrapped(method.ReturnType);
                    foreach (var parameter in method.GetParameters()) yield return Unwrapped(parameter.ParameterType);
                    break;
            }
        }
    }

    private static Type Unwrapped(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) ? type.GetGenericArguments()[0] : type;

    /// <summary>The network, as far as the key is concerned: what each request carried in the one header.</summary>
    private sealed class Recorder(string header = "Authorization") : HttpMessageHandler
    {
        public List<string?> Authorizations { get; } = [];

        public List<string> Sent { get; } = [];

        public (HttpStatusCode Status, string Body, TimeSpan? RetryAfter) Answer { get; init; } = (HttpStatusCode.OK, "{}", null);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.TryGetValues(header, out var values) ? string.Join(",", values) : null);

            Sent.Add(request.Content is { } content
                ? $"{request.Method} {content.Headers.ContentType?.MediaType} {await content.ReadAsStringAsync(cancellationToken)}"
                : $"{request.Method}");

            var response = new HttpResponseMessage(Answer.Status) { Content = new StringContent(Answer.Body) };

            if (Answer.RetryAfter is { } wait) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);

            return response;
        }
    }
}
