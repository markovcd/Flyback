using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// What a cycle means to the eye. <see cref="CycleInvariants"/> pins the ear's
/// half — one sample of latency, carried in a cell — and these pin the other: one
/// frame of latency, carried in a plane, which is a value per pixel and belongs
/// to that pixel alone. See ADR-0074.
/// </summary>
/// <remarks>
/// Written through <see cref="SynthRenderer"/> rather than against the
/// interpreter, because the claim is about frames: what a pixel reads is what it
/// wrote when the picture before was drawn, and nothing below the renderer has a
/// frame to be the previous one.
/// </remarks>
public class PlaneInvariants
{
    private const int Width = 8;
    private const int Height = 8;

    /// <summary>
    /// A loop that adds a knob to itself, which is the smallest patch whose
    /// picture depends on the picture before it without sampling one.
    /// </summary>
    private static CompiledPatch Accumulator(float step = 0.25f)
    {
        var b = new PatchBuilder();

        var unit = b.Add(NodeCatalog.UnitDelayTypeId, 200, 0);
        var add = b.Add("math.add", 400, 0, (1, step));
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0);

        b.Wire(unit, 0, add, 0)
         .Wire(add, 0, unit, 0)
         .Wire(unit, 0, sink, 0);

        return b.Patch.CompileForVideo().Program;
    }

    /// <summary>The frames a program draws, as the brightness of one pixel in each.</summary>
    private static float[] Frames(CompiledPatch program, int count, int x = 4, int y = 4)
    {
        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var pixels = new byte[stride * Height];
        var seen = new float[count];

        for (var frame = 0; frame < count; frame++)
        {
            renderer.Render(program, frame / 60d, Width, Height, pixels, stride);
            seen[frame] = pixels[y * stride + x * 4 + 2] / 255f;
        }

        return seen;
    }

    // --- the frame of latency ------------------------------------------------

    /// <summary>
    /// The whole contract, as the ear's version of it: what a pixel reads is what
    /// it wrote last frame, never this frame — so a loop adding a quarter each
    /// time climbs one step per picture rather than running away within one.
    /// </summary>
    [Fact]
    public void A_pixel_reads_what_it_wrote_into_the_frame_before()
    {
        var seen = Frames(Accumulator(), 4);

        seen[0].ShouldBe(0f);
        seen[1].ShouldBe(0.25f, 0.01f);
        seen[2].ShouldBe(0.5f, 0.01f);
        seen[3].ShouldBe(0.75f, 0.01f);
    }

    /// <summary>
    /// A plane is the pixel's own. Driven by a coordinate, the picture has to come
    /// out a gradient that deepens frame by frame — where one cell shared between
    /// pixels would give a flat field of whichever pixel ran last, and rows that
    /// ran in parallel would make even that unrepeatable.
    /// </summary>
    [Fact]
    public void A_plane_belongs_to_one_pixel_and_no_other()
    {
        var b = new PatchBuilder();

        var coord = b.Add("coord", 0, 0);
        var scaled = b.Add("math.mul", 200, 0, (1, 0.1f));
        var unit = b.Add(NodeCatalog.UnitDelayTypeId, 400, 0);
        var add = b.Add("math.add", 600, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 800, 0);

        b.Wire(coord, 0, scaled, 0)
         .Wire(unit, 0, add, 0)
         .Wire(scaled, 0, add, 1)
         .Wire(add, 0, unit, 0)
         .Wire(unit, 0, sink, 0);

        var program = b.Patch.CompileForVideo().Program;

        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var pixels = new byte[stride * Height];

        for (var frame = 0; frame < 3; frame++)
            renderer.Render(program, frame / 60d, Width, Height, pixels, stride);

        float At(int x) => pixels[4 * stride + x * 4 + 2] / 255f;

        // Two frames of x/10 have gone in, so the right-hand columns hold twice a
        // tenth of their own x and the left-hand ones hold a negative the screen
        // clamps to black.
        At(7).ShouldBe(0.2f * 0.875f, 0.01f);
        At(5).ShouldBe(0.2f * 0.375f, 0.01f);
        At(0).ShouldBe(0f);
    }

    /// <summary>
    /// A rewind puts the patch back at nought, and what it was carrying round goes
    /// with it — otherwise a restarted feedback patch would resume from a picture
    /// that is no longer on the timeline.
    /// </summary>
    [Fact]
    public void A_reset_empties_the_planes()
    {
        var program = Accumulator();
        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var pixels = new byte[stride * Height];

        for (var frame = 0; frame < 4; frame++)
            renderer.Render(program, frame / 60d, Width, Height, pixels, stride);

        pixels[4 * stride + 4 * 4 + 2].ShouldBeGreaterThan((byte)0);

        renderer.Reset();
        renderer.Render(program, 0d, Width, Height, pixels, stride);

        pixels[4 * stride + 4 * 4 + 2].ShouldBe((byte)0);
    }

    // --- what it costs -------------------------------------------------------

    /// <summary>
    /// A patch with no loop in it asks for no plane, and so pays none of this in
    /// memory. The presets are the check because between them they reach every
    /// module in the catalogue, filters and envelopes included — a cell is not a
    /// plane, and nothing but a cycle may claim one.
    /// </summary>
    [Fact]
    public void A_patch_with_no_loop_keeps_no_planes()
    {
        foreach (var preset in Presets.All)
        {
            var program = preset.Build(NodeCatalog.Current).CompileForVideo().Program;

            program.PlaneCount.ShouldBe(0, preset.Name);
        }
    }

    /// <summary>
    /// Both halves of a plane belong to the pixel's share of the frame. Hoisted
    /// into the row's or the frame's, a read would answer for whichever pixel ran
    /// last and a write would put one pixel's value into every pixel's plane.
    /// </summary>
    [Fact]
    public void Both_halves_of_a_plane_run_once_per_pixel()
    {
        var plan = Accumulator().Plan.ShouldNotBeNull();
        var (pixel, _) = plan.Range(EvaluationStage.Pixel);

        var planes = plan.Ops
            .Index()
            .Where(o => o.Item.Code is OpCode.PlaneRead or OpCode.PlaneWrite)
            .ToArray();

        planes.Length.ShouldBe(2);

        foreach (var (at, op) in planes)
            at.ShouldBeGreaterThanOrEqualTo(pixel, $"{op.Code} runs outside the pixel's share");

        // And in the order they were emitted, which is the frame of latency.
        planes[0].Item.Code.ShouldBe(OpCode.PlaneRead);
        planes[1].Item.Code.ShouldBe(OpCode.PlaneWrite);
    }

    /// <summary>
    /// The shader keeps a plane in a render target of its own: read at the texel
    /// this fragment is, written back at the end of the pass, and carried between
    /// frames by the same pair the history ping-pongs between.
    /// </summary>
    [Fact]
    public void The_shader_reads_a_plane_by_texel_and_writes_it_to_its_own_target()
    {
        var source = GlslEmitter.Emit(Accumulator(), GlslDialect.GlslEs300);

        source.PlaneTargets.ShouldBe(1);

        var fragment = source.PatchFragment;

        fragment.ShouldContain("uniform sampler2D uPlane0;");
        fragment.ShouldContain("layout(location = 1) out vec4 outPlane0;");

        // By texel and not by sample: a plane is this pixel's, and a filtered read
        // would blend the neighbours it is defined not to see.
        fragment.ShouldContain("texelFetch(uPlane0, ivec2(gl_FragCoord.xy), 0)");

        // Bounded on the way in, as the interpreter's is, and handed to the target
        // whether this pass wrote it or not.
        fragment.ShouldContain("pl0 = bd(");
        fragment.ShouldContain("outPlane0 = vec4(pl0, 0.0, 0.0, 0.0);");
    }

    /// <summary>
    /// Desktop GLSL 1.50 has no location qualifier on a fragment output, so the
    /// shader declares plain outputs there and <c>GpuFrameRenderer</c> says which
    /// attachment each goes to before it links. Emitting the qualifier anyway
    /// would fail to compile on exactly the machines that path exists for.
    /// </summary>
    [Fact]
    public void The_desktop_dialect_leaves_the_attachment_numbers_to_the_renderer()
    {
        var fragment = GlslEmitter.Emit(Accumulator(), GlslDialect.Glsl150).PatchFragment;

        fragment.ShouldContain("out vec4 outPlane0;");
        fragment.ShouldNotContain("layout(location");
    }

    /// <summary>
    /// And a patch with no loop asks for no target, so nothing about the shader
    /// or the framebuffers it draws into changes for the patches that had none.
    /// </summary>
    [Fact]
    public void A_patch_with_no_loop_asks_for_no_target()
    {
        var plasma = Presets.Plasma(NodeCatalog.BuiltIn).CompileForVideo(NodeCatalog.BuiltIn).Program;
        var source = GlslEmitter.Emit(plasma, GlslDialect.GlslEs300);

        source.PlaneTargets.ShouldBe(0);
        source.PatchFragment.ShouldNotContain("uPlane");
        source.PatchFragment.ShouldContain("out vec4 fragColor;");
    }

    // --- what survives an edit ----------------------------------------------

    /// <summary>
    /// A plane follows the module that claimed it, so an edit that renumbers the
    /// planes — deleting one loop of two, say — leaves the other still carrying
    /// what it was carrying. Without this, a simulation that had been running for
    /// a minute would restart on an edit somewhere else in the patch.
    /// </summary>
    [Fact]
    public void A_plane_is_kept_by_owner_across_a_recompile()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var both = Program([first, second]);
        var state = new PlaneState();

        state.Fit(both, 2, 1);

        for (var pixel = 0; pixel < 2; pixel++)
        {
            state.At(pixel)[0] = 0.5f + pixel;
            state.At(pixel)[1] = 9f;
        }

        // The same two modules, the other way round: each plane has moved slot and
        // must take its values with it.
        state.Fit(Program([second, first]), 2, 1);

        state.At(0)[1].ShouldBe(0.5f);
        state.At(1)[1].ShouldBe(1.5f);
        state.At(0)[0].ShouldBe(9f);

        // And the one that is gone leaves its plane behind rather than handing it
        // to whoever is in that slot now.
        state.Fit(Program([Guid.NewGuid()]), 2, 1);

        state.Count.ShouldBe(1);
        state.At(0)[0].ShouldBe(0f);
        state.At(1)[0].ShouldBe(0f);
    }

    /// <summary>
    /// A frame of another shape keeps nothing: a plane is indexed by pixel rather
    /// than by coordinate, so there is nothing in one to resample into the other.
    /// </summary>
    [Fact]
    public void A_plane_starts_again_when_the_frame_changes_shape()
    {
        var state = new PlaneState();
        var program = Program([Guid.NewGuid()]);

        state.Fit(program, 2, 1);
        state.At(0)[0] = 1f;

        state.Fit(program, 4, 1);

        state.Count.ShouldBe(1);
        state.Width.ShouldBe(4);
        state.At(0)[0].ShouldBe(0f);
    }

    /// <summary>A program that reads one plane per owner, and does nothing else with them.</summary>
    private static CompiledPatch Program(Guid[] owners)
    {
        var ops = owners
            .Select((_, slot) => new Op(OpCode.PlaneRead, slot, k: slot))
            .ToArray();

        return new CompiledPatch(ops, owners.Length, 0, 1, owners: new StateOwners([], [], [], owners));
    }
}
