using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// How much of the catalog's prose the assistant is told, and the list of
/// modules that are told about whatever it costs.
/// </summary>
public sealed class ProsePolicyTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-priority-" + Guid.NewGuid().ToString("N"));

    private string ListFile => Path.Combine(folder, "priority-modules.txt");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static readonly PluginCatalog Everything = PluginHost.Load();

    private static ModuleCatalog Shipped => Everything.Modules;

    private static readonly IReadOnlySet<string> Listed = new HashSet<string> { "osc.string", "scan" };

    /// <summary>The briefing with nothing cut that can be: what no budget gets it under.</summary>
    private static readonly int Floor = Bench(new ProsePolicy(1, Listed)).Briefing.Length;

    /// <summary>
    /// Room enough for the built-ins without their descriptions and a few thousand
    /// characters of them, which is a catalog past its budget.
    /// </summary>
    private static readonly ProsePolicy Tight = new(Floor + Handbook.PresetsReserve + 3_000, Listed);

    private static PatchWorkbench Bench(ProsePolicy prose, IReadOnlyList<PatchPreset>? presets = null) =>
        new(NodeCatalog.BuiltIn, new Patch(), hearing: Listener.Another, prose: prose, presets: presets);

    /// <summary>Far more presets than the room set aside for them has names for.</summary>
    private static readonly PatchPreset[] Saved =
    [
        .. Enumerable.Range(1, 600).Select(i => new PatchPreset($"Saved patch {i:000}", _ => new Patch(), "Something somebody kept.")),
    ];

    /// <summary>
    /// From where the modules' headers alone use up the budget, whatever the modules
    /// leave is all the presets get, and they take no more once their own few hundred
    /// characters of notes are paid for.
    /// </summary>
    [Fact]
    public void Down_to_what_cannot_be_cut_the_briefing_fits_its_budget()
    {
        var modules = Bench(new ProsePolicy(1, Listed), presets: []).Briefing.Length;

        for (var budget = modules + 1_000; budget <= Tight.Budget; budget += 97)
            Bench(new ProsePolicy(budget, Listed), Saved).Briefing.Length.ShouldBeLessThanOrEqualTo(budget, $"a budget of {budget}");
    }

    /// <summary>
    /// The presets' share is set aside before anybody's saved presets are counted, so
    /// a long list keeps the names that fit and says the rest are there.
    /// </summary>
    [Fact]
    public void Past_the_room_set_aside_for_presets_their_names_are_cut_too()
    {
        var bench = Bench(Tight, Saved);

        bench.Briefing.Length.ShouldBeLessThanOrEqualTo(Tight.Budget);
        bench.Briefing.ShouldContain("Not every preset is named below");
        bench.Briefing.ShouldContain(Environment.NewLine + "Saved patch 001" + Environment.NewLine);
        bench.Briefing.ShouldNotContain("Saved patch 600");
    }

    /// <summary>
    /// Every module that ships is described to the assistant, with room to spare
    /// for somebody's plugins.
    /// </summary>
    [Fact]
    public void What_ships_is_described_in_full_under_the_default_budget()
    {
        ProsePolicy.Default.Undescribed(Shipped).ShouldBeEmpty();

        var bench = new PatchWorkbench(Shipped, new Patch());

        bench.Briefing.Length.ShouldBeLessThan(AssistantSettings.DefaultProseBudget * 4 / 5);
        bench.Tools.Select(t => t.Name).ShouldNotContain("describe_module", "there is nothing to look up");
    }

    [Fact]
    public void Every_module_the_shipped_list_names_ships()
    {
        foreach (var id in PriorityModules.Parse(PriorityModules.Shipped))
            Shipped.Get(id).ShouldNotBeNull($"the priority list names '{id}', which no shipped module is");
    }

    /// <summary>
    /// A module five or more of the shipped presets use is common enough that the
    /// assistant is told what it is whatever the budget, so the list keeps up with
    /// the presets rather than with the catalog it was written against.
    /// </summary>
    [Fact]
    public void Every_module_the_presets_lean_on_is_on_the_shipped_list()
    {
        var list = PriorityModules.Parse(PriorityModules.Shipped);

        var uses = Everything.Presets
            .SelectMany(preset => preset.Build(Shipped).Nodes.Select(node => node.TypeId).Distinct(StringComparer.Ordinal))
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(module => module.Count() >= 5)
            .Where(module => Shipped.Get(module.Key) is { Description.Length: > 0 } def && !ExpressionFusion.Retired(def));

        foreach (var module in uses)
            list.ShouldContain(module.Key, $"{module.Count()} presets use '{module.Key}' and the priority list leaves it out");
    }

    [Fact]
    public void Past_the_budget_the_priority_modules_keep_their_descriptions()
    {
        var bench = Bench(Tight);

        bench.Undescribed.ShouldNotBeEmpty();
        bench.Undescribed.ShouldNotContain("osc.string");
        bench.Undescribed.ShouldNotContain("scan");

        bench.Briefing.ShouldContain(NodeCatalog.BuiltIn.Require("osc.string").Description);
        bench.Briefing.ShouldContain(NodeCatalog.BuiltIn.Require("scan").Description);
    }

    [Fact]
    public void Within_the_budget_the_presets_are_described()
    {
        var briefing = new PatchWorkbench(Shipped, new Patch()).Briefing;

        briefing.ShouldContain("Plasma | Two sine fields crossed");
        briefing.ShouldNotContain("Some presets below have no description line");
    }

    /// <summary>
    /// Descriptions go before names: a preset keeps its name while a description that
    /// has no room is left out and said so, the way a module's is.
    /// </summary>
    [Fact]
    public void Past_the_budget_the_presets_lose_their_descriptions_and_say_so()
    {
        var names = Presets.All
            .Where(preset => preset.Kind != PresetKind.Blank)
            .Sum(preset => preset.Name.Length + Environment.NewLine.Length);

        var bench = Bench(new ProsePolicy(Floor + names, Listed));

        bench.Briefing.ShouldContain("Some presets below have no description line");
        bench.Briefing.ShouldNotContain("Not every preset is named below");
        bench.Briefing.ShouldContain(Environment.NewLine + "Whole band" + Environment.NewLine);
        bench.Briefing.ShouldNotContain(Presets.All.Single(preset => preset.Name == "Whole band").Description);
        bench.Tools.Select(t => t.Name).ShouldContain("describe_preset");
    }

    [Fact]
    public async Task A_preset_left_out_of_the_list_is_described_when_it_is_read()
    {
        var bench = Bench(new ProsePolicy(1, new HashSet<string>()));

        var read = await bench.InvokeAsync(
            "describe_preset",
            System.Text.Json.JsonDocument.Parse("""{"name":"Plasma"}""").RootElement,
            CancellationToken.None);

        read.Ok.ShouldBeTrue(read.Text);
        read.Text.ShouldContain("Two sine fields crossed");
    }

    [Fact]
    public void Past_the_budget_the_briefing_stays_inside_it_and_says_what_is_missing()
    {
        var bench = Bench(Tight);

        bench.Briefing.Length.ShouldBeLessThanOrEqualTo(Tight.Budget);
        bench.Briefing.ShouldContain("`describe_module` gives it");
        bench.Tools.Select(t => t.Name).ShouldContain("describe_module");
        bench.Tools.Select(t => t.Name).ShouldContain("find_modules");

        // Long enough not to turn up inside some other module's words by chance.
        foreach (var id in bench.Undescribed)
        {
            var description = NodeCatalog.BuiltIn.Require(id).Description;

            if (description.Length > 60) bench.Briefing.ShouldNotContain(description);
        }
    }

    /// <summary>
    /// The room left is filled rather than given up on, so one module too many
    /// costs a description or two and not every one outside the list.
    /// </summary>
    [Fact]
    public void Past_the_budget_what_is_left_of_it_is_still_used()
    {
        var bench = Bench(Tight);

        var outside = NodeCatalog.BuiltIn.All
            .Count(d => d.Description.Length > 0 && !Tight.Priority.Contains(d.TypeId));

        bench.Undescribed.Count.ShouldBeLessThan(outside);
    }

    /// <summary>Somebody who listed more than fits asked for all of it.</summary>
    [Fact]
    public void A_priority_module_keeps_its_description_even_past_the_budget()
    {
        var everyone = NodeCatalog.BuiltIn.All.Select(d => d.TypeId).ToHashSet();

        Bench(new ProsePolicy(AssistantSettings.LeastProse, everyone)).Undescribed.ShouldBeEmpty();
    }

    /// <summary>
    /// Decided the same whoever is listening, since the canvas marks these modules
    /// with no conversation to ask.
    /// </summary>
    [Fact]
    public void What_is_left_out_does_not_depend_on_who_listens()
    {
        var heard = new[] { Listener.None, Listener.Another, Listener.Itself }
            .Select(hearing => new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), hearing: hearing, prose: Tight).Undescribed)
            .ToArray();

        heard[1].SetEquals(heard[0]).ShouldBeTrue();
        heard[2].SetEquals(heard[0]).ShouldBeTrue();
    }

    [Fact]
    public void The_list_is_written_where_there_is_none()
    {
        PriorityModules.Install(ListFile);

        File.ReadAllText(ListFile).ShouldBe(PriorityModules.Shipped);
        PriorityModules.Load(ListFile).ShouldContain("osc.sine");
    }

    [Fact]
    public void A_list_that_is_there_is_never_overwritten()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(ListFile, "# mine\nosc.saw\n");

        PriorityModules.Install(ListFile);

        File.ReadAllText(ListFile).ShouldBe("# mine\nosc.saw\n");
        PriorityModules.Load(ListFile).ShouldBe(new[] { "osc.saw" });
    }

    [Fact]
    public void With_no_file_the_shipped_list_is_read()
    {
        PriorityModules.Load(ListFile).SetEquals(PriorityModules.Parse(PriorityModules.Shipped)).ShouldBeTrue();
    }

    [Fact]
    public void Notes_blank_lines_and_the_space_around_an_id_are_passed_over()
    {
        PriorityModules.Parse("# a note\r\n\r\n  osc.sine  \r\nmath.add\n#math.sub\n")
            .ShouldBe(new[] { "osc.sine", "math.add" }, ignoreOrder: true);
    }
}
