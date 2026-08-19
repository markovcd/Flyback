using System.Runtime.Loader;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// An assistant loaded off disk, the way a real one will be. What is worth
/// pinning is the thing that would otherwise fail silently and late: that
/// <see cref="IPatchAssistant"/>, <see cref="PatchWorkbench"/> and
/// <see cref="PatchEvent"/> are the same types on both sides of the plugin's
/// own <see cref="AssemblyLoadContext"/>.
/// </summary>
public class AssistantPluginTests
{
    private static PluginCatalog Loaded => PluginHost.Load();

    private static IPatchAssistant Rehearsed =>
        Loaded.Assistants.Single(a => a.Id == "rehearsed");

    [Fact]
    public void An_assistant_in_a_plugin_reaches_the_catalogue()
    {
        Loaded.Assistants.Select(a => a.Id).ShouldContain("rehearsed");
    }

    [Fact]
    public void The_contract_keeps_one_identity_across_the_boundary()
    {
        var assistant = Rehearsed;
        var mine = AssemblyLoadContext.GetLoadContext(typeof(IPatchAssistant).Assembly);
        var theirs = AssemblyLoadContext.GetLoadContext(assistant.GetType().Assembly);

        // The plugin is isolated...
        theirs.ShouldNotBe(AssemblyLoadContext.Default);
        theirs.ShouldNotBe(mine);

        // ...and yet its assistant is the interface this assembly declared,
        // because the contract itself is host-owned and never loaded twice.
        typeof(IPatchAssistant).IsInstanceOfType(assistant).ShouldBeTrue();
        mine.ShouldBe(AssemblyLoadContext.Default);
    }

    [Fact]
    public void It_is_answerable_about_itself_without_being_started()
    {
        var assistant = Rehearsed;

        assistant.Name.ShouldNotBeNullOrWhiteSpace();
        assistant.Schema.DefaultModel.ShouldNotBeNullOrWhiteSpace();
        assistant.Schema.EnvironmentVariable.ShouldNotBeNullOrWhiteSpace();
        assistant.Unavailable(new AssistantConfig(string.Empty, "rehearsal")).ShouldBeNull();
    }

    /// <summary>
    /// The whole path, from out there: a plugin drives the host's workbench with
    /// nothing but the contract, and a patch comes back that the editor could
    /// take as it stands.
    /// </summary>
    [Fact]
    public async Task An_assistant_in_a_plugin_can_build_a_patch()
    {
        var workbench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch());
        using var session = Rehearsed.Start(workbench, new AssistantConfig(string.Empty, "rehearsal"));

        var events = new List<PatchEvent>();
        await foreach (var happened in session.Ask("something grey", CancellationToken.None))
            events.Add(happened);

        events.ShouldNotContain(e => e is PatchEvent.Failed);
        events.OfType<PatchEvent.Saw>().ShouldNotBeEmpty();

        var proposed = events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();

