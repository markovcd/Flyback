using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// How much of the catalogue's prose the assistant is told, and the list of
/// modules that are told about whatever it costs.
/// </summary>
public class ProsePolicyTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-priority-" + Guid.NewGuid().ToString("N"));

    private string ListFile => Path.Combine(folder, "priority-modules.txt");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static readonly PluginCatalog Everything = PluginHost.Load();

    private static ModuleCatalog Shipped => Everything.Modules;

    /// <summary>
    /// Room enough for the built-ins without their descriptions and a few thousand
    /// characters of them, which is a catalogue past its budget.
    /// </summary>
    private static readonly ProsePolicy Tight = new(30_000, new HashSet<string> { "osc.string", "scan" });

    private static PatchWorkbench Bench(ProsePolicy prose) =>
        new(NodeCatalog.BuiltIn, new Patch(), hearing: Listener.Another, prose: prose);

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
    /// the presets rather than with the catalogue it was written against.
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
    /// Every preset stays in the list by name, and a description that has no room is
    /// left out and said so, the way a module's is.
    /// </summary>
    [Fact]
    public void Past_the_budget_the_presets_lose_their_descriptions_and_say_so()
    {
        var bench = Bench(new ProsePolicy(1, new HashSet<string>()));

        bench.Briefing.ShouldContain("Some presets below have no description line");
        bench.Briefing.ShouldContain(Environment.NewLine + "Plasma" + Environment.NewLine);
        bench.Briefing.ShouldNotContain("Two sine fields crossed");
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
