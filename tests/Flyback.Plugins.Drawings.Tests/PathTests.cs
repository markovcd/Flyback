using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Drawings.Tests;

/// <summary>
/// The Path module: a drawing played as a path, once round a cycle, x to the left
/// speaker and y to the right.
/// </summary>
public sealed class PathTests : IDisposable
{
    private const int Freq = 1;
    private const int Amp = 3;

    private const int X = 0;
    private const int Y = 1;

    private static readonly ModuleCatalog Modules = PluginHost.LoadTypes(typeof(DrawingsPlugin)).Modules;

    private static readonly DrawingExtra Drawing = new();

    private readonly string folder = Directory.CreateTempSubdirectory("flyback-path").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private SampleLibrary Library() => new() { Beside = folder };

    private string Write(string name, string text)
    {
        File.WriteAllText(Path.Combine(folder, name), text);
        return name;
    }

    private string Square() => Write("square.svg", """<svg><path d="M0,0 H10 V10 H0 Z"/></svg>""");

    /// <summary>A Path at <paramref name="freq"/> playing <paramref name="file"/>, x to the left speaker and y to the right.</summary>
    private static (Patch Patch, NodeInstance Path) Playing(string file, float freq = 100f)
    {
        var b = new PatchBuilder(Modules);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        var path = b.Add(PathModule.TypeId, (Freq, freq));

        Drawing.Point(path, file);

        b.Wire(path, X, output, NodeCatalog.OutputLeftPort)
         .Wire(path, Y, output, NodeCatalog.OutputRightPort);

        return (b.Patch, path);
    }

    /// <summary>What the speakers played, left and right, for <paramref name="seconds"/>.</summary>
    private static (float[] Left, float[] Right, int Rate) Heard(CompileResult compiled, double seconds)
    {
        var renderer = new AudioRenderer();
        var buffer = new float[(int)(seconds * renderer.SampleRate) * 2];

        renderer.Render(compiled.Program, buffer, renderer.DelayMemoryFor(compiled.Program));

        return (
            [.. buffer.Where((_, i) => i % 2 == 0)],
            [.. buffer.Where((_, i) => i % 2 == 1)],
            renderer.SampleRate);
    }

    /// <summary>A Path playing <paramref name="file"/> onto a Beam on the Output's screen, played for a quarter second.</summary>
    private CompiledPatch OnABeam(string file, float freq = 100f)
    {
        var b = new PatchBuilder(Modules);

        var output = b.Add(NodeCatalog.OutputTypeId);
        var path = b.Add(PathModule.TypeId, (Freq, freq), (Amp, 0.5f));
        var beam = b.Add(NodeCatalog.BeamTypeId, (3, 0.2f));

        Drawing.Point(path, file);

        b.Wire(path, X, beam, 0)
         .Wire(path, Y, beam, 1)
         .Wire(beam, 0, output, NodeCatalog.OutputColorPort);

        var heard = b.Patch.CompileForAudio(Modules, Library());
        var drawn = b.Patch.CompileForVideo(Modules, samples: Library()).Program;

        heard.Issues.ShouldBeEmpty();

        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(heard.Program);
        renderer.Render(heard.Program, new float[renderer.SampleRate / 4 * 2], memory);

        Traces.Refresh(drawn, heard.Program, memory);
        return drawn;
    }

    /// <summary>How lit the Beam is at a place: the phosphor's green.</summary>
    private static double Lit(CompiledPatch program, double x, double y)
    {
        var registers = program.AllocateRegisters();
        program.Evaluate(x, y, 0d, registers, default, aspect: 16d / 9d);

        return registers[program.OutputBase + 1];
    }

    [Fact]
    public void A_square_drawn_on_a_beam_is_lit_along_its_edges_and_dark_inside()
    {
        var drawn = OnABeam(Square());

        double[] edges = [Lit(drawn, 0.5, 0), Lit(drawn, 0, 0.5), Lit(drawn, -0.5, 0), Lit(drawn, 0, -0.5)];

        edges.ShouldAllBe(lit => lit > 0.2);
        Lit(drawn, 0, 0).ShouldBeLessThan(0.01);
        Lit(drawn, 0.25, 0.25).ShouldBeLessThan(0.01);
        Lit(drawn, 0.8, 0).ShouldBeLessThan(0.01);
    }

