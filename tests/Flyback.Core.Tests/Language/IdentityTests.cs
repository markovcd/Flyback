using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// A module built from text is called after the piece of source that made it, so
/// building the same text twice gives the same patch down to the guids.
/// </summary>
/// <remarks>
/// Before this, every build minted fresh ids and the patch it produced was a
/// stranger to the one it replaced — nothing could say which module had stayed
/// the same, so a rebuild lost every canvas position, every selection, and every
/// accumulator that was mid-cycle. The workbench's own tool description already
/// named the cost: writing a patch afresh "gives every module a new identity and
/// loses where they sit on the canvas".
/// <para>
/// Names rather than positions is what makes an edit local: a line added at the
/// top must not rename what is at the bottom, or a patch being typed into would
/// restart from the cursor down on every keystroke.
/// </para>
/// </remarks>
public class IdentityTests
{
    private static Patch Build(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static Dictionary<Guid, string> Modules(Patch patch) =>
        patch.Nodes.ToDictionary(node => node.Id, node => node.TypeId);

    /// <summary>The id of the one module of that type, which the patch must have exactly one of.</summary>
    private static Guid Only(Patch patch, string typeId) =>
        patch.Nodes.Single(node => node.TypeId == typeId).Id;

    private const string Plasma = """
        let slowly = t * 0.2

        x |> sine(freq: 1.5)
          |> add(y |> sine(freq: 1.1, phase: slowly))
          |> remap(-2..2, 0..1)
          |> hsv(saturation: 0.85, value: 1)
          |> out.color
        """;

    [Fact]
    public void The_same_source_builds_the_same_modules_every_time()
    {
        Modules(Build(Plasma)).ShouldBe(Modules(Build(Plasma)));
    }

    /// <summary>
    /// And across runs, not merely within one — a name is hashed rather than
    /// counted, so nothing here depends on the order this process happened to do
    /// things in.
    /// </summary>
    [Fact]
    public void A_module_is_named_by_its_source_and_not_by_a_counter()
    {
        var first = Build("let hum = t |> sine(freq: 55) \nhum |> out.left");
        var second = Build("let hum = t |> sine(freq: 55) \nhum |> out.left");

        var hum = first.Nodes.Single(n => n.TypeId == "osc.sine").Id;

        second.Nodes.Single(n => n.TypeId == "osc.sine").Id.ShouldBe(hum);
    }

    /// <summary>
    /// The one that matters for editing: adding a line leaves everything the
    /// line did not touch exactly as it was.
    /// </summary>
    [Fact]
    public void Adding_a_statement_renames_nothing_that_was_already_there()
    {
        var before = Build(Plasma);
        var after = Build("let hum = t |> sine(freq: 55)\nhum |> out.left\n\n" + Plasma);

        foreach (var (id, type) in Modules(before))
            after.Find(id).ShouldNotBeNull($"the {type} should have survived the edit").TypeId.ShouldBe(type);
    }

    /// <summary>
    /// Including at the top, which is where a counter would have gone wrong: a
    /// statement inserted above everything renumbers every statement below it,
    /// and names do not care.
    /// </summary>
    [Fact]
    public void A_statement_inserted_above_everything_moves_nothing_below_it()
    {
        var before = Build(Plasma);
        var after = Build("let unused = t * 3\n" + Plasma);

        Modules(before).Keys.ShouldBeSubsetOf(Modules(after).Keys);
    }

    /// <summary>
    /// A name is a place in the source rather than a kind of module, so swapping
    /// one module for another in the same place keeps the place.
    /// </summary>
    /// <remarks>
    /// Worth having rather than merely tolerable. Changing a sine to a saw is
    /// changing the waveform of something that is already sounding, and what a
    /// player wants from that is the same note in a different colour — which is
    /// exactly what carrying the accumulator over gives. Restarting the phase
    /// would be a click in the middle of a held note.
    /// </remarks>
    [Fact]
    public void Swapping_one_module_for_another_keeps_the_place_it_stood_in()
    {
        var before = Build("let hum = t |> sine(freq: 55)\nhum |> out.left");
        var after = Build("let hum = t |> saw(freq: 55)\nhum |> out.left");

        var was = before.Nodes.Single(n => n.TypeId == "osc.sine").Id;

        after.Find(was).ShouldNotBeNull().TypeId.ShouldBe("osc.saw");
    }

    /// <summary>
    /// And a statement that grows a stage renames what comes after it in that
    /// statement — an edit is local to a line, not free within one.
    /// </summary>
    [Fact]
    public void A_stage_added_mid_pipeline_renames_the_rest_of_its_own_statement()
    {
        var before = Build("t |> sine(freq: 55) |> gain(0.5) |> out.left");
        var after = Build("t |> sine(freq: 55) |> add(0.1) |> gain(0.5) |> out.left");

        Only(after, "color.gain").ShouldNotBe(Only(before, "color.gain"));

        // The oscillator ahead of the insertion is untouched, so it is only the
        // rest of the line that begins again and not the line.
        Only(after, "osc.sine").ShouldBe(Only(before, "osc.sine"));
    }

    /// <summary>
    /// The Output is the one module every patch has, and every rebuild has to
    /// recognise it as the same one — it carries the gain and the scan settings,
    /// and a patch that lost it would be a patch whose canvas moved on every
    /// evaluation.
    /// </summary>
    [Fact]
    public void The_output_is_the_same_output_every_time()
    {
        var first = Build(Plasma).FirstOf(NodeCatalog.OutputTypeId).ShouldNotBeNull();
        var second = Build(Plasma).FirstOf(NodeCatalog.OutputTypeId).ShouldNotBeNull();

        second.Id.ShouldBe(first.Id);
    }

    /// <summary>
    /// There is one clock in a patch however many lines reach for it, and moving
    /// the first mention should not make it a different one.
    /// </summary>
    [Fact]
    public void The_clock_is_one_module_wherever_it_is_first_mentioned()
    {
        var first = Build("let a = t * 2\nlet b = t * 3\na |> add(b) |> out.left");
        var second = Build("let b = t * 3\nlet a = t * 2\na |> add(b) |> out.left");

        first.Nodes.Count(n => n.TypeId == NodeCatalog.TimeTypeId).ShouldBe(1);

        first.FirstOf(NodeCatalog.TimeTypeId).ShouldNotBeNull().Id
            .ShouldBe(second.FirstOf(NodeCatalog.TimeTypeId).ShouldNotBeNull().Id);
    }

    /// <summary>
    /// Two names for two different things. A def is stamped out at each call, so
    /// two calls must place two sets of modules — and if they shared a name they
    /// would share an accumulator on the next rebuild.
    /// </summary>
    [Fact]
    public void Two_stampings_of_one_def_are_two_different_modules()
    {
        var patch = Build("""
            def voice(pitch) = t |> sine(freq: pitch)

            let low = voice(55)
            let high = voice(880)

            low |> add(high) |> out.left
            """);

        patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);
        patch.Nodes.Select(n => n.Id).Distinct().Count().ShouldBe(patch.Nodes.Count);
    }

