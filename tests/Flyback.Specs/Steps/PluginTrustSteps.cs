using System.CommandLine;
using Reqnroll;
using Shouldly;
using Flyback.Cli.Common;
using Flyback.Plugins;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;

using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Specs.Steps;

/// <summary>
/// Which plugin folders run, over a plugins folder of the scenario's own and a list of
/// what was allowed that is nobody else's.
/// </summary>
[Binding]
public sealed class PluginTrustSteps : IDisposable
{
    private const string Copied = "Copied";

    private readonly string root = Directory.CreateTempSubdirectory("flyback-trust-specs").FullName;

    private PluginCatalog catalog = PluginCatalog.Empty;
    private int shipped;
    private readonly List<string?> carried = [];

    private string Plugins => Path.Combine(root, PluginHost.DirectoryName);

    private string Folder => Path.Combine(Plugins, Copied);

    private PluginAllowances Allowances => new(Path.Combine(root, "allowed-plugins.json"));

    [Given("a plugin folder copied in by hand")]
    public void GivenCopied() => CopyIn("Figures");

    [Given("a secret store plugin copied in by hand")]
    public void GivenStoreCopied() => CopyIn("Dpapi");

    [When("Flyback loads its plugins")]
    public void WhenLoaded() => catalog = PluginHost.Load(Plugins, new PluginTrust(true, ShippedList.Beside(Plugins), Allowances));

    [When("a Debug build loads its plugins")]
    public void WhenDebugLoads() => catalog = PluginHost.Load(Plugins, PluginTrust.Unchecked);

    [When("flyback-cli allows the copied plugin")]
    public void WhenAllowed() => Cli("plugin", "allow", Folder);

    [When("flyback-cli allows the copied plugin to keep keys")]
    public void WhenAllowedKeys() => Cli("plugin", "allow", Folder, "--secrets");

    /// <summary>Not the assembly: the one a scenario has run is open until the process ends.</summary>
    [When("a file in the copied plugin changes")]
    public void WhenChanged() =>
        File.AppendAllText(Directory.GetFiles(Folder, "*.deps.json").Single(), " ");

    [When("Flyback loads the plugins it was built with, allowing nothing")]
    public void WhenShippedLoaded()
    {
        shipped = Directory.GetDirectories(PluginHost.DefaultDirectory).Length;
        catalog = PluginHost.Load(
            PluginHost.DefaultDirectory,
            new PluginTrust(true, ShippedList.Beside(PluginHost.DefaultDirectory), PluginAllowances.None));
    }

    [When("two plugins offer an assistant under the same id")]
    public void WhenTwins() => catalog = PluginHost.LoadTypes(typeof(FirstTwin), typeof(SecondTwin));

    [Then("the copied plugin runs")]
    public void ThenRuns() => catalog.Plugins.ShouldContain(p => p.Info.Id == "flyback.figures");

    [Then("the copied plugin does not run")]
    public void ThenDoesNotRun() => catalog.Plugins.ShouldBeEmpty();

    [Then("it is listed as not yet allowed")]
    public void ThenNotYetAllowed() => Problem().ShouldStartWith("not yet allowed");

    [Then("it is listed as changed since it was allowed")]
    public void ThenChanged() => Problem().ShouldContain("has changed since this plugin was allowed");

    [Then("every one of them runs")]
    public void ThenAllRun()
    {
        catalog.Problems.ShouldBeEmpty();
        catalog.Plugins.Select(p => Path.GetDirectoryName(p.AssemblyPath)).Distinct().Count().ShouldBe(shipped);
    }

    [Then("its secret store is refused")]
    public void ThenStoreRefused()
    {
        catalog.SecretStores.ShouldBeEmpty();
        Problem().ShouldContain("secret store 'dpapi' is refused");
    }

    [Then("its secret store is offered")]
    public void ThenStoreOffered() => catalog.SecretStores.ShouldHaveSingleItem().Id.ShouldBe("dpapi");

    [Then("neither is offered")]
    public void ThenNeither() => catalog.Assistants.ShouldBeEmpty();

    [Then("both are named as the reason")]
    public void ThenBothNamed()
    {
        catalog.Problems.Count.ShouldBe(2);
        catalog.Problems.ShouldContain(p => p.Source == "test.first" && p.Message.Contains("Second twin", StringComparison.Ordinal));
        catalog.Problems.ShouldContain(p => p.Source == "test.second" && p.Message.Contains("First twin", StringComparison.Ordinal));
    }

    [Given("a key entered for an assistant that sends to {word}")]
    public void GivenKeyEntered(string origin) => Entered = origin;

    [When("the assistant sends a request there and one to {word}")]
    public async Task WhenSent(string elsewhere)
    {
        var credentials = new Credentials(null);
        var assistant = new Twin(Entered!);

        credentials.Accept(assistant.Id, "sk-secret", Entered!, keep: false);

        var transport = credentials.Transport(assistant, SettingValues.None, new Recorder(carried));

        await transport.Send(new Uri(Entered + "/v1/models"), null, CancellationToken.None);
        await transport.Send(new Uri(elsewhere + "/v1/models"), null, CancellationToken.None);
    }

    [Then("only the request to {word} carries the key")]
    public void ThenOnlyThere(string origin)
    {
        origin.ShouldBe(Entered);
        carried.ShouldBe(["Bearer sk-secret", null]);
    }

    private string? Entered { get; set; }

    public void Dispose()
    {
        // A folder a scenario loaded stays open until the process ends.
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Problem() => catalog.Problems.ShouldHaveSingleItem().Message;

    private void CopyIn(string shippedFolder)
    {
        var from = Path.Combine(PluginHost.DefaultDirectory, shippedFolder);

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Folder, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private void Cli(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = InProcessCli.Run(
            arguments,
            new PluginRegistry(() => PluginCatalog.Empty, Plugins, null, () => new PluginTrust(true, ShippedList.Beside(Plugins), Allowances)),
            new InvocationConfiguration { Output = output, Error = error });

        code.ShouldBe(Exit.Ok, error.ToString());
    }

    public sealed class FirstTwin : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("test.first", "First twin");

        public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new Twin("https://twin.test"));
    }

    public sealed class SecondTwin : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("test.second", "Second twin");

        public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new Twin("https://twin.test"));
    }

    private sealed class Twin(string origin) : IPatchAssistant
    {
        public string Id => "twin";

        public string Name => "Twin";

        public int Priority => 0;

        public AssistantCredential Credential { get; } = new("FLYBACK_SPECS_TWIN_KEY", "");

        public Uri Endpoint(SettingValues values) => new(origin + "/v1");

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public AssistantSenses Senses(SettingValues values) => default;

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => throw new NotSupportedException();
    }

    /// <summary>The network, as far as the key is concerned.</summary>
    private sealed class Recorder(List<string?> carried) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            carried.Add(request.Headers.Authorization?.ToString());

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
