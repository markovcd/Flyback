using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// Which provider gets asked, what happens when one cannot be, and where the
/// answer is written.
/// </summary>
/// <remarks>
/// None of this reaches a network or the real settings file, and both of those
/// are the point: every question a real survey asks is billed to somebody, so
/// the parts of this command worth proving are exactly the parts that decide
/// whether to ask at all.
/// </remarks>
public class ProbeCommandTests : IDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(),
        "flyback-probe-" + Guid.NewGuid().ToString("N"),
        "assistant.json");

    private readonly List<string> variables = [];

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(path);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        foreach (var variable in variables) Environment.SetEnvironmentVariable(variable, null);
    }

    [Fact]
    public async Task Without_arguments_it_asks_the_one_the_settings_are_on()
    {
        Keyed("ONE_KEY", "TWO_KEY");
        Settled("two");

        var (code, said, _) = await Probe(new Options());

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("two-flash");
        said.ShouldNotContain("one-flash");
    }

    /// <summary>
    /// The conservative default, and the reason for it: every question is billed,
    /// so a bare command asks one provider rather than fanning out across every
    /// key on the machine.
    /// </summary>
    [Fact]
    public async Task Without_arguments_it_writes_only_that_one_down()
    {
        Keyed("ONE_KEY", "TWO_KEY");
        Settled("two");

        await Probe(new Options());

        var written = AssistantSettings.Load(path);

        written.Of("two").Text(Survey.Key).ShouldNotBeEmpty();
        written.Of("one").Text(Survey.Key).ShouldBeEmpty();
    }

    [Fact]
    public async Task All_asks_every_provider_that_has_a_key()
    {
        Keyed("ONE_KEY", "TWO_KEY");

        var (code, said, _) = await Probe(new Options { Provider = "all" });

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("one-flash");
        said.ShouldContain("two-flash");

        var written = AssistantSettings.Load(path);

        written.Of("one").Text(Survey.Key).ShouldNotBeEmpty();
        written.Of("two").Text(Survey.Key).ShouldNotBeEmpty();
    }

    /// <summary>
    /// A provider nobody has configured is the ordinary state rather than a
    /// fault, so it costs a line and not the run.
    /// </summary>
    [Fact]
    public async Task All_carries_on_past_a_provider_with_no_key()
    {
        Keyed("TWO_KEY");

        var (code, said, complained) = await Probe(new Options { Provider = "all" });

        code.ShouldBe(Exit.Ok);
        complained.ShouldContain("No key for One");
        said.ShouldContain("two-flash");
        AssistantSettings.Load(path).Of("two").Text(Survey.Key).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task All_fails_when_not_one_of_them_could_be_asked()
    {
        var (code, _, complained) = await Probe(new Options { Provider = "all" });

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("No key for One");
        complained.ShouldContain("No key for Two");
    }

    /// <summary>
    /// The word is only a word. A plugin that took the name is a real thing
    /// somebody can point at, and pointing at it should reach it.
    /// </summary>
    [Fact]
    public async Task A_provider_actually_called_all_wins_the_name()
    {
        Keyed("ALL_KEY");

        var (code, said, _) = await Probe(new Options { Provider = "all" }, new Surveyable("all", "ALL_KEY"));

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("all-flash");
    }

    [Fact]
    public async Task A_provider_that_cannot_be_surveyed_says_so()
    {
        var (code, _, complained) = await Probe(
            new Options { Provider = "mute" },
            new Unsurveyable());

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("cannot be asked what it offers");
    }

    [Fact]
    public async Task A_missing_key_names_the_variable_to_set()
    {
        var (code, _, complained) = await Probe(new Options { Provider = "one" });

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("ONE_KEY");
    }

    [Fact]
    public async Task Asked_not_to_write_it_down_it_does_not()
    {
        Keyed("ONE_KEY");

        var (code, said, _) = await Probe(new Options { Provider = "one", Dry = true });

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("one-flash");
        AssistantSettings.Load(path).Of("one").Text(Survey.Key).ShouldBeEmpty();
    }

    [Fact]
    public async Task Keys_says_where_each_would_come_from_and_asks_nothing()
    {
        Keyed("ONE_KEY");

        var provider = new Surveyable("one", "ONE_KEY");

        var (code, said, _) = await Probe(new Options { Keys = true }, provider, new Surveyable("two", "TWO_KEY"));

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("one — from ONE_KEY");
        said.ShouldContain("two — none — set TWO_KEY");
        provider.Asked.ShouldBeFalse();
    }

    [Fact]
    public async Task Keys_names_a_provider_that_could_not_be_asked_anyway()
    {
        var (_, said, _) = await Probe(new Options { Keys = true }, new Unsurveyable());

        said.ShouldContain("cannot be surveyed");
    }

    private void Keyed(params string[] names)
    {
        foreach (var name in names)
        {
            Environment.SetEnvironmentVariable(name, "a-key");
            variables.Add(name);
        }
    }

    private void Settled(string provider)
    {
        var settings = new AssistantSettings { Provider = provider };

        settings.Save(path);
    }

    private async Task<(int Code, string Said, string Complained)> Probe(
        Options options,
        params IPatchAssistant[] assistants)
    {
        var said = new StringWriter();
        var complained = new StringWriter();

        IReadOnlyList<IPatchAssistant> installed = assistants.Length > 0
            ? assistants
            : [new Surveyable("one", "ONE_KEY"), new Surveyable("two", "TWO_KEY")];

        var plugins = new PluginCatalog(
            [], [], NodeCatalog.BuiltIn, Presets.All, [], installed);

        var code = await ProbeCommand.Run(
            plugins,
            new ProbeOptions(options.Provider, [], false, false, options.Dry, false, options.Keys),
            said,
            complained,
            CancellationToken.None,
            path);

        return (code, said.ToString(), complained.ToString());
    }

    /// <summary>Named rather than positional, because most of these vary one thing.</summary>
    private sealed record Options
    {
        public string? Provider { get; init; }

        public bool Dry { get; init; }

        public bool Keys { get; init; }
    }

    /// <summary>
    /// An assistant that answers a survey from nothing at all, so that what is
    /// under test is which providers were asked rather than what any endpoint
    /// said.
    /// </summary>
    private sealed class Surveyable(string id, string variable) : IPatchAssistant, IModelSurvey
    {
        public string Id => id;

        public string Name => char.ToUpperInvariant(id[0]) + id[1..];

        public int Priority => 0;

        public bool Asked { get; private set; }

        public AssistantCredential Credential => new(variable, "A key from somewhere.");

        public IReadOnlyList<AssistantField> Form(AssistantValues values) => [];

        public AssistantSenses Senses(AssistantValues values) => new(false, Listener.None);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            throw new NotSupportedException("Nothing here builds a patch.");

        public Task<IReadOnlyList<ModelReport>> Survey(
            AssistantConfig config,
            SurveyOptions options,
            IProgress<string>? said = null,
            CancellationToken cancel = default)
        {
            Asked = true;

            IReadOnlyList<ModelReport> found = [new ModelReport($"{id}-flash") { Hearing = true }];

            said?.Report($"{id}-flash: sees, hears");

            return Task.FromResult(found);
        }
    }

    /// <summary>One that speaks to something with no way of being asked.</summary>
    private sealed class Unsurveyable : IPatchAssistant
    {
        public string Id => "mute";

        public string Name => "Mute";

        public int Priority => 0;

        public AssistantCredential Credential => new("MUTE_KEY", "A key from somewhere.");

        public IReadOnlyList<AssistantField> Form(AssistantValues values) => [];

        public AssistantSenses Senses(AssistantValues values) => new(false, Listener.None);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            throw new NotSupportedException("Nothing here builds a patch.");
    }
}
