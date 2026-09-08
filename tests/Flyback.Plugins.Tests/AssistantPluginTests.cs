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
        assistant.Credential.EnvironmentVariable.ShouldNotBeNullOrWhiteSpace();
        assistant.Form(AssistantValues.None).ShouldNotBeEmpty();
        assistant.Unavailable(AssistantConfig.Unset).ShouldBeNull();
    }

    /// <summary>
    /// A form comes back across the boundary as the shapes this side declared,
    /// which is what makes it drawable at all.
    /// </summary>
    /// <remarks>
    /// The identity check above in miniature, and the one that would fail
    /// silently: a plugin's <see cref="AssistantField"/> resolving to a second
    /// copy of the type would leave every pattern match here falling through to
    /// a row that never gets drawn.
    /// </remarks>
    [Fact]
    public void A_declared_form_crosses_the_boundary_as_the_shapes_it_was_written_as()
    {
        var form = Rehearsed.Form(AssistantValues.None);

        form.ShouldContain(field => field is AssistantField.Pick);
        form.ShouldContain(field => field is AssistantField.Switch);
        form.Select(field => field.Key).Distinct().Count().ShouldBe(form.Count);
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
        using var session = Rehearsed.Start(workbench, AssistantConfig.Unset);

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

        assistant.Credential.EnvironmentVariable.ShouldBe("OPENAI_API_KEY");

        // The endpoint is a field here rather than a constant, which is what
        // makes one adapter reach a dozen providers and a local runtime. Asked
        // of the declared form, because that is all anybody out here can see of
        // it.
        var endpoint = assistant.Form(AssistantValues.None)
            .OfType<AssistantField.Text>()
            .ShouldHaveSingleItem();

        endpoint.Enabled.ShouldBeTrue();
        endpoint.Fallback.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Answered from the configuration alone. Nothing here opens a connection,
    /// which is what lets the panel say what is missing before anybody has paid
    /// for finding out.
    /// </summary>
    [Theory]
    [InlineData("", "gpt-4o", null, "key")]
    [InlineData("sk-something", "gpt-4o", "not-an-address", "http")]
    public void What_is_missing_is_said_without_asking_anybody(
        string key,
        string model,
        string? baseUrl,
        string expected)
    {
        var excuse = OpenAi.Unavailable(Configured(key, model, baseUrl));

        excuse.ShouldNotBeNull();
        excuse.ShouldContain(expected);
    }

    [Fact]
    public void A_complete_configuration_has_nothing_missing()
    {
        OpenAi.Unavailable(Configured("sk-something", "gpt-4o")).ShouldBeNull();
    }

    /// <summary>
    /// A configuration as the panel hands one over: a key, and the answers to
    /// whatever the provider said it wanted, under the provider's own names.
    /// </summary>
    /// <remarks>
    /// An empty model is not among the cases above and cannot be: a setting
    /// nobody has answered falls back to what the schema declared, so a provider
    /// with a default has one whatever the file says. The guard for it is still
    /// in the adapter, for a schema that declares no default.
    /// </remarks>
    private static AssistantConfig Configured(string key, string model, string? baseUrl = null)
    {
        var values = new Dictionary<string, string> { [AssistantSchema.ModelKey] = model };

        if (baseUrl is not null) values[AssistantSchema.EndpointKey] = baseUrl;

        return new AssistantConfig(key, new AssistantValues(values));
    }

    /// <summary>
    /// The guard for ADR-0034, on the route ADR-0069 opened. A provider declares
    /// its own settings now and the App writes the answers to a file in plain
    /// text, so a field named like a credential is a credential on disk.
    /// </summary>
    /// <remarks>
    /// Every assistant that is actually installed, asked what it wants: the
    /// pressure to "just declare an apiKey field" will be real and will look
    /// harmless, and this is what says no. The key has a box of its own that the
    /// host owns and no plugin can reach.
    /// </remarks>
    [Fact]
    public void Nothing_a_provider_asks_for_could_hold_a_secret()
    {
        string[] suspicious = ["key", "secret", "token", "password", "credential"];

        foreach (var assistant in Loaded.Assistants)
        foreach (var field in assistant.Form(AssistantValues.None))
        foreach (var word in suspicious)
        {
            field.Key.Contains(word, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                $"{assistant.Id} declares '{field.Key}', which looks like somewhere a credential "
                + "would end up — and these answers are written out in plain text.");
        }
    }

    [Fact]
    public async Task A_run_that_goes_wrong_is_an_event_rather_than_an_exception()
    {
        var workbench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch());
        using var session = Rehearsed.Start(workbench, AssistantConfig.Unset);

        var events = new List<PatchEvent>();
        await foreach (var happened in session.Ask("please fail", CancellationToken.None))
            events.Add(happened);

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem();
        events.OfType<PatchEvent.Proposed>().ShouldBeEmpty();
        workbench.HasProposal.ShouldBeFalse();
    }
}
