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

    private static readonly ModuleCatalog Shipped = PluginHost.Load().Modules;

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
