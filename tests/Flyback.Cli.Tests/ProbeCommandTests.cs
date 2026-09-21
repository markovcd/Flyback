using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
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

    /// <summary>
    /// A survey reports as it goes, and what it reported is part of what the command
    /// wrote: it is all there once the command has returned, rather than turning up
    /// afterwards whenever a thread pool gets to it.
    /// </summary>
    [Fact]
    public async Task What_a_survey_says_as_it_goes_is_written_before_it_returns()
    {
        Keyed("ONE_KEY");

        var (_, said, _) = await Probe(new Options { Provider = "one", Dry = true });

        said.ShouldContain("one-flash: sees, hears");
    }

    // --- the question before it spends anything -------------------------------

    /// <summary>
    /// The command bills somebody the moment it starts, so it says what that
    /// means and waits. A yes gets exactly what a yes has always got.
    /// </summary>
    [Fact]
    public async Task It_says_what_a_probe_costs_and_waits_for_a_yes()
    {
        Keyed("ONE_KEY");

        var (code, said, _) = await Probe(new Options { Provider = "one", Yes = false, Answer = "y" });

        said.ShouldContain("billed");
        said.ShouldContain("Go ahead?");

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("one-flash");
        AssistantSettings.Load(path).Of("one").Text(Survey.Key).ShouldNotBeEmpty();
    }

    /// <summary>
    /// Nothing is sent, which is the whole of what a no means here — not that the
    /// answer was thrown away after the requests had been paid for.
    /// </summary>
    [Fact]
    public async Task A_no_asks_the_endpoint_nothing()
    {
        Keyed("ONE_KEY");

        var provider = new Surveyable("one", "ONE_KEY");

        var (code, said, _) = await Probe(
            new Options { Provider = "one", Yes = false, Answer = "n" },
            provider);

        code.ShouldBe(Exit.Failed);
        said.ShouldContain("Nothing was asked.");
        provider.Asked.ShouldBeFalse();
    }

    /// <summary>
    /// A stray keypress and a bare Return are both a no. The money is somebody
    /// else's, so the only thing that spends it is the word.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("maybe")]
    [InlineData("Y E S")]
    public async Task Anything_short_of_yes_is_a_no(string answer)
    {
        Keyed("ONE_KEY");

        var provider = new Surveyable("one", "ONE_KEY");

        var (code, _, _) = await Probe(
            new Options { Provider = "one", Yes = false, Answer = answer },
            provider);

        code.ShouldBe(Exit.Failed);
        provider.Asked.ShouldBeFalse();
    }

    /// <summary>
    /// A script has nobody to answer, and a command that took silence for a yes
    /// would spend money on the strength of nothing having objected.
    /// </summary>
    [Fact]
    public async Task With_nobody_to_ask_it_says_so_rather_than_going_ahead()
    {
        Keyed("ONE_KEY");

        var provider = new Surveyable("one", "ONE_KEY");

        var (code, _, complained) = await Probe(
            new Options { Provider = "one", Yes = false },
            provider);

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("--yes");
        provider.Asked.ShouldBeFalse();
    }

    /// <summary>
    /// Being asked to agree to a charge and then told there was never going to be
    /// one is worse than either sentence on its own.
    /// </summary>
    [Fact]
    public async Task A_probe_that_could_not_run_anyway_is_not_worth_a_question()
    {
        var (code, said, complained) = await Probe(new Options { Provider = "one", Yes = false });

        code.ShouldBe(Exit.Failed);
        said.ShouldNotContain("Go ahead?");
        complained.ShouldContain("No key for One");
    }

    /// <summary>
    /// <c>--keys</c> asks nothing of anybody, so there is nothing to agree to.
    /// </summary>
    [Fact]
    public async Task Keys_is_not_worth_a_question()
    {
        Keyed("ONE_KEY");

        var (code, said, _) = await Probe(new Options { Keys = true, Yes = false });

        code.ShouldBe(Exit.Ok);
        said.ShouldNotContain("Go ahead?");
    }

    /// <summary>
    /// A dry run asks the endpoint everything a real one does and is billed for
    /// all of it. What it skips is the writing down.
    /// </summary>
    [Fact]
    public async Task A_dry_run_is_asked_about_too()
    {
        Keyed("ONE_KEY");

        var provider = new Surveyable("one", "ONE_KEY");

        var (code, said, _) = await Probe(
            new Options { Provider = "one", Dry = true, Yes = false, Answer = "n" },
            provider);

        code.ShouldBe(Exit.Failed);
        said.ShouldContain("Go ahead?");
        provider.Asked.ShouldBeFalse();
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
            new ProbeOptions(options.Provider, [], false, false, options.Dry, false, options.Keys, options.Yes),
            said,
            complained,
            CancellationToken.None,
            path,
            options.Answer is null ? null : new StringReader(options.Answer));

        return (code, said.ToString(), complained.ToString());
    }

    /// <summary>Named rather than positional, because most of these vary one thing.</summary>
    private sealed record Options
    {
        public string? Provider { get; init; }

        public bool Dry { get; init; }

        public bool Keys { get; init; }

        /// <summary>
        /// Past the question by default: what nearly all of these are about is
        /// which provider gets asked, and the few about the question itself turn
        /// this off and answer it.
        /// </summary>
        public bool Yes { get; init; } = true;

        /// <summary>
        /// What somebody types at the question. Null is nobody there at all,
        /// which is a pipe or a script rather than an empty answer.
        /// </summary>
        public string? Answer { get; init; }
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

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public AssistantSenses Senses(SettingValues values) => new(false);

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

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public AssistantSenses Senses(SettingValues values) => new(false);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            throw new NotSupportedException("Nothing here builds a patch.");
    }
}
