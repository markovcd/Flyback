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