    [Fact]
    public void Two_stampings_in_one_statement_are_two_different_modules_as_well()
    {
        var patch = Build("""
            def voice(pitch) = t |> sine(freq: pitch)

            voice(55) |> add(voice(880)) |> out.left
            """);

        patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);
        patch.Nodes.Select(n => n.Id).Distinct().Count().ShouldBe(patch.Nodes.Count);
    }

    /// <summary>
    /// Two pipelines with nothing to be called by are told apart by where they
    /// stand, and by nothing else — so this is the one case where a name is a
    /// number, and it still has to be a different number.
    /// </summary>
    [Fact]
    public void Modules_placed_in_one_patch_never_share_a_name()
    {
        var patch = Build(Plasma);

        patch.Nodes.Select(n => n.Id).Distinct().Count().ShouldBe(patch.Nodes.Count);
    }

    // --- what it is all for -------------------------------------------------

    /// <summary>
    /// Both halves of the work together, which is the thing live coding needs
    /// and neither half gives on its own: a patch is playing, a line is added to
    /// its source, the whole patch is rebuilt from text — and the tone that was
    /// already sounding does not notice.
    /// </summary>
    /// <remarks>
    /// Without stable names the rebuilt patch is a stranger and nothing can be
    /// carried over. Without ownership on the cells there is nothing to carry it
    /// by. With both, a rebuild costs only the modules whose source changed.
    /// </remarks>
    [Fact]
    public void A_patch_rebuilt_from_edited_source_keeps_playing()
    {
        const string before = "let hum = t |> sine(freq: 220)\nhum |> out.left";
        const string after = before + "\n\nlet high = t |> sine(freq: 700)\nhigh |> out.right";

        var renderer = new AudioRenderer();
        var first = Build(before).CompileForAudio().Program;
        var memory = renderer.DelayMemoryFor(first);

        renderer.Render(first, new float[960], AudioScan.TimeDriven, memory);

        var second = Build(after).CompileForAudio().Program;
        var carried = renderer.DelayMemoryFor(second, memory).ShouldNotBeNull();

        var hum = Build(after).Nodes.Single(n => n.Name == "hum").Id;
        var cell = carried.Owners.Phases.ToList().IndexOf(hum);

        cell.ShouldBeGreaterThanOrEqualTo(0, "the rebuilt patch should still know which module is hum");

        // It has an evaluation behind it, so it takes a step. A cell that had
        // been thrown away and made again would answer nought.
        carried.Advance(cell, input: 5.25d, frequency: 1d).ShouldNotBe(0d);
    }
}
