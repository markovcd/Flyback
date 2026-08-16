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
        excuse.ShouldContain(expected, Case.Insensitive);
    }

    [Fact]
    public void A_complete_configuration_has_nothing_missing()
    {
        OpenAi.Unavailable(new AssistantConfig("sk-something", "gpt-4o")).ShouldBeNull();
    }

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