        proposed.Summary.ShouldBe("a flat grey field");
        proposed.Patch.Nodes.Count.ShouldBe(2);
        proposed.Patch.CompileForVideo(NodeCatalog.BuiltIn).Issues.ShouldBeEmpty();
    }

    // --- the real one, loaded off disk --------------------------------------

    private static IPatchAssistant OpenAi =>
        Loaded.Assistants.Single(a => a.Id == "openai");

    [Fact]
    public void The_chat_completions_assistant_reaches_the_catalogue()
    {
        var assistant = OpenAi;

        assistant.Schema.BaseUrlEditable.ShouldBeTrue();
        assistant.Schema.DefaultBaseUrl.ShouldNotBeNullOrWhiteSpace();
        assistant.Schema.EnvironmentVariable.ShouldBe("OPENAI_API_KEY");
    }

    /// <summary>
    /// Answered from the configuration alone. Nothing here opens a connection,
    /// which is what lets the panel say what is missing before anybody has paid
    /// for finding out.
    /// </summary>
    [Theory]
    [InlineData("", "gpt-4o", null, "key")]
    [InlineData("sk-something", "", null, "model")]
    [InlineData("sk-something", "gpt-4o", "not-an-address", "http")]
    public void What_is_missing_is_said_without_asking_anybody(
        string key,
        string model,
        string? baseUrl,
        string expected)
    {
        var excuse = OpenAi.Unavailable(new AssistantConfig(key, model, baseUrl));

        excuse.ShouldNotBeNull();
        excuse.ShouldContain(expected);
    }

    [Fact]
    public void A_complete_configuration_has_nothing_missing()
    {
        OpenAi.Unavailable(new AssistantConfig("sk-something", "gpt-4o")).ShouldBeNull();
    }

    /// <summary>
    /// The default has to be one of the suggestions, or the box opens on a name
    /// its own list does not contain.
    /// </summary>
    [Fact]
    public void The_model_it_starts_on_is_one_of_the_ones_it_offers()
    {
        OpenAi.Schema.SuggestedModels.Select(m => m.Id).ShouldContain(OpenAi.Schema.DefaultModel);
        OpenAi.Schema.Known(OpenAi.Schema.DefaultModel).ShouldNotBeNull();
    }

    /// <summary>
    /// A model is an ear or a driver, and here nothing is both. That is not a
    /// rule this enforces — it is what these models are, and it is the whole
    /// reason the sound goes to a second one: driving with an ear would build
    /// blind, and a conversation driven by one is refused on its first turn for
    /// carrying no audio.
    /// </summary>
    [Fact]
    public void Nothing_that_listens_here_can_also_look()
    {
        var ears = OpenAi.Schema.Ears.ToArray();

        ears.ShouldNotBeEmpty("there is nothing to listen with otherwise");
        ears.ShouldAllBe(m => !m.Vision);

        // And the one that drives by default is the other way round.
        OpenAi.Schema.Known(OpenAi.Schema.DefaultModel)!.Vision.ShouldBeTrue();
    }

    /// <summary>
    /// The longest match wins, and this is the pair that makes it matter:
    /// <c>gpt-4o-audio-preview</c> begins with <c>gpt-4o</c>, so a shortest- or
    /// first-match rule would read the audio model as the one model in the list
    /// that would take away the capability it was chosen for.
    /// </summary>
    [Theory]
    [InlineData("gpt-4o", "gpt-4o", true, false)]
    [InlineData("gpt-4o-audio-preview", "gpt-4o-audio-preview", false, true)]
    [InlineData("gpt-audio", "gpt-audio", false, true)]
    [InlineData("llama3.1", "llama3.1", false, false)]
    public void What_a_model_accepts_is_read_off_the_name(
        string typed,
        string expected,
        bool vision,
        bool hearing)
    {
        var known = OpenAi.Schema.Known(typed).ShouldNotBeNull();

        known.Id.ShouldBe(expected);
        known.Vision.ShouldBe(vision);
        known.Hearing.ShouldBe(hearing);
    }

    /// <summary>
    /// A dated snapshot is the model it is a snapshot of. Matching whole names
    /// would make every one of these a stranger, on a form where a stranger
    /// means "you decide" rather than "it can".
    /// </summary>
    [Theory]
    [InlineData("gpt-4o-2024-11-20", "gpt-4o", false)]
    [InlineData("gpt-4o-audio-preview-2024-12-17", "gpt-4o-audio-preview", true)]
    [InlineData("gpt-4o-mini-audio-preview-2024-12-17", "gpt-4o-mini-audio-preview", true)]
    public void A_dated_snapshot_is_recognised_as_what_it_is_a_snapshot_of(
        string typed,
        string expected,
        bool hearing)
    {
        var known = OpenAi.Schema.Known(typed).ShouldNotBeNull();

        known.Id.ShouldBe(expected);
        known.Hearing.ShouldBe(hearing);
    }

    /// <summary>
    /// Null is "nobody here knows", not "it cannot" — the endpoint is a field,
    /// so most of what this reaches was never written down here.
    /// </summary>
    /// <remarks>
    /// The middle three are the ones this rule exists for. Every one of them
    /// begins with the name of a model that <em>is</em> written down, and not
    /// one of them is that model: a bare prefix match would answer for all
    /// three, and would answer wrongly in the direction that takes a switch away
    /// from somebody who knows better than this list does.
    /// </remarks>
    [Theory]
    [InlineData("mistral-large")]
    [InlineData("gpt-4o-transcribe")]
    [InlineData("gpt-4o-realtime-preview")]
    [InlineData("gpt-4o-search-preview")]
    [InlineData("")]
    [InlineData(null)]
    public void A_model_nobody_wrote_down_is_a_stranger_rather_than_a_refusal(string? typed) =>
        OpenAi.Schema.Known(typed).ShouldBeNull();

    [Fact]
    public async Task A_run_that_goes_wrong_is_an_event_rather_than_an_exception()
    {
        var workbench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch());
        using var session = Rehearsed.Start(workbench, new AssistantConfig(string.Empty, "rehearsal"));

        var events = new List<PatchEvent>();
        await foreach (var happened in session.Ask("please fail", CancellationToken.None))
            events.Add(happened);

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem();
        events.OfType<PatchEvent.Proposed>().ShouldBeEmpty();
        workbench.HasProposal.ShouldBeFalse();
    }
}
