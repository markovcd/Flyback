using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Printing a patch, and reading back what was printed.
/// </summary>
/// <remarks>
/// The round trip is the whole specification of this thing. A printed patch is
/// not the same file — ids are new and the layout is redone — so what has to
/// hold is that it is the same *instrument*: print it, build it, and the two
/// compile to the same program, opcode for opcode and register for register.
/// Every preset in the box is held to that.
/// </remarks>
public class PrinterTests
{
    public static TheoryData<string> Names => [.. Flyback.Core.Graph.Presets.All.Select(p => p.Name)];

    private static Patch Preset(string name) =>
        Flyback.Core.Graph.Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn);

    private static Patch Reread(Patch patch, out string source)
    {
        source = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty($"{load.Report}{Environment.NewLine}--- printed ---{Environment.NewLine}{source}");

        return load.Patch;
    }

    /// <summary>What a program is, for comparing two of them.</summary>
    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    [Theory]
    [MemberData(nameof(Names))]
    public void A_printed_preset_reads_back_as_the_same_instrument(string name)
    {
        var original = Preset(name);
        var again = Reread(original, out var source);

        Fingerprint(again.CompileForVideo(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(original.CompileForVideo(NodeCatalog.BuiltIn).Program),
                $"{name}: the picture{Environment.NewLine}{source}");

        Fingerprint(again.CompileForAudio(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(original.CompileForAudio(NodeCatalog.BuiltIn).Program),
                $"{name}: the sound{Environment.NewLine}{source}");
    }

    /// <summary>
    /// Printing twice gives the same text, which is what makes a diff between
    /// two patches mean something rather than being mostly noise.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Printing_is_the_same_text_every_time(string name)
    {
        var patch = Preset(name);

        PatchPrinter.Print(patch, NodeCatalog.BuiltIn)
            .ShouldBe(PatchPrinter.Print(patch, NodeCatalog.BuiltIn));
    }

    /// <summary>A patch that has been through the language once does not drift on a second pass.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void A_second_trip_changes_nothing(string name)
    {
        var once = Reread(Preset(name), out var first);
        _ = Reread(once, out var second);

        second.ShouldBe(first);
    }

    // --- what it writes -------------------------------------------------------

    [Fact]
    public void The_clock_and_the_coordinates_are_written_as_the_words_for_them()
    {
        var source = PatchPrinter.Print(Preset("Plasma"), NodeCatalog.BuiltIn);

        source.ShouldContain("x |>");
        source.ShouldContain("t |>");
        source.ShouldNotContain("coord(");
        source.ShouldNotContain("time(");
    }

    [Fact]
    public void A_knob_left_at_its_default_is_not_written() =>
        PatchPrinter.Print(Preset("Kaleidoscope"), NodeCatalog.BuiltIn)
            .ShouldNotContain("phase:");

    /// <summary>A name somebody chose is worth keeping, and it comes back as the binding.</summary>
    [Fact]
    public void A_named_module_keeps_its_name()
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.BuiltIn);

        var sine = NodeInstance.Create(NodeCatalog.BuiltIn.Require("osc.sine"), 0, 0);
        sine.Rename(NodeCatalog.BuiltIn.Require("osc.sine"), "carrier");

        patch.Nodes.Add(sine);
        patch.Connect(sine.Id, 0, patch.Output.Id, NodeCatalog.OutputLeftPort);

        PatchPrinter.Print(patch, NodeCatalog.BuiltIn).ShouldContain("let carrier = sine(");
    }

    /// <summary>
    /// A module called something that reads as a pitch cannot be written as a
    /// name, however good it looks on the canvas.
    /// </summary>
    [Fact]
    public void A_name_that_would_read_as_a_note_is_not_used()
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.BuiltIn);

        var def = NodeCatalog.BuiltIn.Require("osc.sine");
        var sine = NodeInstance.Create(def, 0, 0);

        sine.Rename(def, "A3");
        patch.Nodes.Add(sine);

        patch.Connect(sine.Id, 0, patch.Output.Id, NodeCatalog.OutputLeftPort);
        patch.Connect(sine.Id, 0, patch.Output.Id, NodeCatalog.OutputRightPort);

        var source = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        source.ShouldNotContain("let A3 =");
        PatchLanguage.Build(source, NodeCatalog.BuiltIn).Issues.ShouldBeEmpty();
    }

    /// <summary>The two ambiguous short names and the one that reads as a socket are written in full.</summary>
    [Fact]
    public void An_ambiguous_name_is_written_in_full()
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.BuiltIn);

        var hsv = NodeInstance.Create(NodeCatalog.BuiltIn.Require("color.hsv"), 0, 0);

        patch.Nodes.Add(hsv);
        patch.Connect(hsv.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        PatchPrinter.Print(patch, NodeCatalog.BuiltIn).ShouldContain("color.hsv(");
    }

    /// <summary>A duration comes back as the time it means, not as the decade it is stored on.</summary>
    [Fact]
    public void A_duration_is_written_as_a_time() =>
        PatchPrinter.Print(Preset("Two channels"), NodeCatalog.BuiltIn).ShouldContain("ms");

    [Fact]
    public void A_sequence_comes_back_as_a_step_block() =>
        PatchPrinter.Print(Preset("Sequence"), NodeCatalog.BuiltIn).ShouldContain("[ A3 C4 D4 E4 G4 E4 D4 C4 ]");

    [Fact]
    public void A_scale_comes_back_as_its_pitch_classes() =>
        PatchPrinter.Print(Preset("In key"), NodeCatalog.BuiltIn).ShouldContain("[ C D E G A ]");

    /// <summary>The one wire that runs backwards, which no pipeline can say.</summary>
    [Fact]
    public void A_cycle_comes_back_as_a_back_wire()
    {
        var source = PatchPrinter.Print(Preset("Loop"), NodeCatalog.BuiltIn);

        source.ShouldContain("= unit()");
        source.ShouldContain(".in <- ");
    }

    /// <summary>
    /// The file a player names, which no preset in the box sets — so nothing
    /// else here would have noticed it going missing.
    /// </summary>
    [Fact]
    public void A_file_a_module_names_comes_back_with_it()
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.BuiltIn);

        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.SampleTypeId);
        var clip = NodeInstance.Create(def, 0, 0);

        SampleExtra.Set(clip, "drums.wav");
        patch.Nodes.Add(clip);
        patch.Connect(clip.Id, 0, patch.Output.Id, NodeCatalog.OutputLeftPort);

        var source = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        source.ShouldContain("""sample("drums.wav")""");

        var again = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        again.Issues.ShouldBeEmpty(again.Report);
        SampleExtra.Of(again.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.SampleTypeId))
            .ShouldBe("drums.wav");
    }

    [Fact]
    public void An_empty_patch_prints_to_nothing_much() =>
        PatchLanguage.Build(PatchPrinter.Print(Preset("Empty"), NodeCatalog.BuiltIn), NodeCatalog.BuiltIn)
            .Issues.ShouldBeEmpty();
}