    /// <summary>
    /// What constant speed is for: a beam lays down the same energy every moment, so
    /// a side four times as long, crossed in four times as long, is as bright.
    /// </summary>
    [Fact]
    public void A_long_side_and_a_short_one_are_equally_bright()
    {
        var drawn = OnABeam(Write("wide.svg", """<svg><path d="M0,0 H40 V10 H0 Z"/></svg>"""));

        // Half a picture height wide, an eighth tall.
        var top = Lit(drawn, 0, 0.125);
        var side = Lit(drawn, 0.5, 0);

        // Within a seventh: each side is read at one point, which lands at a
        // different place across the beam's width.
        top.ShouldBeGreaterThan(0.1);
        side.ShouldBe(top, top * 0.15);
    }

    /// <summary>
    /// Drawn rather than heard, the phase is the clock times the pitch, so once round
    /// the square at one hertz reaches each side, at the size 'amp' says.
    /// </summary>
    [Fact]
    public void The_picture_reads_the_drawing_too_at_the_size_amp_says()
    {
        var b = new PatchBuilder(Modules);
        var output = b.Add(NodeCatalog.OutputTypeId);
        var path = b.Add(PathModule.TypeId, (Freq, 1f), (Amp, 0.5f));

        Drawing.Point(path, Square());
        b.Wire(path, X, output, NodeCatalog.OutputColorPort);

        var drawn = b.Patch.CompileForVideo(Modules, samples: Library());

        drawn.Issues.ShouldBeEmpty();

        var across = Enumerable.Range(0, 100).Select(i =>
        {
            var registers = drawn.Program.AllocateRegisters();
            drawn.Program.Evaluate(0d, 0d, i / 100d, registers, default);
            return registers[drawn.Program.OutputBase];
        }).ToList();

        across.Min().ShouldBe(-0.5d, 1e-4);
        across.Max().ShouldBe(0.5d, 1e-4);
        across[0].ShouldBe(-0.5d, 1e-4);
    }

    [Fact]
    public void A_file_that_is_not_there_is_named_and_the_module_stays_at_the_center()
    {
        var (patch, path) = Playing("gone.svg");

        var compiled = patch.CompileForAudio(Modules, Library());
        var issue = compiled.Issues.ShouldHaveSingleItem();

        issue.NodeId.ShouldBe(path.Id);
        issue.Severity.ShouldBe(IssueSeverity.Error);
        issue.Message.ShouldContain("gone.svg");
        issue.Message.ShouldContain("no file there");
        Heard(compiled, 0.02).Left.ShouldAllBe(sample => sample == 0f);
    }

    [Fact]
    public void A_kind_of_file_it_cannot_read_says_which_it_can()
    {
        var compiled = Playing(Write("song.mid", "MThd")).Patch.CompileForAudio(Modules, Library());

        compiled.Issues.ShouldHaveSingleItem().Message.ShouldContain(".svg, an .obj or a .png");
    }

    [Fact]
    public void A_module_with_no_drawing_chosen_says_so()
    {
        var issue = Playing(string.Empty).Patch.CompileForAudio(Modules, Library()).Issues.ShouldHaveSingleItem();

        issue.Severity.ShouldBe(IssueSeverity.Warning);
        issue.Message.ShouldContain("no drawing chosen");
    }

    [Fact]
    public void Without_a_library_nothing_can_open_a_drawing()
    {
        Playing("square.svg").Patch.CompileForAudio(Modules).Issues.ShouldHaveSingleItem()
            .Message.ShouldContain("nothing here can open a drawing");
    }

    [Fact]
    public void A_path_comes_back_from_the_text_with_its_drawing()
    {
        var (patch, _) = Playing("shapes/star.svg", freq: 55f);

        var source = PatchPrinter.Print(patch, Modules);

        source.ShouldContain("path(\"shapes/star.svg\"");

        var again = PatchLanguage.Build(source, Modules);

        again.Issues.ShouldBeEmpty(again.Report);
        Drawing.PathOf(again.Patch.Nodes.Single(n => n.TypeId == PathModule.TypeId)).ShouldBe("shapes/star.svg");
    }

    [Fact]
    public void Its_file_is_one_a_bundle_carries_and_a_move_renames()
    {
        var (patch, path) = Playing("star.svg");
        var def = Modules.Require(PathModule.TypeId);
        var extra = def.Extra<DrawingExtra>().ShouldNotBeNull();

        extra.Files(path).ShouldBe(["star.svg"]);

        extra.Rebase(path, name => "moved/" + name);
        Drawing.PathOf(path).ShouldBe("moved/star.svg");
        patch.Nodes.ShouldContain(path);
    }
}
