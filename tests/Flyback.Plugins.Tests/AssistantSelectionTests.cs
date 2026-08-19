using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Which assistant is offered, given several installed. Decided entirely from
/// what each says about itself — nothing here opens a connection or needs a key.
/// </summary>
public class AssistantSelectionTests
{
    private static PluginCatalog CatalogOf(params IPatchAssistant[] assistants) =>
        new([], [], Core.Graph.NodeCatalog.BuiltIn, Core.Graph.Presets.All, [], assistants);

    [Fact]
    public void Nothing_installed_means_nothing_to_ask()
    {
        CatalogOf().PreferredAssistant.ShouldBeNull();
    }

    [Fact]
    public void The_highest_priority_assistant_wins()
    {
        var catalog = CatalogOf(
            new FakeAssistant("generic", Priority: 0),
            new FakeAssistant("native", Priority: 100));

        catalog.PreferredAssistant!.Id.ShouldBe("native");
    }

    [Fact]
    public void Equal_priorities_break_on_id_so_the_choice_is_repeatable()
    {
        var catalog = CatalogOf(new FakeAssistant("zeta"), new FakeAssistant("alpha"));

        catalog.PreferredAssistant!.Id.ShouldBe("alpha");
    }

    /// <summary>
    /// Unlike a sound backend, an assistant that cannot run yet is still the one
    /// to show. "No key set" is a sentence somebody can act on, and a panel that
    /// hid the assistant would leave them nothing to act on it with.
    /// </summary>
    [Fact]
    public void An_assistant_with_nothing_configured_is_still_the_one_offered()
    {
        var catalog = CatalogOf(new FakeAssistant("claude", Excuse: "set a key first."));

        catalog.PreferredAssistant!.Id.ShouldBe("claude");
        catalog.PreferredAssistant!.Unavailable(Nothing).ShouldBe("set a key first.");
    }

    [Fact]
    public void An_assistant_can_be_found_by_the_id_a_setting_names()
    {
        var catalog = CatalogOf(new FakeAssistant("alpha"), new FakeAssistant("zeta"));

        catalog.Assistant("zeta")!.Id.ShouldBe("zeta");
        catalog.Assistant("gone").ShouldBeNull();
    }

    private static AssistantConfig Nothing => new(string.Empty, "some-model");

    private sealed record FakeAssistant(string Id, int Priority = 0, string? Excuse = null) : IPatchAssistant
    {
        public string Name => Id;

        public AssistantSchema Schema { get; } =
            new("some-model", [new AssistantModel("some-model")], "SOME_KEY", "somewhere");

        public string? Unavailable(AssistantConfig config) => Excuse;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            throw new NotSupportedException("this one is only ever asked about itself.");
    }
}
