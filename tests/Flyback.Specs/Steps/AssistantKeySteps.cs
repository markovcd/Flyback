using Flyback.App.Assist;
using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Where an assistant's key is sent, over a network that only records what each request carried.</summary>
[Binding]
public sealed class AssistantKeySteps : IDisposable
{
    private const string Key = "sk-proj-abcdefghijklmnop";

    /// <summary>A variable of this scenario's own, so no other scenario sees what it exports.</summary>
    private readonly string variable = $"FLYBACK_SPEC_KEY_{Guid.NewGuid():N}";

    private readonly Credentials credentials = new(null);
    private readonly List<string?> carried = [];
    private readonly List<PatchEvent> events = [];
    private Sending? assistant;
    private KnobSettingAssistant? listening;

    private Sending Assistant => assistant.ShouldNotBeNull();

    [Given("an admin key entered for an assistant that sends to {word}")]
    public void GivenAnAdminKey(string origin)
    {
        assistant = new Sending(origin, variable);
        credentials.Accept(assistant.Id, "sk-admin-abcdefghijklmnop", origin, keep: false);
    }

    [Given("a key exported for an assistant that sends to {word}")]
    public void GivenAnExportedKey(string origin)
    {
        assistant = new Sending(origin, variable);
        Environment.SetEnvironmentVariable(variable, Key);
    }

    [When("the assistant sends a request there")]
    public async Task WhenSent()
    {
        var transport = credentials.Transport(Assistant, SettingValues.None, new Recorder(carried));

        await transport.Send(new Uri(Assistant.Origin + "/v1/models"), null, CancellationToken.None);
    }

    [Then("the request carries no key")]
    public void ThenNoKey() => carried.ShouldBe([null]);

    [Then("the request carries the key")]
    public void ThenTheKey() => carried.ShouldBe([$"Bearer {Key}"]);

    [Then("the assistant says the key is not sent over plain http")]
    public void ThenItSaysWhy()
    {
        var config = new AssistantConfig(credentials.Transport(Assistant, SettingValues.None), SettingValues.None);

        Credentials.Elsewhere(Assistant, config).ShouldNotBeNull().ShouldContain("plain http");
    }

    [Given("a conversation with an assistant that has a key")]
    public void GivenAConversation() => listening = new KnobSettingAssistant();

    [When("a message with the key in it is sent")]
    public async Task WhenTheKeyIsInTheMessage()
    {
        var config = new AssistantConfig(new KeyedTransport(Key, "https://assistant.test", new AssistantCredential("", "")), SettingValues.None);

        using var run = new AssistantRun(listening.ShouldNotBeNull(), config, NodeCatalog.BuiltIn, new Patch());

        await foreach (var happened in run.Ask($"my key is {Key}, make something")) events.Add(happened);
    }

    [Then("the assistant never hears it")]
    public void ThenNeverHeard() => listening.ShouldNotBeNull().Heard.ShouldBeEmpty();

    [Then("the person is told the message had the key in it")]
    public void ThenTold() => events.ShouldHaveSingleItem().ShouldBeOfType<PatchEvent.Failed>().Message.ShouldContain("has your key in it");

    public void Dispose() => Environment.SetEnvironmentVariable(variable, null);

    /// <summary>An assistant that sends to one origin and is given its key in <c>variable</c>.</summary>
    private sealed class Sending(string origin, string variable) : IPatchAssistant
    {
        public string Origin => origin;

        public string Id => "sending";

        public string Name => "Sending";

        public int Priority => 0;

        public AssistantCredential Credential { get; } = new(variable, "");

        public Uri? Endpoint(SettingValues values) => new(origin + "/v1");

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public AssistantSenses Senses(SettingValues values) => default;

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => throw new NotSupportedException();
    }

    /// <summary>The network, as far as the key is concerned: what each request carried.</summary>
    private sealed class Recorder(List<string?> carried) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            carried.Add(request.Headers.Authorization?.ToString());

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
