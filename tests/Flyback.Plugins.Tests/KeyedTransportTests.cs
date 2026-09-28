using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>The key goes on requests to the origin it was entered for, and on nothing else.</summary>
public sealed class KeyedTransportTests
{
    private static readonly AssistantCredential Bearer = new("KEY", "");

    [Fact]
    public async Task The_key_goes_to_the_origin_it_is_bound_to_and_no_other()
    {
        var network = new Recorder();
        using var client = new HttpClient(new KeyedTransport("sk-secret", "https://api.example.test", Bearer, network).Handler, disposeHandler: false);

        await client.GetAsync(new Uri("https://api.example.test/v1/models"), TestContext.Current.CancellationToken);
        await client.GetAsync(new Uri("https://elsewhere.test/v1/models"), TestContext.Current.CancellationToken);
        await client.GetAsync(new Uri("http://api.example.test/v1/models"), TestContext.Current.CancellationToken);
        await client.GetAsync(new Uri("https://api.example.test:8443/v1/models"), TestContext.Current.CancellationToken);

        network.Authorizations.ShouldBe(["Bearer sk-secret", null, null, null]);
    }

    [Fact]
    public async Task A_header_of_the_providers_naming_carries_the_key_alone()
    {
        var network = new Recorder("x-goog-api-key");
        var credential = new AssistantCredential("KEY", "") { Header = "x-goog-api-key", Scheme = null };
        using var client = new HttpClient(new KeyedTransport("g-secret", "https://generativelanguage.googleapis.com", credential, network).Handler, disposeHandler: false);

        await client.GetAsync(new Uri("https://generativelanguage.googleapis.com/v1beta/models"), TestContext.Current.CancellationToken);

        network.Authorizations.ShouldBe(["g-secret"]);
    }

    [Fact]
    public async Task A_header_the_assistant_set_itself_is_not_sent_on()
    {
        var network = new Recorder();
        using var client = new HttpClient(new KeyedTransport("sk-secret", "https://api.example.test", Bearer, network).Handler, disposeHandler: false);

        client.DefaultRequestHeaders.Add("Authorization", "Bearer made-up");

        await client.GetAsync(new Uri("https://elsewhere.test/"), TestContext.Current.CancellationToken);

        network.Authorizations.ShouldBe([null]);
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

    [Fact]
    public async Task An_assistant_that_disposes_its_client_leaves_the_network_to_everyone_else()
    {
        var network = new Recorder();
        var first = new KeyedTransport("sk-one", "https://api.example.test", Bearer, network);
        var second = new KeyedTransport("sk-two", "https://api.example.test", Bearer, network);

        using (var careless = new HttpClient(first.Handler)) { }

        using var client = new HttpClient(second.Handler, disposeHandler: false);

        await client.GetAsync(new Uri("https://api.example.test/"), TestContext.Current.CancellationToken);

        network.Authorizations.ShouldBe(["Bearer sk-two"]);
    }

    [Theory]
    [InlineData("https://API.Example.test/v1", "https://api.example.test")]
    [InlineData("https://api.example.test:443/v1", "https://api.example.test")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434")]
    public void An_origin_is_scheme_host_and_any_port_of_its_own(string address, string origin) =>
        KeyedTransport.OriginOf(new Uri(address)).ShouldBe(origin);

    /// <summary>The network, as far as the key is concerned: what each request carried in the one header.</summary>
    private sealed class Recorder(string header = "Authorization") : HttpMessageHandler
    {
        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.TryGetValues(header, out var values) ? string.Join(",", values) : null);

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
